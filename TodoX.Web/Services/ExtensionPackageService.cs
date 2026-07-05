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
            foreach (var file in Directory.EnumerateFiles(templateRoot))
            {
                var entryName = Path.GetFileName(file);
                if (string.Equals(entryName, "config.json", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
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
            var configEntry = archive.CreateEntry("config.json", CompressionLevel.Fastest);
            await using var configStream = configEntry.Open();
            await configStream.WriteAsync(Encoding.UTF8.GetBytes(configJson), ct);
        }

        return new ExtensionPackageResult(fileName, "application/zip", output.ToArray());
    }
}
