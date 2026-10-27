using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace TodoX.Web.Services.VideoRender;

// Local media processing only. No provider, scene render, or point dependencies.
internal static class FinalMergeMedia
{
    internal sealed record Input(string Path, int Seconds);
    internal sealed record Probe(string Codec, int Width, int Height, string PixelFormat, string Fps,
        string TimeBase, string ExtraData, bool Audio, string AudioSignature, string StreamLayout);
    internal sealed record Result(int ExitCode, string Output, string Error, string Arguments = "")
    {
        public string Log => $"arguments={Arguments}\nexitCode={ExitCode}\n{Output}\n{Error}";
    }

    internal static async Task<Result> RunAsync(string executable, IReadOnlyList<string> args, TimeSpan timeout, CancellationToken ct)
    {
        var start = new ProcessStartInfo(executable) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start media tool.");
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);
        try { await process.WaitForExitAsync(deadline.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            if (ct.IsCancellationRequested) throw;
            throw new TimeoutException("Final merge media tool timed out.");
        }
        return new(process.ExitCode, await stdout, await stderr, JsonSerializer.Serialize(args));
    }

    internal static string ProbeExecutable(string ffmpeg)
    {
        var directory = Path.GetDirectoryName(ffmpeg);
        var name = Path.GetExtension(ffmpeg).Equals(".exe", StringComparison.OrdinalIgnoreCase) ? "ffprobe.exe" : "ffprobe";
        return string.IsNullOrEmpty(directory) ? name : Path.Combine(directory, name);
    }

    internal static async Task<Probe> InspectAsync(string ffmpeg, string path, TimeSpan timeout, CancellationToken ct)
    {
        var result = await RunAsync(ProbeExecutable(ffmpeg), ["-v", "error", "-show_streams", "-show_data", "-of", "json", path], timeout, ct);
        if (result.ExitCode != 0) throw new InvalidOperationException($"ffprobe failed: {result.Error}");
        using var doc = JsonDocument.Parse(result.Output);
        var streams = doc.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        var videos = streams.Where(s => Text(s, "codec_type") == "video").ToArray();
        if (videos.Length != 1) throw new InvalidOperationException("Final merge requires exactly one video stream per input.");
        var v = videos[0];
        var audio = streams.Where(s => Text(s, "codec_type") == "audio").ToArray();
        return new(Text(v, "codec_name"), v.GetProperty("width").GetInt32(), v.GetProperty("height").GetInt32(),
            Text(v, "pix_fmt"), Text(v, "avg_frame_rate"), Text(v, "time_base"), Text(v, "extradata"), audio.Length > 0,
            string.Join(";", audio.Select(a => $"{Text(a, "codec_name")}/{Text(a, "sample_rate")}/{Text(a, "channels")}/{Text(a, "time_base")}/{Text(a, "extradata")}")),
            string.Join(",", streams.Select(s => Text(s, "codec_type"))));
    }

    internal static bool CanCopy(IReadOnlyList<Probe> probes)
        => probes.Count > 0 && probes[0].Codec == "h264" && probes[0].PixelFormat == "yuv420p" && probes.All(p => p == probes[0]);

