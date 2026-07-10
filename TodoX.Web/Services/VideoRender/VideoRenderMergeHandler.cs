using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TodoX.Web.Models;
using TodoX.Web.Services.Render;

namespace TodoX.Web.Services.VideoRender;

public sealed class VideoRenderMergeHandler : IRenderJobHandler
{
    public const string JobTypeName = "merge_video_job";
    public string JobType => JobTypeName;

    private readonly ILogger<VideoRenderMergeHandler> _logger;
    private readonly IOptionsMonitor<VideoRenderOptions> _options;
    private readonly VideoRenderRepository _repo;
    private readonly IWebHostEnvironment _env;

    public VideoRenderMergeHandler(ILogger<VideoRenderMergeHandler> logger, IOptionsMonitor<VideoRenderOptions> options, VideoRenderRepository repo, IWebHostEnvironment env)
    {
        _logger = logger;
        _options = options;
        _repo = repo;
        _env = env;
    }

    public async Task HandleAsync(RenderJobDto job, CancellationToken ct)
    {
        var projectId = TryReadLong(job.InputJson, "projectId") ?? TryReadLong(job.PromptJson, "projectId") ?? throw new InvalidOperationException("Thieu projectId trong job input.");
        var project = await _repo.GetProjectAsync(projectId, ct) ?? throw new InvalidOperationException("Khong tim thay project video.");
        if (!project.Scenes.Any() || project.Scenes.Any(x => x.Status != VideoSceneStatuses.VideoReady))
        {
            throw new InvalidOperationException("Chua du scene video ready de merge.");
        }

        await _repo.UpdateProjectAsync(project.Id, VideoProjectStatuses.Merging, errorMessage: null, ct: ct);
        var root = ResolveRoot(_options.CurrentValue.StorageRoot);
        var projectRoot = Path.Combine(root, project.JobFolder);
        var finalDir = Path.Combine(projectRoot, "final");
        Directory.CreateDirectory(finalDir);
        var concat = Path.Combine(finalDir, "concat.txt");
        var finalPath = Path.Combine(finalDir, "final.mp4");
        var lines = project.Scenes.OrderBy(x => x.SceneIndex).Select(scene => $"file '{Path.GetFullPath(scene.SceneVideoPath ?? string.Empty).Replace("'", "''")}'").ToArray();
        await File.WriteAllLinesAsync(concat, lines, Encoding.UTF8, ct);

        var ffmpegPath = _options.CurrentValue.FfmpegPath;
        var psi = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = $"-y -f concat -safe 0 -i \"{concat}\" -c copy \"{finalPath}\"",
            WorkingDirectory = finalDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Khong khoi dong duoc FFmpeg.");
        var stdout = await process.StandardOutput.ReadToEndAsync(ct);
        var stderr = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        await File.WriteAllTextAsync(Path.Combine(finalDir, "ffmpeg.log"), stdout + Environment.NewLine + stderr, ct);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"FFmpeg merge failed. ExitCode={process.ExitCode}");
        }

        var relative = Path.GetRelativePath(root, finalPath).Replace(Path.DirectorySeparatorChar, '/');
        var url = $"{_options.CurrentValue.PublicBase.TrimEnd('/')}/{relative}";
        await _repo.UpdateProjectAsync(project.Id, VideoProjectStatuses.Completed, url, finalPath, null, ct);
        await _repo.AddProjectEventAsync(project.Id, "PROJECT_MERGED", "info", "Final video merged.", new { finalPath, url }, ct);
    }

    private string ResolveRoot(string? path)
        => Path.IsPathRooted(path) ? path! : Path.Combine(_env.ContentRootPath, path ?? string.Empty);

    private static long? TryReadLong(string json, string key)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        return doc.RootElement.TryGetProperty(key, out var value) &&
               (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var parsedId)
                || value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out parsedId))
            ? parsedId
            : null;
    }
}
