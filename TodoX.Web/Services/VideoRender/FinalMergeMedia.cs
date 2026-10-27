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
        var decode = await RunAsync(ffmpeg, ["-v", "error", "-xerror", "-err_detect", "explode", "-i", path, "-map", "0:v:0", "-an", "-f", "null", "-"], timeout, ct);
        return decode.ExitCode == 0 && string.IsNullOrWhiteSpace(decode.Error) ? null : "video_decode_failed: " + decode.Error;
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
        await File.WriteAllTextAsync(Path.Combine(directory, "fallback-validation.log"), validation ?? "PASS", ct);
        if (validation is not null) throw new InvalidOperationException("Final merge validation failed: " + validation);
    }

    private static string Text(JsonElement value, string property)
        => value.TryGetProperty(property, out var item) ? item.ToString() : string.Empty;
}
