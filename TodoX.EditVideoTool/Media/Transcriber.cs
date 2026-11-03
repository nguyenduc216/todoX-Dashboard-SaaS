using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using TodoX.EditVideoTool.Components;

namespace TodoX.EditVideoTool.Media;

public sealed record TranscriptSegment(double Start, double End, string Text);

public sealed record Transcript(string Text, IReadOnlyList<TranscriptSegment> Segments, double Seconds);

public sealed record VoiceCheckResult(double Score, bool Ok, IReadOnlyList<string> Missing, IReadOnlyList<string> Extra, string Transcript);

/// <summary>
/// Local speech-to-text with whisper.cpp (component "whisper": whisper-cli.exe + a ggml model). Used to listen back
/// to a voiceover and check it says what the script says. Runs offline; audio never leaves the machine.
/// </summary>
public sealed class Transcriber
{
    public const double PassScore = 0.85;

    private readonly ComponentLocator _components;
    private readonly AgentPaths _paths;

    public Transcriber(ComponentLocator components, AgentPaths paths)
    {
        _components = components;
        _paths = paths;
    }

    public string? ModelPath
    {
        get
        {
            var dir = Path.Combine(_paths.ComponentsDir, "whisper");
            return Directory.Exists(dir)
                ? Directory.EnumerateFiles(dir, "ggml-*.bin", SearchOption.AllDirectories).OrderByDescending(f => new FileInfo(f).Length).FirstOrDefault()
                : null;
        }
    }

    public bool IsAvailable => _components.Locate("whisper-cli").Installed && ModelPath is not null;

    public async Task<Transcript> TranscribeAsync(string mediaPath, string language = "vi", CancellationToken ct = default)
    {
        var cli = _components.Locate("whisper-cli");
        var model = ModelPath;
        if (!cli.Installed || model is null)
        {
            throw new InvalidOperationException("Chưa cài bộ nghe lại lời thoại. Mở TodoX EditVideo → Kiểm tra cập nhật.");
        }

        var work = Path.Combine(Path.GetTempPath(), $"todox-whisper-{Guid.NewGuid():N}");
        Directory.CreateDirectory(work);
        try
        {
            var wav = Path.Combine(work, "audio.wav");
            await RunAsync(_components.Locate("ffmpeg").Path ?? throw new InvalidOperationException("Chưa cài bộ ghép & xử lý video."),
                ["-nostdin", "-v", "error", "-y", "-i", mediaPath, "-vn", "-ar", "16000", "-ac", "1", "-c:a", "pcm_s16le", wav], ct);

            var outBase = Path.Combine(work, "out");
            await RunAsync(cli.Path!,
                ["-m", model, "-f", wav, "-l", language, "-oj", "-of", outBase, "-np", "-t", Math.Clamp(Environment.ProcessorCount - 1, 1, 8).ToString(CultureInfo.InvariantCulture)], ct);

            return Parse(await File.ReadAllTextAsync(outBase + ".json", Encoding.UTF8, ct));
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    internal static Transcript Parse(string whisperJson)
    {
        using var doc = JsonDocument.Parse(whisperJson);
        var segments = new List<TranscriptSegment>();
        if (doc.RootElement.TryGetProperty("transcription", out var items))
        {
            foreach (var item in items.EnumerateArray())
            {
                var text = item.TryGetProperty("text", out var t) ? t.GetString()?.Trim() ?? "" : "";
                double from = 0, to = 0;
                if (item.TryGetProperty("offsets", out var o))
                {
                    from = o.TryGetProperty("from", out var f) ? f.GetDouble() / 1000 : 0;
                    to = o.TryGetProperty("to", out var e) ? e.GetDouble() / 1000 : 0;
                }

                if (text.Length > 0)
                {
                    segments.Add(new TranscriptSegment(Math.Round(from, 2), Math.Round(to, 2), text));
                }
            }
        }

        return new Transcript(string.Join(' ', segments.Select(s => s.Text)), segments, segments.Count == 0 ? 0 : segments[^1].End);
    }

    /// <summary>
    /// Word-level comparison of what the voice says against the script (case, punctuation and tone marks are
    /// ignored so small recognition slips do not fail a good voiceover). Score = 2·LCS / (expected + heard).
    /// </summary>
    public static VoiceCheckResult Compare(string script, string transcript)
    {
        var expected = Words(script);
        var heard = Words(transcript);
        if (expected.Count == 0 && heard.Count == 0)
        {
            return new VoiceCheckResult(1, true, [], [], transcript);
        }

        var lcs = new int[expected.Count + 1, heard.Count + 1];
        for (var i = expected.Count - 1; i >= 0; i--)
        {
            for (var j = heard.Count - 1; j >= 0; j--)
            {
                lcs[i, j] = expected[i] == heard[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            }
        }

        var missing = new List<string>();
        var extra = new List<string>();
        int x = 0, y = 0;
        while (x < expected.Count && y < heard.Count)
        {
            if (expected[x] == heard[y]) { x++; y++; }
            else if (lcs[x + 1, y] >= lcs[x, y + 1]) { missing.Add(expected[x]); x++; }
            else { extra.Add(heard[y]); y++; }
        }

        missing.AddRange(expected.Skip(x));
        extra.AddRange(heard.Skip(y));
        var score = Math.Round(2.0 * lcs[0, 0] / (expected.Count + heard.Count), 3);
        return new VoiceCheckResult(score, score >= PassScore, missing.Take(30).ToList(), extra.Take(30).ToList(), transcript);
    }

    internal static List<string> Words(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat == UnicodeCategory.NonSpacingMark)
            {
                continue; // drop tone marks
            }

            sb.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : ' ');
        }

        return sb.ToString().Replace('đ', 'd').Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    private static async Task RunAsync(string exe, IReadOnlyList<string> args, CancellationToken ct)
    {
        // stdin must never be inherited: in MCP mode it is the assistant's channel.
        var psi = new ProcessStartInfo(exe) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        using var proc = Process.Start(psi)!;
        proc.StandardInput.Close();
        using var reg = ct.Register(() => { try { proc.Kill(true); } catch (InvalidOperationException) { } });
        var stdout = proc.StandardOutput.ReadToEndAsync(ct);
        var stderr = proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
        await stdout;
        if (proc.ExitCode != 0)
        {
            var err = (await stderr).Trim();
            throw new InvalidOperationException($"{Path.GetFileName(exe)} lỗi: " + (err.Length > 300 ? err[^300..] : err));
        }
    }
}
