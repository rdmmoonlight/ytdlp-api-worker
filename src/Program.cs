using System;
using System.IO;
using System.Net;
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
        Console.WriteLine("=== MD to OneNote Uploader (Dengan Cek Duplikat) ===");

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

        string? accessToken = await PollForAccessTokenAsync(httpClient, clientId, deviceCodeInfo.Value.DeviceCode, deviceCodeInfo.Value.Interval);

        if (string.IsNullOrEmpty(accessToken))
        {
            Console.WriteLine("[Error] Gagal mendapatkan Access Token.");
            return;
        }

        Console.WriteLine("\n[Berhasil] Otorisasi sukses! Memulai pemeriksaan catatan...\n");

        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

        string[] mdFiles = Directory.GetFiles(markdownFolderPath, "*.md", SearchOption.AllDirectories);
        Console.WriteLine($"Ditemukan {mdFiles.Length} file .md.\n");

        int totalSukses = 0;
        int totalSkipped = 0;
        int totalGagal = 0;

        foreach (var filePath in mdFiles)
        {
            string relativePath = Path.GetRelativePath(markdownFolderPath, filePath);
            string pageTitle = Path.ChangeExtension(relativePath, null).Replace('\\', '/');

            // 1. Cek Apakah Halaman Sudah Ada di OneNote
            bool exists = await CheckPageExistsAsync(httpClient, pageTitle);
            if (exists)
            {
                Console.WriteLine($"[Skip] '{pageTitle}' sudah ada di OneNote.");
                totalSkipped++;
                continue;
            }

            Console.WriteLine($"Processing: {relativePath}...");

            bool isUploaded = false;
            int maxRetry = 3;

            for (int retry = 1; retry <= maxRetry; retry++)
            {
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
                        totalSukses++;
                        isUploaded = true;
                        break;
                    }
                    else if (response.StatusCode == (HttpStatusCode)429 || response.StatusCode == HttpStatusCode.ServiceUnavailable)
                    {
                        Console.WriteLine($"[Rate Limit] Server sibuk. Coba lagi ({retry}/{maxRetry}) dalam 5 detik...");
                        await Task.Delay(5000);
                    }
                    else
                    {
                        string errorBody = await response.Content.ReadAsStringAsync();
                        Console.WriteLine($"[Gagal] '{pageTitle}' (Status: {response.StatusCode}): {errorBody}\n");
                        break;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Gagal] '{relativePath}': {ex.Message}\n");
                    break;
                }
            }

            if (!isUploaded) totalGagal++;
            await Task.Delay(1500);
        }

        Console.WriteLine("\n=======================================================");
        Console.WriteLine($" Ringkasan:");
        Console.WriteLine($" Total File : {mdFiles.Length}");
        Console.WriteLine($" Di-Skip    : {totalSkipped} (Sudah ada di OneNote)");
        Console.WriteLine($" Sukses Upload Baru: {totalSukses}");
        Console.WriteLine($" Gagal      : {totalGagal}");
        Console.WriteLine("=======================================================");
    }

    private static async Task<bool> CheckPageExistsAsync(HttpClient client, string pageTitle)
    {
        try
        {
            // Query ke OneNote Graph API untuk mencari halaman berdasarkan judul
            string encodedTitle = Uri.EscapeDataString(pageTitle);
            var response = await client.GetAsync($"https://graph.microsoft.com/v1.0/me/onenote/pages?$filter=title eq '{encodedTitle}'&$select=id");
            
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("value", out var valueArray) && valueArray.GetArrayLength() > 0)
                {
                    return true; // Halaman sudah ada
                }
            }
        }
        catch { }

        return false;
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
                if (error == "authorization_pending") continue;
            }

            return null;
        }
    }
}