    internal static async Task<string?> ValidateAsync(string ffmpeg, string path, decimal seconds, TimeSpan timeout, CancellationToken ct)
    {
        if (!File.Exists(path) || new FileInfo(path).Length == 0) return "output_missing_or_empty";
        var frames = await RunAsync(ProbeExecutable(ffmpeg), ["-v", "error", "-select_streams", "v:0", "-show_streams", "-show_frames", "-show_entries", "stream=codec_type,start_time,duration:frame=best_effort_timestamp_time", "-of", "json", path], timeout, ct);
        if (frames.ExitCode != 0 || !string.IsNullOrWhiteSpace(frames.Error)) return "video_timestamp_probe_failed: " + frames.Error;
        using var doc = JsonDocument.Parse(frames.Output);
        if (!doc.RootElement.TryGetProperty("streams", out var streams) || streams.GetArrayLength() != 1)
            return "missing_output_video_stream";
        var video = streams[0];
        if (!decimal.TryParse(Text(video, "duration"), NumberStyles.Float, CultureInfo.InvariantCulture, out var duration)
            || Math.Abs(duration - seconds) > .25m) return "video_stream_duration_mismatch";
        var times = doc.RootElement.GetProperty("frames").EnumerateArray()
            .Select(f => decimal.TryParse(Text(f, "best_effort_timestamp_time"), NumberStyles.Float, CultureInfo.InvariantCulture, out var t) ? (decimal?)t : null).ToArray();
        var error = ValidateTimeline(times, seconds);
        if (error is not null) return error;
        var decode = await RunAsync(ffmpeg, ["-v", "error", "-xerror", "-err_detect", "explode", "-i", path, "-map", "0:v:0", "-map", "0:a?", "-f", "null", "-"], timeout, ct);
        if (decode.ExitCode != 0 || !string.IsNullOrWhiteSpace(decode.Error)) return "media_decode_failed: " + decode.Error;
        return await ValidateAudioAsync(ffmpeg, path, seconds, timeout, ct);
    }

