using System.IO.Compression;
using System.Text;
using System.Text.Json;
using TodoX.Web.Models;

namespace TodoX.Web.Services;

public sealed class ExtensionPackageService
{
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _configuration;
    private readonly ExtensionTokenService _tokens;

    public ExtensionPackageService(
        IWebHostEnvironment env,
        IConfiguration configuration,
        ExtensionTokenService tokens)
    {
        _env = env;
        _configuration = configuration;
        _tokens = tokens;
    }

    public async Task<ExtensionPackageResult> CreateForUserAsync(
        Guid customerId,
        Guid userId,
        CancellationToken ct = default)
    {
        var templateRoot = Path.Combine(_env.WebRootPath, "extension-template");
        if (!Directory.Exists(templateRoot))
        {
            throw new DirectoryNotFoundException(
                $"Khong tim thay thu muc Chrome Extension template: {templateRoot}. " +
                "Hay kiem tra TodoX.Web/wwwroot/extension-template da duoc commit va publish len server.");
        }

        await ValidateTemplateAsync(templateRoot, ct);

        var issued = await _tokens.IssueAsync(customerId, userId, "TodoX Chrome Extension", ct);
        var publicBaseUrl = (_configuration["TodoX:PublicBaseUrl"] ?? "https://dashboard.todox.vn").TrimEnd('/');
        var generatedAt = DateTimeOffset.UtcNow;
        var fileName = $"TodoX-Chrome-Extension-{generatedAt:yyyyMMddHHmmss}.zip";
        const string zipRoot = "TodoX-Chrome-Extension";

        await using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in Directory.EnumerateFiles(templateRoot, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();

                var relativeName = Path.GetRelativePath(templateRoot, file).Replace("\\", "/");
                if (ShouldSkipExtensionTemplateFile(relativeName))
                {
                    continue;
                }

                await AddFileEntryAsync(archive, $"{zipRoot}/{relativeName}", file, ct);
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

            await AddTextEntryAsync(archive, $"{zipRoot}/config.json", configJson, ct);
            await AddTextEntryAsync(archive, $"{zipRoot}/README_CAI_DAT.txt", BuildReadme(), ct);
        }

        return new ExtensionPackageResult(fileName, "application/zip", output.ToArray());
    }

    private static async Task ValidateTemplateAsync(string templateRoot, CancellationToken ct)
    {
        var requiredFiles = new[]
        {
            "manifest.json",
            "background.js",
            "content.js",
            "styles.css"
        };

        foreach (var required in requiredFiles)
        {
            var path = Path.Combine(templateRoot, required);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Thieu file Chrome Extension template: {required}", path);
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
                    "manifest.json co khai bao youtube-content.js nhung thieu file youtube-content.js.",
                    youtubePath);
            }
        }
    }

    private static bool ShouldSkipExtensionTemplateFile(string entryName)
    {
        if (string.IsNullOrWhiteSpace(entryName))
        {
            return true;
        }

        var lower = entryName.Replace("\\", "/").ToLowerInvariant();
        if (lower.EndsWith(".br") ||
            lower.EndsWith(".gz") ||
            lower.EndsWith(".map") ||
            lower.EndsWith(".tmp"))
        {
            return true;
        }

        return lower is "config.json" or "config.template.json" ||
               lower.EndsWith("/config.json") ||
               lower.EndsWith("/config.template.json");
    }

    private static async Task AddFileEntryAsync(
        ZipArchive archive,
        string entryName,
        string sourcePath,
        CancellationToken ct)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);

        await using var entryStream = entry.Open();
        await using var input = File.OpenRead(sourcePath);

        await input.CopyToAsync(entryStream, ct);
    }

    private static async Task AddTextEntryAsync(
        ZipArchive archive,
        string entryName,
        string content,
        CancellationToken ct)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);

        await using var entryStream = entry.Open();
        await using var writer = new StreamWriter(
            entryStream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            bufferSize: 1024,
            leaveOpen: false);

        await writer.WriteAsync(content.AsMemory(), ct);
        await writer.FlushAsync(ct);
    }

    private static string BuildReadme() =>
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
