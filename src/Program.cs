using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Azure.Identity;
using Markdig;
using Microsoft.Graph;

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

        // Autentikasi Non-Interaktif menggunakan ClientSecretCredential (App Permissions)
        var options = new ClientSecretCredentialOptions { TenantId = tenantId };
        var credential = new ClientSecretCredential(tenantId, clientId, clientSecret, options);
        var graphClient = new GraphServiceClient(credential, new[] { "https://graph.microsoft.com/.default" });

        var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
        string[] mdFiles = Directory.GetFiles(markdownFolderPath, "*.md", SearchOption.TopDirectoryOnly);

        if (mdFiles.Length == 0)
        {
            Console.WriteLine("Tidak ada file .md ditemukan.");
            return;
        }

        Console.WriteLine($"Ditemukan {mdFiles.Length} file .md. Mengunggah ke OneNote user: {targetUserId}...\n");

        foreach (var filePath in mdFiles)
        {
            string fileName = Path.GetFileNameWithoutExtension(filePath);
            Console.WriteLine($"Processing: {Path.GetFileName(filePath)}...");

            try
            {
                string mdContent = await File.ReadAllTextAsync(filePath);
                string htmlBody = Markdown.ToHtml(mdContent, pipeline);

                string fullHtmlDocument = $@"<!DOCTYPE html>
<html>
  <head>
    <title>{fileName}</title>
    <meta name=""created"" content=""{DateTime.Now:yyyy-MM-ddTHH:mm:ssK}"" />
  </head>
  <body>
    {htmlBody}
  </body>
</html>";

                // Kirim ke OneNote milik spesifik User ID / Email
                var requestInfo = graphClient.Users[targetUserId].Onenote.Pages.ToPostRequestInformation(
                    new MemoryStream(Encoding.UTF8.GetBytes(fullHtmlDocument)),
                    config => config.Headers.Add("Content-Type", "text/html")
                );

                await graphClient.RequestAdapter.SendNoContentAsync(requestInfo);
                Console.WriteLine($"[Sukses] '{fileName}' berhasil diunggah.\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Gagal] '{fileName}': {ex.Message}\n");
            }
        }
    }
}