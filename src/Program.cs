using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Azure.Identity;
using Markdig;
using Microsoft.Graph;
using Microsoft.Kiota.Abstractions;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== MD to OneNote Uploader (Cloud / Unattended Mode) ===");

        // Membaca credential dari Environment Variables
        string? clientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID");
        string? tenantId = Environment.GetEnvironmentVariable("AZURE_TENANT_ID");
        string? clientSecret = Environment.GetEnvironmentVariable("AZURE_CLIENT_SECRET");
        string? targetUserId = Environment.GetEnvironmentVariable("AZURE_TARGET_USER_ID"); // Email/ID Pemilik OneNote
        string markdownFolderPath = Environment.GetEnvironmentVariable("MD_FOLDER_PATH") ?? "./docs";

        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(tenantId) || 
            string.IsNullOrEmpty(clientSecret) || string.IsNullOrEmpty(targetUserId))
        {
            Console.WriteLine("[Error] Environment variables (AZURE_CLIENT_ID, AZURE_TENANT_ID, AZURE_CLIENT_SECRET, AZURE_TARGET_USER_ID) harus diisi!");
            return;
        }

        if (!Directory.Exists(markdownFolderPath))
        {
            Console.WriteLine($"[Error] Folder markdown tidak ditemukan: {Path.GetFullPath(markdownFolderPath)}");
            return;
        }

        // Autentikasi Non-Interaktif menggunakan ClientSecretCredential
        var credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
        var graphClient = new GraphServiceClient(credential, new[] { "https://graph.microsoft.com/.default" });

        var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

        string[] mdFiles = Directory.GetFiles(markdownFolderPath, "*.md", SearchOption.AllDirectories);

        if (mdFiles.Length == 0)
        {
            Console.WriteLine("Tidak ada file .md ditemukan.");
            return;
        }

        Console.WriteLine($"Ditemukan {mdFiles.Length} file .md. Mengunggah ke OneNote user: {targetUserId}...\n");

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

                // PERBAIKAN: Buat RequestInformation secara eksplisit untuk mengirim payload HTML (text/html)
                var requestInfo = new RequestInformation
                {
                    HttpMethod = Method.POST,
                    UrlTemplate = "{+baseurl}/users/{user%2Did}/onenote/pages",
                };
                requestInfo.PathParameters.Add("baseurl", "https://graph.microsoft.com/v1.0");
                requestInfo.PathParameters.Add("user%2Did", targetUserId);

                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(fullHtmlDocument));
                requestInfo.SetStreamContent(stream, "text/html");

                // Kirim request via RequestAdapter dari GraphClient
                await graphClient.RequestAdapter.SendNoContentAsync(requestInfo);
                
                Console.WriteLine($"[Sukses] '{pageTitle}' berhasil diunggah.\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Gagal] '{relativePath}': {ex.Message}\n");
            }
        }
    }
}
