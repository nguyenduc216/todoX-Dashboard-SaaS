using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using TodoX.EditVideoTool.Media;
using TodoX.EditVideoTool.Server;

namespace TodoX.EditVideoTool.Mcp;

/// <summary>A scene for todox_join: a clip already in the edit (c0, c1…) or a new video file, optionally trimmed.</summary>
public sealed record SceneSpec(
    [property: Description("Clip id from the timeline (c0, c1…) or the path of another video file to add.")] string Clip,
    [property: Description("Start inside the clip, seconds; omit to let TodoX choose.")] double? From = null,
    [property: Description("Scene length, seconds; omit to let TodoX choose.")] double? Seconds = null);

public sealed record CaptionSpec(
    [property: Description("Caption text.")] string Text,
    [property: Description("Start time in the finished video, seconds.")] double Start,
    [property: Description("End time in the finished video, seconds.")] double End);

// Step-by-step editing. Every change goes through TodoX (the server checks and re-signs the plan), so the video is
// always made with TodoX tools: todox_edit_start → todox_cut / todox_join / todox_caption / todox_titles /
// todox_audio / todox_look → todox_export.
public sealed partial class VideoTools
{
    private const string EditParam = "Edit id from todox_edit_start / todox_auto_edit; omit for the most recent edit.";

    [McpServerTool(Name = "todox_edit_start", Destructive = false)]
    [Description("Start editing a video step by step: TodoX reads the clips and builds a first timeline (scenes, captions, titles, music, look) WITHOUT rendering yet. Then adjust with todox_cut, todox_join, todox_caption, todox_titles, todox_audio, todox_look and finish with todox_export. Returns the edit id and the timeline.")]
    public async Task<string> EditStart(
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
        [Description("The TodoX skill (todox-...) guiding this video, as stated at the end of that skill.")] string? skill = null,
        [Description("That skill's version number, as stated at the end of the skill.")] int? skillVersion = null)
    {
        await using var session = await SessionLogger.StartAsync(_server, _paths, "edit_start", ct);
        if (await ReadyProblemAsync(session, ct) is { } problem)
        {
            return problem;
        }

        try
        {
            var started = await StartEditAsync(new EditBrief(clips, aspect, targetSeconds, title, subtitle, script, voice, outroText, music, voiceover, style, accentColor, skill, skillVersion), session, progress, ct);
            if (started.Error is not null)
            {
                return started.Error;
            }

            await session.EndAsync("ok", new { edit = started.Edit!.Id, skill, skillVersion }, ct);
            return Timeline(started.Edit, started.Plan!, started.VoiceCheck);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FileNotFoundException or ArgumentException or IOException)
        {
            session.Error("edit_start_failed", ex.Message);
            return ex.Message;
        }
    }

    [McpServerTool(Name = "todox_timeline", ReadOnly = true, Idempotent = true)]
    [Description("Show the current timeline of an edit: numbered scenes with clip, start and length, captions with times, intro/outro, music, voiceover and look.")]
    public string ShowTimeline([Description(EditParam)] string? edit = null)
    {
        var project = _edits.Find(edit);
        if (project is null)
        {
            return NoEdit(edit);
        }

        var plan = PlanDocument.Read(project.Document, _verifier);
        return plan is null ? "Bản dựng này không còn hợp lệ. Hãy bắt đầu lại bằng todox_edit_start." : Timeline(project, plan, null);
    }

    [McpServerTool(Name = "todox_cut", Destructive = false)]
    [Description("Trim one scene: choose where it starts inside its clip and how long it lasts. Returns the updated timeline.")]
    public Task<string> Cut(
        [Description("Scene number from the timeline (1 = first scene).")] int scene,
        CancellationToken ct,
        [Description("Start inside the clip, seconds.")] double? from = null,
        [Description("Scene length, seconds.")] double? seconds = null,
        [Description(EditParam)] string? edit = null) =>
        ChangeAsync(edit, _ => new { op = "cut", scene, from, duration = seconds }, ct);

