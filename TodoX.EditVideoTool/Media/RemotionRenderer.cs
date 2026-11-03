using System.Diagnostics;
using System.Text.Json;
using TodoX.EditVideoTool.Components;

namespace TodoX.EditVideoTool.Media;

public sealed record RenderProgress(string Stage, double Percent);

public sealed record RenderResult(string OutputPath, double Seconds, TimeSpan Elapsed);

/// <summary>
/// Runs TodoX.EditVideoTool.Templates/render.mjs with the bundled Node. The Remotion folder holds
/// render.mjs, node_modules and build/ (the bundle); the installer puts it under components/remotion.
/// </summary>
public sealed class RemotionRenderer
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly AgentPaths _paths;
    private readonly ComponentLocator _components;

    public RemotionRenderer(AgentPaths paths, ComponentLocator components)
    {
        _paths = paths;
        _components = components;
    }

    public string? RemotionDir
    {
        get
        {
            var candidates = new[]
            {
                Environment.GetEnvironmentVariable("TODOX_EDITVIDEO_REMOTION_DIR"),
                Path.Combine(_paths.ComponentsDir, "remotion")
            };
            return candidates.FirstOrDefault(d => !string.IsNullOrWhiteSpace(d)
                && File.Exists(Path.Combine(d!, "render.mjs"))
                && Directory.Exists(Path.Combine(d!, "build")));
        }
    }

    /// <summary>Remotion keeps its downloaded headless browser under node_modules/.remotion.</summary>
    public static bool HasRenderBrowser(string remotionDir) =>
        Directory.Exists(Path.Combine(remotionDir, "node_modules", ".remotion"))
        && Directory.EnumerateFiles(Path.Combine(remotionDir, "node_modules", ".remotion"), "*.exe", SearchOption.AllDirectories).Any();

    public async Task<RenderResult> RenderAsync(AutoEditPlan plan, string outputPath, IProgress<RenderProgress>? progress, CancellationToken ct = default)
    {
        var node = _components.Locate("node");
        if (!node.Installed)
        {
            throw new InvalidOperationException("Chưa cài Node.js cho TodoX. Chạy lại bộ cài.");
        }

        var dir = RemotionDir ?? throw new InvalidOperationException("Chưa cài bộ mẫu Remotion của TodoX. Chạy lại bộ cài.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);

        var jobFile = Path.Combine(Path.GetTempPath(), $"todox-render-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(jobFile, JsonSerializer.Serialize(new
        {
            bundle = Path.Combine(dir, "build"),
            output = Path.GetFullPath(outputPath),
            props = plan
        }, Json), ct);

        var psi = new ProcessStartInfo(node.Path!)
        {
            WorkingDirectory = dir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add(Path.Combine(dir, "render.mjs"));
        psi.ArgumentList.Add(jobFile);

        var started = Stopwatch.StartNew();
        string? error = null;
        double seconds = plan.TotalSeconds;
        var stderrTail = new Queue<string>();
        try
        {
            using var proc = Process.Start(psi)!;
            using var reg = ct.Register(() => { try { proc.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } });
            var stderrTask = Task.Run(async () =>
            {
                while (await proc.StandardError.ReadLineAsync() is { } line)
                {
                    stderrTail.Enqueue(line);
                    if (stderrTail.Count > 20)
                    {
                        stderrTail.Dequeue();
                    }
                }
            }, CancellationToken.None);

            while (await proc.StandardOutput.ReadLineAsync(ct) is { } line)
            {
                var evt = ParseEvent(line);
                switch (evt?.Type)
                {
                    case "browser_download":
                        progress?.Report(new RenderProgress("Tải trình duyệt dựng hình (lần đầu)", evt.Percent * 100));
                        break;
                    case "stage":
                        progress?.Report(new RenderProgress(evt.Stage == "render" ? "Đang dựng video" : "Chuẩn bị", 0));
                        break;
                    case "progress":
                        progress?.Report(new RenderProgress("Đang dựng video", evt.Percent));
                        break;
                    case "done":
                        seconds = evt.Seconds;
                        break;
                    case "error":
                        error = evt.Message;
                        break;
                }
            }

            await proc.WaitForExitAsync(ct);
            await stderrTask;
            ct.ThrowIfCancellationRequested();
            if (proc.ExitCode != 0 || error is not null)
            {
                throw new InvalidOperationException("Dựng video thất bại: " + (error ?? string.Join(" | ", stderrTail)));
            }
        }
        finally
        {
            File.Delete(jobFile);
        }

        return new RenderResult(Path.GetFullPath(outputPath), seconds, started.Elapsed);
    }

    internal sealed record RenderEvent(string Type, string? Stage, double Percent, double Seconds, string? Message);

    internal static RenderEvent? ParseEvent(string line)
    {
        if (!line.StartsWith('{'))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(line);
            var r = doc.RootElement;
            return new RenderEvent(
                r.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "",
                r.TryGetProperty("stage", out var s) ? s.GetString() : null,
                r.TryGetProperty("percent", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetDouble() : 0,
                r.TryGetProperty("seconds", out var sec) && sec.ValueKind == JsonValueKind.Number ? sec.GetDouble() : 0,
                r.TryGetProperty("message", out var m) ? m.GetString() : null);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
