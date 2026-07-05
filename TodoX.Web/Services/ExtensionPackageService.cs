using System.IO.Compression;
using System.Text;
using System.Text.Json;
using TodoX.Web.Models;

namespace TodoX.Web.Services;

public sealed class ExtensionPackageService
{
    private const string ZipRoot = "TodoX-Chrome-Extension";

    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _configuration;
    private readonly ExtensionTokenService _tokens;

    public ExtensionPackageService(IWebHostEnvironment env, IConfiguration configuration, ExtensionTokenService tokens)
    {
        _env = env;
        _configuration = configuration;
        _tokens = tokens;
    }

    public async Task<ExtensionPackageResult> CreateForUserAsync(Guid customerId, Guid userId, CancellationToken ct = default)
    {
        var issued = await _tokens.IssueAsync(customerId, userId, "TodoX Chrome Extension", ct);
        var publicBaseUrl = (_configuration["TodoX:PublicBaseUrl"] ?? "https://dashboard.todox.vn").TrimEnd('/');
        var generatedAt = DateTimeOffset.UtcNow;
        var fileName = $"TodoX-Chrome-Extension-{generatedAt:yyyyMMddHHmmss}.zip";

        await using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            var templateRoot = Path.Combine(_env.WebRootPath, "extension-template");
            if (!Directory.Exists(templateRoot))
            {
                throw new InvalidOperationException(
                    $"Không tìm thấy thư mục Chrome Extension template: {templateRoot}. " +
                    "Hãy kiểm tra TodoX.Web/wwwroot/extension-template đã được commit và publish lên server.");
            }

            var requiredFiles = new[]
            {
                "manifest.json",
                "background.js",
                "content.js",
                "styles.css"
            };

            foreach (var required in requiredFiles)
            {
                var fullPath = Path.Combine(templateRoot, required);
                if (!File.Exists(fullPath))
                {
                    throw new InvalidOperationException(
                        $"Thiếu file Chrome Extension template: {required}. " +
                        "Hãy kiểm tra thư mục wwwroot/extension-template.");
                }
            }

            var manifestPath = Path.Combine(templateRoot, "manifest.json");
            var manifestText = await File.ReadAllTextAsync(manifestPath, ct);
            if (manifestText.Contains("youtube-content.js", StringComparison.OrdinalIgnoreCase))
            {
                var youtubePath = Path.Combine(templateRoot, "youtube-content.js");
                if (!File.Exists(youtubePath))
                {
                    throw new FileNotFoundException(
                        "manifest.json có khai báo youtube-content.js nhưng thiếu file youtube-content.js.",
                        youtubePath);
                }
            }

            foreach (var file in Directory.EnumerateFiles(templateRoot, "*", SearchOption.AllDirectories))
            {
                var entryName = Path.GetRelativePath(templateRoot, file).Replace("\\", "/");
                if (ShouldSkipExtensionTemplateFile(entryName))
                {
                    continue;
                }

                var entry = archive.CreateEntry($"{ZipRoot}/{entryName}", CompressionLevel.Fastest);
                await using var entryStream = entry.Open();
                await using var input = File.OpenRead(file);
                await input.CopyToAsync(entryStream, ct);
            }

            var config = new
            {
                apiBaseUrl = publicBaseUrl,
                extensionToken = issued.RawToken,
                customerId,
                userId,
                generatedAt
            };
            var configJson = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            var configEntry = archive.CreateEntry($"{ZipRoot}/config.json", CompressionLevel.Fastest);
            await using var configStream = configEntry.Open();
            await configStream.WriteAsync(Encoding.UTF8.GetBytes(configJson), ct);

            var readmeEntry = archive.CreateEntry($"{ZipRoot}/README_CAI_DAT.txt", CompressionLevel.Fastest);
            await using var readmeStream = readmeEntry.Open();
            await using var readmeWriter = new StreamWriter(readmeStream, Encoding.UTF8);
            await readmeWriter.WriteAsync(GetInstallReadme());
        }

        return new ExtensionPackageResult(fileName, "application/zip", output.ToArray());
    }

    private static bool ShouldSkipExtensionTemplateFile(string entryName)
    {
        if (string.IsNullOrWhiteSpace(entryName))
        {
            return true;
        }

        var lower = entryName.ToLowerInvariant();
        if (lower.EndsWith(".br") || lower.EndsWith(".gz") || lower.EndsWith(".map"))
        {
            return true;
        }

        return lower is "config.json" or "config.template.json";
    }

    private static string GetInstallReadme() =>
        """
        HUONG DAN CAI TODOX CHROME EXTENSION

        1. Giai nen file ZIP nay ra mot thu muc.
        2. Mo Chrome.
        3. Truy cap: chrome://extensions
        4. Bat Developer mode o goc phai.
        5. Bam Load unpacked.
        6. Chon thu muc da giai nen, noi co file manifest.json.
        7. Mo TikTok va kiem tra nut [+] todoX tren video.

        Luu y:
        - Khong chon tung file rieng le.
        - Phai chon ca thu muc chua manifest.json.
        - Khong xoa file config.json vi file nay chua token ket noi voi TodoX.
        """;
}