    [McpServerTool(Name = "todox_join", Destructive = false)]
    [Description("Set the order of scenes: reorder, drop, repeat, or add new video files. Optionally choose the transition between scenes (fade, slide, mix) and its length. Returns the updated timeline.")]
    public async Task<string> Join(
        CancellationToken ct,
        [Description("All scenes of the video in order. Omit to keep the scenes and only change the transition.")] SceneSpec[]? scenes = null,
        [Description("Transition between scenes: fade, slide or mix.")] string? transition = null,
        [Description("Transition length in seconds (0 = hard cut, max 1.5).")] double? transitionSeconds = null,
        [Description(EditParam)] string? edit = null)
    {
        var project = _edits.Find(edit);
        if (project is null)
        {
            return NoEdit(edit);
        }

        // New files are probed here and join the edit's clips; the server only ever sees their id and metadata.
        List<object>? specs = null;
        if (scenes is not null)
        {
            specs = [];
            foreach (var s in scenes)
            {
                var id = project.Clips.Any(c => c.Id == s.Clip) ? s.Clip : await AddClipAsync(project, s.Clip, ct);
                if (id is null)
                {
                    return $"Không tìm thấy clip \"{s.Clip}\". Dùng mã clip trong timeline (c0, c1…) hoặc đường dẫn file video.";
                }

                specs.Add(new { clip = id, from = s.From, duration = s.Seconds });
            }
        }

        return await ChangeAsync(project, _ => new { op = "arrange", scenes = specs, transition, transitionSeconds }, ct);
    }

    [McpServerTool(Name = "todox_caption", Destructive = false)]
    [Description("Set the on-screen captions: either a script that TodoX splits and times across the video, or exact lines with start/end times. Optionally change the caption look (karaoke, box, pop, plain). Pass an empty script to remove captions. Returns the updated timeline.")]
    public Task<string> Caption(
        CancellationToken ct,
        [Description("Text to show as captions, timed automatically; \"\" removes all captions.")] string? script = null,
        [Description("Exact caption lines with times in the finished video (instead of script).")] CaptionSpec[]? captions = null,
        [Description("Caption look: karaoke, box, pop or plain.")] string? captionStyle = null,
        [Description(EditParam)] string? edit = null) =>
        ChangeAsync(edit, _ => new
        {
            op = "captions",
            script,
            captions = captions?.Select(c => new { text = c.Text, start = c.Start, end = c.End }),
            captionStyle
        }, ct);

    [McpServerTool(Name = "todox_titles", Destructive = false)]
    [Description("Set the intro title/subtitle and the closing card text. Pass \"\" to remove the intro (title) or the closing card (outroText). Returns the updated timeline.")]
    public Task<string> Titles(
        CancellationToken ct,
        [Description("Intro title; \"\" removes the intro.")] string? title = null,
        [Description("Intro subtitle line.")] string? subtitle = null,
        [Description("Closing card text, e.g. a call to action; \"\" removes it.")] string? outroText = null,
        [Description(EditParam)] string? edit = null) =>
        ChangeAsync(edit, _ => new { op = "titles", title, subtitle, outroText }, ct);

    [McpServerTool(Name = "todox_audio", Destructive = false)]
    [Description("Set the sound: background music file and volume, a voiceover (an audio file, or a TodoX voice reading a script — costs points), and whether the clips' own sound is kept. Use \"none\" to remove music or voiceover. Returns the updated timeline.")]
    public async Task<string> Audio(
        IProgress<ProgressNotificationValue> progress,
        CancellationToken ct,
        [Description("Music file, or \"none\" to remove the music.")] string? music = null,
        [Description("Music volume 0–1.5 (default 0.6).")] double? musicVolume = null,
        [Description("Voiceover audio file, or \"none\" to remove the voiceover.")] string? voiceover = null,
        [Description("Create the voiceover with this TodoX voice code ('default' for the default voice) reading voiceScript. Costs points.")] string? voice = null,
        [Description("Text the TodoX voice reads.")] string? voiceScript = null,
        [Description("Voiceover volume 0–1.5 (default 1).")] double? voiceoverVolume = null,
        [Description("Keep the clips' own sound (default: only when there is no music or voiceover).")] bool? keepClipAudio = null,
        [Description(EditParam)] string? edit = null)
    {
        var project = _edits.Find(edit);
        if (project is null)
        {
            return NoEdit(edit);
        }

        bool? hasMusic = null;
        if (IsNone(music))
        {
            hasMusic = false;
            project.Media.Remove("music");
        }
        else if (music is not null)
        {
            var path = Path.GetFullPath(music);
            if (!File.Exists(path))
            {
                return $"Không tìm thấy file nhạc: {path}";
            }

            project.Media["music"] = path;
            hasMusic = true;
        }

        bool? hasVoice = null;
        VoiceCheckResult? check = null;
        if (IsNone(voiceover))
        {
            hasVoice = false;
            project.Media.Remove("voiceover");
        }
        else if (voiceover is not null || !string.IsNullOrWhiteSpace(voice))
        {
            string path;
            if (voiceover is not null)
            {
                path = Path.GetFullPath(voiceover);
                if (!File.Exists(path))
                {
                    return $"Không tìm thấy file giọng đọc: {path}";
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(voiceScript))
                {
                    return "Cần voiceScript (lời đọc) để tạo giọng đọc TodoX.";
                }

                await using var session = await SessionLogger.StartAsync(_server, _paths, "voice", ct);
                if (await ReadyProblemAsync(session, ct) is { } problem)
                {
                    return problem;
                }

                var created = await CreateVoiceAsync(voiceScript, voice == "default" ? null : voice, OutputFolder(project), session, progress, ct);
                if (created.Error is not null)
                {
                    return created.Error;
                }

                path = created.Path!;
                check = created.Check;
            }

            project.Media["voiceover"] = path;
            hasVoice = true;
        }

        return await ChangeAsync(project, _ => new { op = "audio", music = hasMusic, musicVolume, voiceover = hasVoice, voiceoverVolume, keepClipAudio }, ct, check);
    }

