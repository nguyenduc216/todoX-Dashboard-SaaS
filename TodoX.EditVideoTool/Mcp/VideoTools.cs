using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using TodoX.EditVideoTool.Licensing;
using TodoX.EditVideoTool.Media;

namespace TodoX.EditVideoTool.Mcp;

[McpServerToolType]
public sealed class VideoTools
{
    private readonly LicenseGuard _license;
    private readonly MediaProbe _probe;
    private readonly RemotionRenderer _renderer;

    public VideoTools(LicenseGuard license, MediaProbe probe, RemotionRenderer renderer)
    {
        _license = license;
        _probe = probe;
        _renderer = renderer;
    }

    [McpServerTool(Name = "todox_probe", ReadOnly = true, Idempotent = true)]
    [Description("List video clips in the given files or folders with duration, resolution, fps and whether they have sound.")]
    public async Task<string> Probe(
        [Description("Video files and/or folders on this computer.")] string[] paths,
        CancellationToken ct)
    {
        if (await LicenseProblemAsync(ct) is { } problem)
        {
            return problem;
        }

        var files = MediaProbe.ExpandInputs(paths);
        var infos = new List<MediaInfo>();
        foreach (var f in files)
        {
            infos.Add(await _probe.ProbeAsync(f, ct));
        }

        return JsonSerializer.Serialize(new
        {
            count = infos.Count,
            totalSeconds = Math.Round(infos.Sum(i => i.DurationSeconds), 1),
            clips = infos.Select(i => new
            {
                file = i.Path,
                seconds = Math.Round(i.DurationSeconds, 2),
                resolution = $"{i.Width}x{i.Height}",
                orientation = i.Height > i.Width ? "portrait" : i.Height == i.Width ? "square" : "landscape",
                fps = i.Fps,
                hasAudio = i.HasAudio
            })
        }, StatusTools.Json);
    }

    [McpServerTool(Name = "todox_auto_edit", Destructive = false, OpenWorld = false)]
    [Description("Edit clips into one finished video on this computer: picks the best part of each clip, adds intro/outro, transitions, animated captions from the script, optional music and voiceover, and renders an MP4. Returns the output file path.")]
    public async Task<string> AutoEdit(
        [Description("Video files and/or folders, in the order they should appear.")] string[] clips,
        IProgress<ProgressNotificationValue> progress,
        CancellationToken ct,
        [Description("9:16 (TikTok/Reels, default), 16:9 (YouTube), 1:1 or 4:5.")] string aspect = "9:16",
        [Description("Target length in seconds.")] double targetSeconds = 30,
        [Description("Intro title; omit for no intro.")] string? title = null,
        [Description("Intro subtitle line.")] string? subtitle = null,
        [Description("Narration/caption text shown as animated captions.")] string? script = null,
        [Description("Closing card text, e.g. a call to action; omit for none.")] string? outroText = null,
        [Description("Background music file.")] string? music = null,
        [Description("Voiceover audio file (e.g. from todox_voice_tts).")] string? voiceover = null,
        [Description("Brand accent color, hex.")] string accentColor = "#2449C9",
        [Description("Output .mp4 path; default: an 'output' folder next to the first clip.")] string? output = null)
    {
        if (await LicenseProblemAsync(ct) is { } problem)
        {
            return problem;
        }

        var files = MediaProbe.ExpandInputs(clips);
        if (files.Count == 0)
        {
            return "Không tìm thấy clip video nào trong đường dẫn đã cho.";
        }

        progress.Report(new ProgressNotificationValue { Progress = 0, Total = 100, Message = $"Đang đọc {files.Count} clip" });
        var infos = new List<MediaInfo>();
        foreach (var f in files)
        {
            infos.Add(await _probe.ProbeAsync(f, ct));
        }

        double? voiceSeconds = null;
        if (voiceover is not null)
        {
            voiceSeconds = (await _probe.ProbeAsync(Path.GetFullPath(voiceover), ct)).DurationSeconds;
        }

        var plan = EditPlanner.Plan(new AutoEditRequest(
            infos, aspect, targetSeconds, title, subtitle, script, outroText,
            music is null ? null : Path.GetFullPath(music),
            voiceover is null ? null : Path.GetFullPath(voiceover),
            voiceSeconds, accentColor));

        var outPath = output ?? Path.Combine(
            Path.GetDirectoryName(files[0])!, "output",
            $"todox-{DateTime.Now:yyyyMMdd-HHmmss}-{aspect.Replace(':', 'x')}.mp4");

        var lastReported = -1.0;
        var result = await _renderer.RenderAsync(plan, outPath, new Progress<RenderProgress>(p =>
        {
            if (Math.Abs(p.Percent - lastReported) >= 2 || p.Percent >= 100)
            {
                lastReported = p.Percent;
                progress.Report(new ProgressNotificationValue { Progress = (float)Math.Clamp(p.Percent, 0, 100), Total = 100, Message = p.Stage });
            }
        }), ct);

        return JsonSerializer.Serialize(new
        {
            output = result.OutputPath,
            seconds = Math.Round(result.Seconds, 1),
            resolution = $"{plan.Width}x{plan.Height}",
            clipsUsed = plan.Clips.Count,
            clipsGiven = files.Count,
            captions = plan.Captions.Count,
            renderSeconds = Math.Round(result.Elapsed.TotalSeconds, 0)
        }, StatusTools.Json);
    }

    /// <summary>Returns a user-facing message when the license does not allow running tools, otherwise null.</summary>
    private async Task<string?> LicenseProblemAsync(CancellationToken ct)
    {
#if DEBUG
        if (Environment.GetEnvironmentVariable("TODOX_EDITVIDEO_DEV_NOLICENSE") == "1")
        {
            return null;
        }
#endif
        var status = await _license.EnsureAsync(ct);
        return status.IsUsable ? null : $"TodoX EditVideoTool chưa sẵn sàng: {status.Message}";
    }
}