    internal static async Task<string?> ValidateAudioAsync(string ffmpeg, string path, decimal seconds, TimeSpan timeout, CancellationToken ct)
    {
        var probe = await RunAsync(ProbeExecutable(ffmpeg),
            ["-v", "error", "-select_streams", "a", "-show_streams", "-show_frames", "-show_entries",
                "stream=start_time,duration:frame=best_effort_timestamp_time,nb_samples,sample_rate", "-of", "json", path], timeout, ct);
        if (probe.ExitCode != 0 || !string.IsNullOrWhiteSpace(probe.Error)) return "audio_probe_failed";
        using var metadata = JsonDocument.Parse(probe.Output);
        var streams = metadata.RootElement.GetProperty("streams");
        if (streams.GetArrayLength() == 0) return null; // Silent selected sources are supported.
        if (streams.GetArrayLength() != 1) return "unexpected_audio_stream_count";
        var audio = streams[0];
        bool Number(JsonElement element, string key, out decimal value)
            => decimal.TryParse(Text(element, key), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        if (!Number(audio, "start_time", out var start) || Math.Abs(start) > .25m
            || !Number(audio, "duration", out var duration) || Math.Abs(duration - seconds) > .25m)
            return "audio_video_timeline_mismatch";
        var times = metadata.RootElement.GetProperty("frames").EnumerateArray()
            .Select(f => Number(f, "best_effort_timestamp_time", out var time) ? (decimal?)time : null).ToArray();
        return ValidateTimeline(times, seconds) is null ? null : "audio_timestamp_coverage_invalid";
    }

    // Compare each configured output interval with its selected source. A static source is valid;
    // a moving source becoming a single repeated sampled picture is not. This is a bounded
    // motion-loss screen, not proof of semantic/lip synchronization or frame-exact identity.
    internal static async Task<string?> ValidateMotionAsync(string ffmpeg, string output, IReadOnlyList<Input> inputs, TimeSpan timeout, CancellationToken ct)
    {
        decimal offset = 0;
        for (var i = 0; i < inputs.Count; i++)
        {
            var source = await MotionSamplesAsync(ffmpeg, inputs[i].Path, 0, inputs[i].Seconds, timeout, ct);
            var final = await MotionSamplesAsync(ffmpeg, output, offset, inputs[i].Seconds, timeout, ct);
            if (source.Length < 2048 || final.Length < 2048) return $"scene_motion_samples_missing:{i}";
            if (HasMotion(source) && !HasMotion(final))
                return $"scene_motion_lost:{i}:offset={offset.ToString(CultureInfo.InvariantCulture)}";
            offset += inputs[i].Seconds;
        }
        return null;
    }

    private static bool HasMotion(byte[] frames)
    {
        const int pixels = 32 * 32;
        for (var offset = pixels; offset < frames.Length; offset += pixels)
        {
            long difference = 0;
            for (var pixel = 0; pixel < pixels; pixel++)
                difference += Math.Abs(frames[offset + pixel] - frames[offset - pixels + pixel]);
            if (difference > pixels) return true; // Ignore sub-luma compression noise.
        }
        return false;
    }

    private static async Task<byte[]> MotionSamplesAsync(string ffmpeg, string path, decimal start, int seconds, TimeSpan timeout, CancellationToken ct)
    {
        var samples = Path.Combine(Path.GetTempPath(), $"todox-motion-{Guid.NewGuid():N}.gray");
        try
        {
            var result = await RunAsync(ffmpeg, ["-y", "-v", "error", "-ss", start.ToString(CultureInfo.InvariantCulture), "-i", path,
                "-t", seconds.ToString(CultureInfo.InvariantCulture), "-map", "0:v:0", "-an", "-vf", "fps=2,scale=32:32,format=gray", "-f", "rawvideo", samples], timeout, ct);
            if (result.ExitCode != 0 || !string.IsNullOrWhiteSpace(result.Error)) throw new InvalidOperationException("Motion validation decode failed: " + result.Error);
            var bytes = await File.ReadAllBytesAsync(samples, ct);
            if (bytes.Length % 1024 != 0) throw new InvalidOperationException("Incomplete motion samples.");
            return bytes;
        }
        finally { if (File.Exists(samples)) File.Delete(samples); }
    }

    internal static string? ValidateTimeline(IReadOnlyList<decimal?> times, decimal seconds)
    {
        if (times.Count < 2 || times.Any(t => t is null)) return "missing_video_frames_or_timestamps";
        if (Math.Abs(times[0]!.Value) > .25m) return "video_start_timestamp_out_of_range";
        for (var i = 1; i < times.Count; i++)
            if (times[i] <= times[i - 1] || times[i] - times[i - 1] > .5m) return "non_monotonic_or_gapped_video_timestamps";
        if (Math.Abs(times[^1]!.Value - seconds) > .25m) return "video_duration_mismatch";
        return null;
    }

    internal static async Task MergeAsync(string ffmpeg, string directory, string concat, string output,
        IReadOnlyList<Input> inputs, decimal seconds, TimeSpan timeout, Func<string, Task> fallbackEvent, CancellationToken ct)
    {
        var probes = new List<Probe>();
        foreach (var input in inputs)
        {
            probes.Add(await InspectAsync(ffmpeg, input.Path, timeout, ct));
            var metadata = await RunAsync(ProbeExecutable(ffmpeg), ["-v", "error", "-show_streams", "-show_format", "-of", "json", input.Path], timeout, ct);
            await File.WriteAllTextAsync(Path.Combine(directory, $"input-{probes.Count - 1:D4}-probe.json"), metadata.Output, ct);
        }
        await File.WriteAllTextAsync(Path.Combine(directory, "input-probes.json"), JsonSerializer.Serialize(probes), ct);
        string? reason = "incompatible_input_streams";
        if (CanCopy(probes))
        {
            var copy = await RunAsync(ffmpeg, VideoRenderMergeHandler.BuildCopyConcatArguments(concat, output, seconds), timeout, ct);
            await File.WriteAllTextAsync(Path.Combine(directory, "ffmpeg-copy.log"), copy.Log, ct);
            reason = copy.ExitCode == 0 ? await ValidateAsync(ffmpeg, output, seconds, timeout, ct) : "copy_exit_code_" + copy.ExitCode;
            if (reason is null) reason = await ValidateMotionAsync(ffmpeg, output, inputs, timeout, ct);
            if (reason is null && probes.Any(p => p.Audio) && !(await InspectAsync(ffmpeg, output, timeout, ct)).Audio)
                reason = "selected_scene_audio_missing";
        }
        else await File.WriteAllTextAsync(Path.Combine(directory, "ffmpeg-copy.log"), "skipped: " + reason, ct);
        await File.WriteAllTextAsync(Path.Combine(directory, "copy-validation.log"), reason ?? "PASS", ct);
        if (reason is null) return;
        await fallbackEvent(reason);
        var target = probes[0];
        var fps = target.Fps.Split('/');
        if (fps.Length != 2 || !decimal.TryParse(fps[0], out var numerator) || !decimal.TryParse(fps[1], out var denominator) || numerator <= 0 || denominator <= 0)
            throw new InvalidOperationException("Invalid source frame rate.");
        var normalized = new List<string>();
        var anyAudio = probes.Any(p => p.Audio);
        for (var i = 0; i < inputs.Count; i++)
        {
            var input = inputs[i];
            var destination = Path.Combine(directory, $"normalized-{i:D4}.mp4");
            var duration = input.Seconds.ToString(CultureInfo.InvariantCulture);
            var args = new List<string> { "-y", "-v", "warning", "-i", input.Path };
            if (anyAudio && !probes[i].Audio) args.AddRange(["-f", "lavfi", "-i", "anullsrc=r=48000:cl=stereo"]);
            args.AddRange(["-map", "0:v:0", "-vf", $"setpts=PTS-STARTPTS,scale={target.Width}:{target.Height}:force_original_aspect_ratio=decrease,pad={target.Width}:{target.Height}:(ow-iw)/2:(oh-ih)/2,setsar=1,fps={target.Fps},tpad=stop_mode=clone:stop_duration={duration}", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-video_track_timescale", "90000"]);
            if (anyAudio) args.AddRange(["-map", probes[i].Audio ? "0:a:0" : "1:a:0", "-af", "aresample=48000:async=1:first_pts=0,apad", "-c:a", "aac", "-ar", "48000", "-ac", "2"]);
            else args.Add("-an");
            args.AddRange(["-t", duration, destination]);
            var result = await RunAsync(ffmpeg, args, timeout, ct);
            await File.WriteAllTextAsync(Path.Combine(directory, $"ffmpeg-normalize-{i:D4}.log"), result.Log, ct);
            if (result.ExitCode != 0) throw new InvalidOperationException($"Scene normalization failed: scene order {i}. {VideoRenderMergeHandler.SafeTail(result.Error)}");
            normalized.Add(destination);
        }
        var normalizedConcat = Path.Combine(directory, "concat-normalized.txt");
        await File.WriteAllLinesAsync(normalizedConcat, VideoRenderMergeHandler.BuildConcatLines(normalized), new System.Text.UTF8Encoding(false), ct);
        var fallback = await RunAsync(ffmpeg, VideoRenderMergeHandler.BuildTranscodeConcatArguments(normalizedConcat, output, seconds), timeout, ct);
        await File.WriteAllTextAsync(Path.Combine(directory, "ffmpeg-fallback.log"), fallback.Log, ct);
        var validation = fallback.ExitCode == 0 ? await ValidateAsync(ffmpeg, output, seconds, timeout, ct) : "fallback_exit_code_" + fallback.ExitCode;
        if (validation is null) validation = await ValidateMotionAsync(ffmpeg, output, inputs, timeout, ct);
        if (validation is null && anyAudio && !(await InspectAsync(ffmpeg, output, timeout, ct)).Audio)
            validation = "selected_scene_audio_missing";
        await File.WriteAllTextAsync(Path.Combine(directory, "fallback-validation.log"), validation ?? "PASS", ct);
        if (validation is not null) throw new InvalidOperationException("Final merge validation failed: " + validation);
    }

    private static string Text(JsonElement value, string property)
        => value.TryGetProperty(property, out var item) ? item.ToString() : string.Empty;
}