    [McpServerTool(Name = "todox_look", Destructive = false)]
    [Description("Change the look of the video: a TodoX style from todox_styles and/or the brand accent color. Returns the updated timeline.")]
    public Task<string> Look(
        CancellationToken ct,
        [Description("Style id from todox_styles.")] string? style = null,
        [Description("Brand accent color, hex #RRGGBB.")] string? accentColor = null,
        [Description(EditParam)] string? edit = null) =>
        ChangeAsync(edit, _ => new { op = "look", styleId = style, accentColor }, ct);

    [McpServerTool(Name = "todox_export", Destructive = false, OpenWorld = false)]
    [Description("Render the edit into the finished MP4 on this computer. Returns the output file path.")]
    public async Task<string> Export(
        IProgress<ProgressNotificationValue> progress,
        CancellationToken ct,
        [Description("Output .mp4 path; default: an 'output' folder next to the first clip.")] string? output = null,
        [Description(EditParam)] string? edit = null)
    {
        var project = _edits.Find(edit);
        if (project is null)
        {
            return NoEdit(edit);
        }

        await using var session = await SessionLogger.StartAsync(_server, _paths, "export", ct);
        if (await ReadyProblemAsync(session, ct) is { } problem)
        {
            return problem;
        }

        try
        {
            return await ExportAsync(project, output, session, progress, 0, ct);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FileNotFoundException or ArgumentException or IOException)
        {
            session.Error("export_failed", ex.Message);
            return ex.Message;
        }
    }

    // ---- shared by todox_auto_edit and the step tools

    private sealed record EditBrief(string[] Clips, string? Aspect, double? TargetSeconds, string? Title, string? Subtitle, string? Script,
        string? Voice, string? OutroText, string? Music, string? Voiceover, string? Style, string? AccentColor, string? Skill, int? SkillVersion);

    private sealed record StartResult(EditProject? Edit, AutoEditPlan? Plan, VoiceCheckResult? VoiceCheck, string? Error);

