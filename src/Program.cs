using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Markdig;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== MD to OneNote Uploader (Device Code Flow) ===");

        string? clientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID");
        string markdownFolderPath = Environment.GetEnvironmentVariable("MD_FOLDER_PATH") ?? "./docs";

        if (string.IsNullOrEmpty(clientId))
        {
            Console.WriteLine("[Error] AZURE_CLIENT_ID wajib diisi!");
            return;
        }

        if (!Directory.Exists(markdownFolderPath))
        {
            Console.WriteLine($"[Error] Folder markdown tidak ditemukan: {Path.GetFullPath(markdownFolderPath)}");
            return;
        }

        using var httpClient = new HttpClient();

        // 1. Minta Device Code dari Azure
        Console.WriteLine("Memulai otorisasi akun Microsoft...");
        var deviceCodeInfo = await RequestDeviceCodeAsync(httpClient, clientId);

        if (deviceCodeInfo == null)
        {
            Console.WriteLine("[Error] Gagal mendapatkan Device Code dari Azure.");
            return;
        }

        Console.WriteLine("\n=======================================================");
        Console.WriteLine($"1. Buka browser di HP/Laptop: {deviceCodeInfo.Value.VerificationUri}");
        Console.WriteLine($"2. Masukkan Kode Ini: {deviceCodeInfo.Value.UserCode}");
        Console.WriteLine("=======================================================\n");

        // 2. Poll server Azure sampai user memasukkan kode di browser
        string? accessToken = await PollForAccessTokenAsync(httpClient, clientId, deviceCodeInfo.Value.DeviceCode, deviceCodeInfo.Value.Interval);

        if (string.IsNullOrEmpty(accessToken))
        {
            Console.WriteLine("[Error] Gagal mendapatkan Access Token / Otorisasi dibatalkan.");
            return;
        }

        Console.WriteLine("\n[Berhasil] Otorisasi sukses! Memulai pengunggahan catatan...\n");

        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

        string[] mdFiles = Directory.GetFiles(markdownFolderPath, "*.md", SearchOption.AllDirectories);

        if (mdFiles.Length == 0)
        {
            Console.WriteLine("Tidak ada file .md ditemukan.");
            return;
        }

        Console.WriteLine($"Ditemukan {mdFiles.Length} file .md. Mengunggah ke OneNote...\n");

        foreach (var filePath in mdFiles)
        {
            string relativePath = Path.GetRelativePath(markdownFolderPath, filePath);
            string pageTitle = Path.ChangeExtension(relativePath, null).Replace('\\', '/');

            Console.WriteLine($"Processing: {relativePath}...");

            try
            {
                string mdContent = await File.ReadAllTextAsync(filePath);
                string htmlBody = Markdown.ToHtml(mdContent, pipeline);

                string fullHtmlDocument = $@"<!DOCTYPE html>
<html>
  <head>
    <title>{pageTitle}</title>
    <meta name=""created"" content=""{DateTime.Now:yyyy-MM-ddTHH:mm:ssK}"" />
  </head>
  <body>
    {htmlBody}
  </body>
</html>";

                using var request = new HttpRequestMessage(HttpMethod.Post, "https://graph.microsoft.com/v1.0/me/onenote/pages");
                request.Content = new StringContent(fullHtmlDocument, Encoding.UTF8, "text/html");

                var response = await httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[Sukses] '{pageTitle}' berhasil diunggah.\n");
                }
                else
                {
                    string errorBody = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"[Gagal] '{pageTitle}' (Status: {response.StatusCode}): {errorBody}\n");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Gagal] '{relativePath}': {ex.Message}\n");
            }
        }
    }

    private static async Task<(string DeviceCode, string UserCode, string VerificationUri, int Interval)?> RequestDeviceCodeAsync(HttpClient client, string clientId)
    {
        var data = new Dictionary<string, string>
        {
            { "client_id", clientId },
            { "scope", "offline_access Notes.ReadWrite" }
        };

        var response = await client.PostAsync("https://login.microsoftonline.com/common/oauth2/v2.0/devicecode", new FormUrlEncodedContent(data));
        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        return (
            root.GetProperty("device_code").GetString()!,
            root.GetProperty("user_code").GetString()!,
            root.GetProperty("verification_uri").GetString()!,
            root.GetProperty("interval").GetInt32()
        );
    }

    private static async Task<string?> PollForAccessTokenAsync(HttpClient client, string clientId, string deviceCode, int interval)
    {
        var data = new Dictionary<string, string>
        {
            { "client_id", clientId },
            { "grant_type", "urn:ietf:params:oauth:grant-type:device_code" },
            { "device_code", deviceCode }
        };

        while (true)
        {
            await Task.Delay(interval * 1000);

            var response = await client.PostAsync("https://login.microsoftonline.com/common/oauth2/v2.0/token", new FormUrlEncodedContent(data));
            var json = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (response.IsSuccessStatusCode)
            {
                return root.GetProperty("access_token").GetString();
            }

            if (root.TryGetProperty("error", out var errorProp))
            {
                string error = errorProp.GetString()!;
                if (error == "authorization_pending")
                {
                    continue; // Masih menunggu pengguna memasukkan kode di browser
                }
            }

            Console.WriteLine($"[Error Device Login] {json}");
            return null;
        }
    }
}
