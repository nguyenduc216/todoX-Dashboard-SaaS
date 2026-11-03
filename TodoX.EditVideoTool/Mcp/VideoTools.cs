using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using TodoX.EditVideoTool.Licensing;
using TodoX.EditVideoTool.Media;
using TodoX.EditVideoTool.Server;
using TodoX.EditVideoTool.Status;

namespace TodoX.EditVideoTool.Mcp;

[McpServerToolType]
public sealed class VideoTools
{
    /// <summary>Connectivity attempts inside a tool call: first try + 3 retries 15 s apart.</summary>
    private const int ServerAttempts = 4;

    private readonly LicenseGuard _license;
    private readonly LicenseVerifier _verifier;
    private readonly MediaProbe _probe;
    private readonly ClipNormalizer _normalizer;
    private readonly RemotionRenderer _renderer;
    private readonly TodoXServerClient _server;
    private readonly ServerConnectivity _connectivity;
    private readonly AgentPaths _paths;

    public VideoTools(LicenseGuard license, LicenseVerifier verifier, MediaProbe probe, ClipNormalizer normalizer,
        RemotionRenderer renderer, TodoXServerClient server, ServerConnectivity connectivity, AgentPaths paths)
    {
        _license = license;
        _verifier = verifier;
        _probe = probe;
        _normalizer = normalizer;
        _renderer = renderer;
        _server = server;
        _connectivity = connectivity;
        _paths = paths;
    }

