using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using TodoX.EditVideoTool.Licensing;
using TodoX.EditVideoTool.Media;
using TodoX.EditVideoTool.Server;
using TodoX.EditVideoTool.Setup;
using TodoX.EditVideoTool.Status;

namespace TodoX.EditVideoTool.Mcp;

[McpServerToolType]
public sealed partial class VideoTools
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
    private readonly Transcriber _transcriber;
    private readonly SkillSync _skills;
    private readonly EditStore _edits;

    public VideoTools(LicenseGuard license, LicenseVerifier verifier, MediaProbe probe, ClipNormalizer normalizer,
        RemotionRenderer renderer, TodoXServerClient server, ServerConnectivity connectivity, AgentPaths paths, Transcriber transcriber,
        SkillSync skills, EditStore edits)
    {
        _edits = edits;
        _skills = skills;
        _transcriber = transcriber;
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
        return result.Error ?? JsonSerializer.Serialize(new { audio = result.Path, seconds = result.Seconds, points = result.Points, voiceCheck = result.Check }, StatusTools.Json);
    }

    [McpServerTool(Name = "todox_transcribe", ReadOnly = true, Idempotent = true)]
    [Description("Listen to an audio or video file on this computer and return what is said, with timestamps (offline speech-to-text).")]
    public async Task<string> Transcribe(
        [Description("Audio or video file.")] string path,
        CancellationToken ct,
        [Description("Spoken language code, default vi.")] string language = "vi")
    {
        await using var session = await SessionLogger.StartAsync(_server, _paths, "transcribe", ct);
        if (await ReadyProblemAsync(session, ct) is { } problem)
        {
            return problem;
        }

        try
        {
            var t = await _transcriber.TranscribeAsync(Path.GetFullPath(path), language, ct);
            session.Info("transcribed", null, new { seconds = t.Seconds, segments = t.Segments.Count });
            return JsonSerializer.Serialize(t, StatusTools.Json);
        }
        catch (InvalidOperationException ex)
        {
            session.Error("transcribe_failed", ex.Message);
            return ex.Message;
        }
    }

    [McpServerTool(Name = "todox_voice_check", ReadOnly = true, Idempotent = true)]
    [Description("Check that a voiceover says the script: listens to the audio and returns a match score (0-1), missing and extra words.")]
    public async Task<string> VoiceCheck(
        [Description("Voiceover audio (or video) file.")] string audio,
        [Description("The script the voice should read.")] string script,
        CancellationToken ct)
    {
        await using var session = await SessionLogger.StartAsync(_server, _paths, "voice_check", ct);
        if (await ReadyProblemAsync(session, ct) is { } problem)
        {
            return problem;
        }

        try
        {
            var result = await CheckVoiceAsync(Path.GetFullPath(audio), script, ct);
            session.Info("voice_checked", null, new { result!.Score, result.Ok });
            return JsonSerializer.Serialize(result, StatusTools.Json);
        }
        catch (InvalidOperationException ex)
        {
            session.Error("voice_check_failed", ex.Message);
            return ex.Message;
        }
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
    [Description("Edit clips into one finished video on this computer: TodoX picks the best part of each clip and adds intro/outro, transitions, animated captions from the script, optional music and a TodoX voiceover, then renders an MP4. Returns the output file path and an edit id: adjust the result with todox_cut, todox_join, todox_caption, todox_titles, todox_audio, todox_look and render again with todox_export.")]
    public async Task<string> AutoEdit(
        [Description("Video files and/or folders, in the order they should appear.")] string[] clips,
        IProgress<ProgressNotificationValue> progress,
        CancellationToken ct,
        [Description("9:16 (TikTok/Reels), 16:9 (YouTube), 1:1 or 4:5; omit for the skill's or the default (9:16).")] string? aspect = null,
        [Description("Target length in seconds; omit for the skill's or the default (30).")] double? targetSeconds = null,
        [Description("Intro title; omit for no intro.")] string? title = null,
        [Description("Intro subtitle line.")] string? subtitle = null,
        [Description("Narration/caption text shown as animated captions (and read aloud when voice is set).")] string? script = null,
        [Description("Read the script aloud with this TodoX voice code (see todox_voice_list); 'default' for the default voice. Costs points.")] string? voice = null,
        [Description("Closing card text, e.g. a call to action; omit for none.")] string? outroText = null,
        [Description("Background music file.")] string? music = null,
        [Description("Existing voiceover audio file (instead of voice).")] string? voiceover = null,
        [Description("Look id from todox_styles; default TodoX.")] string? style = null,
        [Description("Brand accent color, hex, overrides the style's.")] string? accentColor = null,
        [Description("Output .mp4 path; default: an 'output' folder next to the first clip.")] string? output = null,
        [Description("The TodoX skill (todox-...) guiding this video, as stated at the end of that skill.")] string? skill = null,
        [Description("That skill's version number, as stated at the end of the skill.")] int? skillVersion = null)
    {
        await using var session = await SessionLogger.StartAsync(_server, _paths, "auto_edit", ct);
        if (await ReadyProblemAsync(session, ct) is { } problem)
        {
            return problem;
        }

        try
        {
            // Same path as the step tools: a signed edit is kept, so the result can be adjusted and exported again.
            var started = await StartEditAsync(new EditBrief(clips, aspect, targetSeconds, title, subtitle, script, voice, outroText, music, voiceover, style, accentColor, skill, skillVersion), session, progress, ct);
            return started.Error ?? await ExportAsync(started.Edit!, output, session, progress, 15, ct, started.VoiceCheck);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FileNotFoundException or ArgumentException or IOException)
        {
            session.Error("auto_edit_failed", ex.Message);
            return ex.Message;
        }
    }

    private sealed record SkillTagInfo(string Skill, int? Version, string? VersionName, string? Title);

    private SkillTagInfo? SkillTag(string? skill, int? version)
    {
        if (string.IsNullOrWhiteSpace(skill))
        {
            return null;
        }

        var slug = skill.Trim();
        var local = _skills.LoadState().Skills.GetValueOrDefault(slug);
        // No version given: the one installed in Claude right now.
        var v = version ?? local?.Installed ?? local?.Effective;
        return new SkillTagInfo(slug, v, v is { } n ? local?.Find(n)?.Name : null, local?.Title);
    }

    private static void WriteVideoInfo(string videoPath, SkillTagInfo? tag, string style, string aspect, double seconds)
    {
        try
        {
            File.WriteAllText(videoPath + ".todox.json", JsonSerializer.Serialize(new
            {
                video = Path.GetFileName(videoPath),
                created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                app = StatusService.Version,
                skill = tag?.Skill,
                skillTitle = tag?.Title,
                skillVersion = tag?.Version,
                skillVersionName = tag?.VersionName,
                style,
                aspect,
                seconds = Math.Round(seconds, 1)
            }, new JsonSerializerOptions(StatusTools.Json) { WriteIndented = true }));
        }
        catch (IOException)
        {
            // The video itself is what matters; the info file is a convenience.
        }
    }

    private sealed record VoiceResult(string? Path, double Seconds, decimal Points, string? Error, VoiceCheckResult? Check = null);

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

        // Listen back to the voiceover so a wrong/garbled read is caught before it goes into a video.
        VoiceCheckResult? check = null;
        if (_transcriber.IsAvailable)
        {
            Report(progress, 10, "Đang nghe lại giọng đọc để kiểm tra");
            try
            {
                check = await CheckVoiceAsync(path, text, ct);
                if (check is { Ok: false })
                {
                    session.Warn("voice_check_low", $"score {check.Score}", new { missing = check.Missing.Take(10), extra = check.Extra.Take(10) });
                }
                else
                {
                    session.Info("voice_check_ok", null, new { check!.Score });
                }
            }
            catch (InvalidOperationException ex)
            {
                session.Warn("voice_check_skipped", ex.Message);
            }
        }

        return new VoiceResult(path, Math.Round(seconds, 1), points, null, check);
    }

    private async Task<VoiceCheckResult?> CheckVoiceAsync(string audio, string script, CancellationToken ct)
    {
        var heard = await _transcriber.TranscribeAsync(audio, "vi", ct);
        return Transcriber.Compare(script, heard.Text);
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
