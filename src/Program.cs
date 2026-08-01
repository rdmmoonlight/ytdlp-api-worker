using System;
using System.Collections.Generic;
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
        Console.WriteLine("=== MD to OneNote Uploader (User Context / RefreshToken Mode) ===");

        string? clientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID");
        string? clientSecret = Environment.GetEnvironmentVariable("AZURE_CLIENT_SECRET");
        string? refreshToken = Environment.GetEnvironmentVariable("AZURE_REFRESH_TOKEN");
        string markdownFolderPath = Environment.GetEnvironmentVariable("MD_FOLDER_PATH") ?? "./docs";

        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(refreshToken))
        {
            Console.WriteLine("[Error] AZURE_CLIENT_ID dan AZURE_REFRESH_TOKEN wajib diisi!");
            return;
        }

        if (!Directory.Exists(markdownFolderPath))
        {
            Console.WriteLine($"[Error] Folder markdown tidak ditemukan: {Path.GetFullPath(markdownFolderPath)}");
            return;
        }

        // 1. Tukar Refresh Token menjadi Access Token
        Console.WriteLine("Mendapatkan Access Token dari Microsoft Azure...");
        string? accessToken = await GetAccessTokenAsync(clientId, clientSecret, refreshToken);

        if (string.IsNullOrEmpty(accessToken))
        {
            Console.WriteLine("[Error] Gagal mendapatkan Access Token. Periksa kembali Client ID / Refresh Token.");
            return;
        }

        using var httpClient = new HttpClient();
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

    /// <summary>
    /// Helper method untuk menukar Refresh Token menjadi Access Token beserta diagnosa log
    /// </summary>
    private static async Task<string?> GetAccessTokenAsync(string clientId, string? clientSecret, string refreshToken)
    {
        using var client = new HttpClient();
        var requestData = new Dictionary<string, string>
        {
            { "client_id", clientId },
            { "grant_type", "refresh_token" },
            { "refresh_token", refreshToken },
            { "scope", "offline_access Notes.ReadWrite" },
            { "redirect_uri", "http://localhost" }
        };

        if (!string.IsNullOrEmpty(clientSecret))
        {
            requestData.Add("client_secret", clientSecret);
        }

        var response = await client.PostAsync("https://login.microsoftonline.com/common/oauth2/v2.0/token", new FormUrlEncodedContent(requestData));
        var json = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"[Azure Error Detail] HTTP {(int)response.StatusCode}: {json}");
            return null;
        }

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("access_token", out var tokenProp))
        {
            return tokenProp.GetString();
        }

        return null;
    }
}