    private async Task<StartResult> StartEditAsync(EditBrief brief, SessionLogger session, IProgress<ProgressNotificationValue> progress, CancellationToken ct)
    {
        var files = MediaProbe.ExpandInputs(brief.Clips);
        if (files.Count == 0)
        {
            return new StartResult(null, null, null, "Không tìm thấy clip video nào trong đường dẫn đã cho.");
        }

        Report(progress, 2, $"Đang đọc {files.Count} clip");
        var project = _edits.Create(brief.Aspect ?? "9:16");
        project.Skill = string.IsNullOrWhiteSpace(brief.Skill) ? null : brief.Skill.Trim().ToLowerInvariant();
        // No version given: the one the user set as default for this skill.
        project.SkillVersion = brief.SkillVersion ?? (project.Skill is null ? null : _skills.LoadState().Skills.GetValueOrDefault(project.Skill)?.Effective);
        foreach (var f in files)
        {
            var info = await _probe.ProbeAsync(f, ct);
            project.Clips.Add(new EditClip(project.NextClipId(), info.Path, info.DurationSeconds, info.Width, info.Height, info.HasVideo, info.HasAudio));
        }

        session.Info("probed", null, new { clips = project.Clips.Count, seconds = Math.Round(project.Clips.Sum(c => c.Seconds), 1) });
        foreach (var c in project.Clips)
        {
            project.Media[c.Id] = c.Path;
        }

        var voicePath = brief.Voiceover is null ? null : Path.GetFullPath(brief.Voiceover);
        VoiceCheckResult? voiceCheck = null;
        if (voicePath is null && !string.IsNullOrWhiteSpace(brief.Voice) && !string.IsNullOrWhiteSpace(brief.Script))
        {
            Report(progress, 5, "Đang tạo giọng đọc TodoX");
            var created = await CreateVoiceAsync(brief.Script, brief.Voice == "default" ? null : brief.Voice, OutputFolder(project), session, progress, ct);
            if (created.Error is not null)
            {
                return new StartResult(null, null, null, created.Error);
            }

            voicePath = created.Path;
            voiceCheck = created.Check;
        }

        double? voiceSeconds = voicePath is null ? null : (await _probe.ProbeAsync(voicePath, ct)).DurationSeconds;
        if (brief.Music is not null)
        {
            project.Media["music"] = Path.GetFullPath(brief.Music);
        }

        if (voicePath is not null)
        {
            project.Media["voiceover"] = voicePath;
        }

        // Only technical metadata leaves the machine: ids, durations, sizes. Never paths or footage.
        Report(progress, 12, "TodoX đang lên kế hoạch dựng");
        var planned = await _server.PlanAsync(new
        {
            clips = project.ClipMetadata(),
            aspect = brief.Aspect,
            targetSeconds = brief.TargetSeconds,
            title = brief.Title,
            subtitle = brief.Subtitle,
            script = brief.Script,
            outroText = brief.OutroText,
            hasMusic = brief.Music is not null,
            hasVoiceover = voicePath is not null,
            voiceoverSeconds = voiceSeconds,
            styleId = brief.Style,
            accentColor = brief.AccentColor,
            skill = project.Skill,
            skillVersion = project.SkillVersion
        }, ct);
        if (!planned.Ok)
        {
            session.Error("plan_failed", planned.Message, new { planned.ErrorCode });
            return new StartResult(null, null, null, ExplainEdit(planned));
        }

        project.Document = planned.Value.GetProperty("document").GetString()!;
        var plan = PlanDocument.Read(project.Document, _verifier);
        if (plan is null)
        {
            session.Error("plan_signature_invalid");
            return new StartResult(null, null, null, "Kế hoạch dựng không có chữ ký hợp lệ của TodoX. Hãy cập nhật TodoX EditVideo.");
        }

        // The skill may have chosen the frame; name files after what was planned.
        project.Aspect = AspectOf(plan.Width, plan.Height);
        _edits.Save(project);
        session.Info("planned", null, new { edit = project.Id, scenes = plan.Clips.Count, plan.TotalSeconds, style = plan.Style.Id, aspect = project.Aspect });
        return new StartResult(project, plan, voiceCheck, null);
    }