    [McpServerTool(Name = "todox_probe", ReadOnly = true, Idempotent = true)]
    [Description("List video clips in the given files or folders with duration, resolution, fps and whether they have sound.")]
    public async Task<string> Probe(
        [Description("Video files and/or folders on this computer.")] string[] paths,
        CancellationToken ct)
    {
        if (await ReadyProblemAsync(null, ct) is { } problem)
        {
            return problem;
        }

        var infos = new List<MediaInfo>();
        foreach (var f in MediaProbe.ExpandInputs(paths))
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

    [McpServerTool(Name = "todox_styles", ReadOnly = true, Idempotent = true)]
    [Description("List the video looks (styles) available for todox_auto_edit.")]
    public async Task<string> Styles(CancellationToken ct)
    {
        var styles = await _server.StylesAsync(ct);
        return styles.Ok ? styles.Value.GetRawText() : ServerConnectivity.OfflineText;
    }

    [McpServerTool(Name = "todox_voice_list", ReadOnly = true, Idempotent = true)]
    [Description("List Vietnamese voices available for narration, the point price per voiceover and the customer's point balance.")]
    public async Task<string> VoiceList(CancellationToken ct)
    {
        if (await ReadyProblemAsync(null, ct) is { } problem)
        {
            return problem;
        }

        var voices = await _server.VoicesAsync(ct);
        return voices.Ok ? voices.Value.GetRawText() : Explain(voices);
    }

    [McpServerTool(Name = "todox_voice_tts", Destructive = false)]
    [Description("Turn narration text into a voiceover audio file with a TodoX voice (costs points; price from todox_voice_list). Returns the local audio file path.")]
    public async Task<string> VoiceTts(
        [Description("Narration text, up to 3000 characters.")] string text,
        IProgress<ProgressNotificationValue> progress,
        CancellationToken ct,
        [Description("Voice code from todox_voice_list; omit for the default voice.")] string? voice = null,
        [Description("Folder to save the audio in; default: Documents\\TodoX\\voice.")] string? folder = null)
    {
        await using var session = await SessionLogger.StartAsync(_server, _paths, "voice", ct);
        if (await ReadyProblemAsync(session, ct) is { } problem)
        {
            return problem;
        }

        var result = await CreateVoiceAsync(text, voice, folder ?? DefaultVoiceFolder(), session, progress, ct);
        return result.Error ?? JsonSerializer.Serialize(new { audio = result.Path, seconds = result.Seconds, points = result.Points }, StatusTools.Json);
    }

    [McpServerTool(Name = "todox_convert", Destructive = false)]
    [Description("Convert a video into a widely compatible MP4 (H.264, at most 1920px on the long side).")]
    public async Task<string> Convert(
        [Description("Input video file.")] string input,
        CancellationToken ct,
        [Description("Output .mp4 path; default: next to the input with '-todox.mp4'.")] string? output = null)
    {
        await using var session = await SessionLogger.StartAsync(_server, _paths, "convert", ct);
        if (await ReadyProblemAsync(session, ct) is { } problem)
        {
            return problem;
        }

        var src = Path.GetFullPath(input);
        var dst = output ?? Path.Combine(Path.GetDirectoryName(src)!, Path.GetFileNameWithoutExtension(src) + "-todox.mp4");
        try
        {
            var path = await _normalizer.ConvertAsync(src, dst, ct);
            session.Info("convert_done");
            return JsonSerializer.Serialize(new { output = path }, StatusTools.Json);
        }
        catch (InvalidOperationException ex)
        {
            session.Error("convert_failed", ex.Message);
            return ex.Message;
        }
    }

    [McpServerTool(Name = "todox_auto_edit", Destructive = false, OpenWorld = false)]
    [Description("Edit clips into one finished video on this computer: TodoX picks the best part of each clip and adds intro/outro, transitions, animated captions from the script, optional music and a TodoX voiceover, then renders an MP4. Returns the output file path.")]
    public async Task<string> AutoEdit(
        [Description("Video files and/or folders, in the order they should appear.")] string[] clips,
        IProgress<ProgressNotificationValue> progress,
        CancellationToken ct,
        [Description("9:16 (TikTok/Reels, default), 16:9 (YouTube), 1:1 or 4:5.")] string aspect = "9:16",
        [Description("Target length in seconds.")] double targetSeconds = 30,
        [Description("Intro title; omit for no intro.")] string? title = null,
        [Description("Intro subtitle line.")] string? subtitle = null,
        [Description("Narration/caption text shown as animated captions (and read aloud when voice is set).")] string? script = null,
        [Description("Read the script aloud with this TodoX voice code (see todox_voice_list); 'default' for the default voice. Costs points.")] string? voice = null,
        [Description("Closing card text, e.g. a call to action; omit for none.")] string? outroText = null,
        [Description("Background music file.")] string? music = null,
        [Description("Existing voiceover audio file (instead of voice).")] string? voiceover = null,
        [Description("Look id from todox_styles; default TodoX.")] string? style = null,
        [Description("Brand accent color, hex, overrides the style's.")] string? accentColor = null,
        [Description("Output .mp4 path; default: an 'output' folder next to the first clip.")] string? output = null)
    {
        await using var session = await SessionLogger.StartAsync(_server, _paths, "auto_edit", ct);
        if (await ReadyProblemAsync(session, ct) is { } problem)
        {
            return problem;
        }

        try
        {
            var files = MediaProbe.ExpandInputs(clips);
            if (files.Count == 0)
            {
                return "Không tìm thấy clip video nào trong đường dẫn đã cho.";
            }

            Report(progress, 2, $"Đang đọc {files.Count} clip");
            var infos = new List<MediaInfo>();
            foreach (var f in files)
            {
                infos.Add(await _probe.ProbeAsync(f, ct));
            }

            session.Info("probed", null, new { clips = infos.Count, seconds = Math.Round(infos.Sum(i => i.DurationSeconds), 1) });

            var outDir = Path.GetDirectoryName(Path.GetFullPath(output ?? Path.Combine(Path.GetDirectoryName(files[0])!, "output", "x.mp4")))!;
            string? voicePath = voiceover is null ? null : Path.GetFullPath(voiceover);
            if (voicePath is null && !string.IsNullOrWhiteSpace(voice) && !string.IsNullOrWhiteSpace(script))
            {
                Report(progress, 5, "Đang tạo giọng đọc TodoX");
                var created = await CreateVoiceAsync(script, voice == "default" ? null : voice, outDir, session, progress, ct);
                if (created.Error is not null)
                {
                    return created.Error;
                }

                voicePath = created.Path;
            }

            double? voiceSeconds = voicePath is null ? null : (await _probe.ProbeAsync(voicePath, ct)).DurationSeconds;

            // Only technical metadata leaves the machine: ids, durations, sizes. Never paths or footage.
            var media = new Dictionary<string, string>();
            var inputs = infos.Select((info, i) =>
            {
                media[$"c{i}"] = info.Path;
                return new { id = $"c{i}", info.DurationSeconds, info.Width, info.Height, info.HasVideo, info.HasAudio };
            }).ToList();
            if (music is not null)
            {
                media["music"] = Path.GetFullPath(music);
            }

            if (voicePath is not null)
            {
                media["voiceover"] = voicePath;
            }

            Report(progress, 12, "TodoX đang lên kế hoạch dựng");
            var planned = await _server.PlanAsync(new
            {
                clips = inputs,
                aspect,
                targetSeconds,
                title,
                subtitle,
                script,
                outroText,
                hasMusic = music is not null,
                hasVoiceover = voicePath is not null,
                voiceoverSeconds = voiceSeconds,
                styleId = style,
                accentColor
            }, ct);
            if (!planned.Ok)
            {
                session.Error("plan_failed", planned.Message, new { planned.ErrorCode });
                return Explain(planned);
            }

            var plan = PlanDocument.Open(planned.Value.GetProperty("document").GetString()!, _verifier, media);
            if (plan is null)
            {
                session.Error("plan_signature_invalid");
                return "Kế hoạch dựng không có chữ ký hợp lệ của TodoX. Hãy cập nhật TodoX EditVideo.";
            }

            // Phone footage (rotated / HEVC / 4K) gets a cached 1080p proxy so rendering is stable and fast.
            var proxied = 0;
            var clipsReady = new List<PlanClip>();
            foreach (var clip in plan.Clips)
            {
                var info = infos.First(i => i.Path == clip.Src);
                if (ClipNormalizer.NeedsProxy(info))
                {
                    Report(progress, 15, $"Chuẩn hóa clip {Path.GetFileName(info.Path)}");
                    proxied++;
                }

                clipsReady.Add(clip with { Src = await _normalizer.PrepareAsync(info, ct) });
            }

            plan = plan with { Clips = clipsReady };
            session.Info("planned", null, new { scenes = plan.Clips.Count, plan.TotalSeconds, style = plan.Style.Id, proxied, aspect });

            var outPath = output ?? Path.Combine(outDir, $"todox-{DateTime.Now:yyyyMMdd-HHmmss}-{aspect.Replace(':', 'x')}.mp4");
            var last = -1.0;
            var result = await _renderer.RenderAsync(plan, outPath, new Progress<RenderProgress>(p =>
            {
                var pct = 20 + p.Percent * 0.8;
                if (pct - last >= 2 || p.Percent >= 100)
                {
                    last = pct;
                    Report(progress, pct, p.Stage);
                }
            }), ct);

            session.Info("rendered", null, new { seconds = Math.Round(result.Seconds, 1), renderSeconds = Math.Round(result.Elapsed.TotalSeconds) });
            return JsonSerializer.Serialize(new
            {
                output = result.OutputPath,
                seconds = Math.Round(result.Seconds, 1),
                resolution = $"{plan.Width}x{plan.Height}",
                style = plan.Style.Name,
                scenes = plan.Clips.Count,
                clipsGiven = files.Count,
                captions = plan.Captions.Count,
                voiceover = voicePath,
                renderSeconds = Math.Round(result.Elapsed.TotalSeconds, 0)
            }, StatusTools.Json);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FileNotFoundException or ArgumentException or IOException)
        {
            session.Error("auto_edit_failed", ex.Message);
            return ex.Message;
        }
    }

    private sealed record VoiceResult(string? Path, double Seconds, decimal Points, string? Error);

    private async Task<VoiceResult> CreateVoiceAsync(string text, string? voice, string folder, SessionLogger session, IProgress<ProgressNotificationValue> progress, CancellationToken ct)
    {
        var submitted = await _server.SubmitTtsAsync(text, voice, session.SessionId, ct);
        if (!submitted.Ok)
        {
            session.Error("voice_submit_failed", submitted.ErrorCode, new { chars = text.Length });
            return new VoiceResult(null, 0, 0, Explain(submitted));
        }

        var job = submitted.Value;
        var deadline = DateTime.UtcNow.AddMinutes(3);
        while (job.GetProperty("status").GetString() == "submitted" && DateTime.UtcNow < deadline)
        {
            Report(progress, 8, "Đang chờ giọng đọc TodoX");
            await Task.Delay(TimeSpan.FromSeconds(3), ct);
            var polled = await _server.TtsStatusAsync(job.GetProperty("id").GetGuid(), ct);
            if (!polled.Ok)
            {
                session.Error("voice_poll_failed", polled.ErrorCode);
                return new VoiceResult(null, 0, 0, Explain(polled));
            }

            job = polled.Value;
        }

        if (job.GetProperty("status").GetString() != "succeeded" || job.GetProperty("audioUrl").GetString() is not { } url)
        {
            session.Error("voice_not_ready", job.GetProperty("status").GetString());
            return new VoiceResult(null, 0, 0, "Không tạo được giọng đọc lúc này. Thử lại sau ít phút.");
        }

        var path = Path.Combine(folder, $"todox-voice-{DateTime.Now:yyyyMMdd-HHmmss}.mp3");
        await _server.DownloadAsync(url, path, null, ct);
        var seconds = (await _probe.ProbeAsync(path, ct)).DurationSeconds;
        var points = job.TryGetProperty("points", out var p) ? p.GetDecimal() : 0;
        session.Info("voice_done", null, new { seconds = Math.Round(seconds, 1), points, chars = text.Length });
        return new VoiceResult(path, Math.Round(seconds, 1), points, null);
    }

    /// <summary>
    /// License + TodoX reachability before any work. Unreachable → Windows notification and retries every 15 s;
    /// still unreachable → a clear message for the assistant to relay.
    /// </summary>
    private async Task<string?> ReadyProblemAsync(SessionLogger? session, CancellationToken ct)
    {
#if DEBUG
        if (Environment.GetEnvironmentVariable("TODOX_EDITVIDEO_DEV_NOLICENSE") == "1")
        {
            return null;
        }
#endif
        var status = await _license.EnsureAsync(ct);
        if (!status.IsUsable)
        {
            session?.Error("license", status.Code);
            return $"TodoX EditVideo chưa sẵn sàng: {status.Message}";
        }

        if (!await _connectivity.EnsureConnectedAsync(ServerAttempts, ct: ct))
        {
            return "Không kết nối được máy chủ TodoX sau nhiều lần thử (15 giây/lần). Kiểm tra Internet rồi thử lại.";
        }

        return null;
    }

    private static string Explain<T>(ServerResult<T> r) => r.ErrorCode switch
    {
        LicenseApiClient.NetworkError => ServerConnectivity.OfflineText,
        "insufficient_points" or "voice_service_unavailable" or "voice_not_found" => r.Message ?? r.ErrorCode,
        _ => LicenseMessages.ForServerError(r.ErrorCode, r.Message)
    };

    private static string DefaultVoiceFolder() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TodoX", "voice");

    private static void Report(IProgress<ProgressNotificationValue> progress, double percent, string message) =>
        progress.Report(new ProgressNotificationValue { Progress = (float)Math.Clamp(percent, 0, 100), Total = 100, Message = message });
}