    /// <summary>Renders the signed plan; <paramref name="baseProgress"/> lets todox_auto_edit continue its own progress bar.</summary>
    private async Task<string> ExportAsync(EditProject project, string? output, SessionLogger session, IProgress<ProgressNotificationValue> progress, double baseProgress, CancellationToken ct, VoiceCheckResult? voiceCheck = null)
    {
        var missing = project.Media.Where(m => !File.Exists(m.Value)).Select(m => m.Value).ToList();
        if (missing.Count > 0)
        {
            return "Không tìm thấy file: " + string.Join(", ", missing.Take(5));
        }

        var plan = PlanDocument.Open(project.Document, _verifier, project.Media);
        if (plan is null)
        {
            session.Error("plan_signature_invalid");
            return "Kế hoạch dựng không có chữ ký hợp lệ của TodoX. Hãy bắt đầu lại bằng todox_edit_start.";
        }

        // Phone footage (rotated / HEVC / 4K) gets a cached 1080p proxy so rendering is stable and fast.
        var proxied = 0;
        var clipsReady = new List<PlanClip>();
        foreach (var clip in plan.Clips)
        {
            var info = await _probe.ProbeAsync(clip.Src, ct);
            if (ClipNormalizer.NeedsProxy(info))
            {
                Report(progress, baseProgress + 3, $"Chuẩn hóa clip {Path.GetFileName(info.Path)}");
                proxied++;
            }

            clipsReady.Add(clip with { Src = await _normalizer.PrepareAsync(info, ct) });
        }

        plan = plan with { Clips = clipsReady };
        session.Info("export", null, new { edit = project.Id, scenes = plan.Clips.Count, plan.TotalSeconds, style = plan.Style.Id, proxied, project.Aspect });

        var outPath = output ?? Path.Combine(OutputFolder(project), $"todox-{DateTime.Now:yyyyMMdd-HHmmss}-{project.Aspect.Replace(':', 'x')}.mp4");
        var start = baseProgress + 5;
        var last = -1.0;
        var result = await _renderer.RenderAsync(plan, outPath, new Progress<RenderProgress>(p =>
        {
            var pct = start + p.Percent * (100 - start) / 100;
            if (pct - last >= 2 || p.Percent >= 100)
            {
                last = pct;
                Report(progress, pct, p.Stage);
            }
        }), ct);

        session.Info("rendered", null, new { seconds = Math.Round(result.Seconds, 1), renderSeconds = Math.Round(result.Elapsed.TotalSeconds) });
        project.LastOutput = result.OutputPath;
        _edits.Save(project);

        // Every video is tagged with the skill version that made it: in the usage log (dashboard) and in a small
        // file next to the video, so versions can be compared later.
        var tag = SkillTag(project.Skill, project.SkillVersion);
        WriteVideoInfo(result.OutputPath, tag, plan.Style.Name, project.Aspect, result.Seconds);
        await session.EndAsync("ok", tag is null ? new { edit = project.Id } : new { edit = project.Id, skill = tag.Skill, skillVersion = tag.Version, skillVersionName = tag.VersionName, skillTitle = tag.Title }, ct);
        return JsonSerializer.Serialize(new
        {
            output = result.OutputPath,
            edit = project.Id,
            skill = tag is null ? null : new { tag.Skill, tag.Title, version = tag.Version, versionName = tag.VersionName },
            seconds = Math.Round(result.Seconds, 1),
            resolution = $"{plan.Width}x{plan.Height}",
            style = plan.Style.Name,
            scenes = plan.Clips.Count,
            clipsGiven = project.Clips.Count,
            captions = plan.Captions.Count,
            voiceover = project.Media.GetValueOrDefault("voiceover"),
            voiceCheck = voiceCheck is null ? null : new { voiceCheck.Score, voiceCheck.Ok, missing = voiceCheck.Missing.Take(10) },
            renderSeconds = Math.Round(result.Elapsed.TotalSeconds, 0),
            next = "Muốn sửa: todox_timeline rồi todox_cut / todox_join / todox_caption / todox_titles / todox_audio / todox_look, sau đó todox_export."
        }, StatusTools.Json);
    }

    private Task<string> ChangeAsync(string? edit, Func<EditProject, object> change, CancellationToken ct)
    {
        var project = _edits.Find(edit);
        return project is null ? Task.FromResult(NoEdit(edit)) : ChangeAsync(project, change, ct);
    }

    private async Task<string> ChangeAsync(EditProject project, Func<EditProject, object> change, CancellationToken ct, VoiceCheckResult? voiceCheck = null)
    {
        if (await ReadyProblemAsync(null, ct) is { } problem)
        {
            return problem;
        }

        var edited = await _server.PlanEditAsync(new { document = project.Document, clips = project.ClipMetadata(), edit = change(project) }, ct);
        if (!edited.Ok)
        {
            return ExplainEdit(edited);
        }

        var document = edited.Value.GetProperty("document").GetString()!;
        var plan = PlanDocument.Read(document, _verifier);
        if (plan is null)
        {
            return "Kế hoạch dựng không có chữ ký hợp lệ của TodoX. Hãy cập nhật TodoX EditVideo.";
        }

        project.Document = document;
        _edits.Save(project);
        return Timeline(project, plan, voiceCheck);
    }

    private async Task<string?> AddClipAsync(EditProject project, string file, CancellationToken ct)
    {
        string path;
        try
        {
            path = Path.GetFullPath(file);
        }
        catch (ArgumentException)
        {
            return null;
        }

        var existing = project.Clips.FirstOrDefault(c => string.Equals(c.Path, path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return existing.Id;
        }

        if (!File.Exists(path))
        {
            return null;
        }

        var info = await _probe.ProbeAsync(path, ct);
        var clip = new EditClip(project.NextClipId(), info.Path, info.DurationSeconds, info.Width, info.Height, info.HasVideo, info.HasAudio);
        project.Clips.Add(clip);
        project.Media[clip.Id] = clip.Path;
        return clip.Id;
    }

    private static string Timeline(EditProject project, AutoEditPlan plan, VoiceCheckResult? voiceCheck)
    {
        var files = project.Clips.ToDictionary(c => c.Id);
        var starts = PlanDocument.SceneStarts(plan);
        return JsonSerializer.Serialize(new
        {
            edit = project.Id,
            seconds = Math.Round(plan.TotalSeconds, 1),
            aspect = project.Aspect,
            resolution = $"{plan.Width}x{plan.Height}",
            style = plan.Style.Id,
            accentColor = plan.AccentColor,
            captionStyle = plan.Style.CaptionStyle,
            transition = plan.Style.Transition,
            transitionSeconds = plan.TransitionSeconds,
            intro = plan.Intro is null ? null : new { plan.Intro.Title, plan.Intro.Subtitle, plan.Intro.Seconds },
            scenes = plan.Clips.Select((c, i) => new
            {
                scene = i + 1,
                clip = c.Src,
                file = files.TryGetValue(c.Src, out var f) ? Path.GetFileName(f.Path) : c.Src,
                from = Math.Round(c.From, 2),
                seconds = Math.Round(c.Duration, 2),
                startsAt = starts[i],
                clipSound = c.KeepAudio
            }),
            outro = plan.Outro is null ? null : new { plan.Outro.Text, plan.Outro.Seconds },
            captions = plan.Captions.Select(c => new { c.Text, start = Math.Round(c.Start, 2), end = Math.Round(c.End, 2) }),
            music = plan.Music is null ? null : new { file = Path.GetFileName(project.Media.GetValueOrDefault("music")), plan.Music.Volume },
            voiceover = plan.Voiceover is null ? null : new { file = Path.GetFileName(project.Media.GetValueOrDefault("voiceover")), plan.Voiceover.Volume },
            voiceCheck = voiceCheck is null ? null : new { voiceCheck.Score, voiceCheck.Ok, missing = voiceCheck.Missing.Take(10) },
            clipsAvailable = project.Clips.Select(c => new { clip = c.Id, file = Path.GetFileName(c.Path), seconds = Math.Round(c.Seconds, 2), hasSound = c.HasAudio }),
            skill = project.Skill is null ? null : new { project.Skill, version = project.SkillVersion },
            lastOutput = project.LastOutput
        }, StatusTools.Json);
    }

    private static string AspectOf(int width, int height) => (width, height) switch
    {
        (1920, 1080) => "16:9",
        (1080, 1080) => "1:1",
        (1080, 1350) => "4:5",
        _ => "9:16"
    };

    private static string OutputFolder(EditProject project) =>
        project.Clips.FirstOrDefault() is { } first ? Path.Combine(Path.GetDirectoryName(first.Path)!, "output") : DefaultVoiceFolder();

    private static bool IsNone(string? value) =>
        value is not null && (value.Trim().Length == 0 || value.Trim().Equals("none", StringComparison.OrdinalIgnoreCase));

    private static string NoEdit(string? edit) => string.IsNullOrWhiteSpace(edit)
        ? "Chưa có bản dựng nào. Bắt đầu bằng todox_edit_start (hoặc todox_auto_edit)."
        : $"Không có bản dựng \"{edit}\". Bắt đầu bằng todox_edit_start (hoặc todox_auto_edit).";

    /// <summary>Server-side rule violations (scene too short, unknown style…) are explained as sent, so the assistant can fix the call.</summary>
    private static string ExplainEdit<T>(ServerResult<T> r) =>
        r.ErrorCode == "invalid_request" && !string.IsNullOrWhiteSpace(r.Message) ? "TodoX không áp dụng được thay đổi: " + r.Message : Explain(r);
}
