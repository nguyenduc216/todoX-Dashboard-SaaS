namespace TodoX.Web.Models;

public static class AiStudioAssetTypes
{
    public const string Voice = "voice";
    public const string Music = "music";
}

public sealed class AiStudioVoiceDto
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string ProviderCode { get; set; } = string.Empty;
    public string? ProviderVoiceId { get; set; }
    public string? CompatibilityAlias { get; set; }
    public string? Gender { get; set; }
    public string? LanguageCode { get; set; }
    public string? Region { get; set; }
    public string? Description { get; set; }
    public string? PreviewFileName { get; set; }
    public string? PreviewStorageKey { get; set; }
    public string? PreviewFileUrl { get; set; }
    public decimal DefaultRate { get; set; } = 1.0m;
    public decimal? MinRate { get; set; }
    public decimal? MaxRate { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDefault { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

public sealed class AiStudioMusicDto
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Category { get; set; } = "other";
    public string? FileName { get; set; }
    public string? StorageKey { get; set; }
    public string? FileUrl { get; set; }
    public int? DurationSeconds { get; set; }
    public string? MimeType { get; set; }
    public long? FileSize { get; set; }
    public decimal DefaultVolume { get; set; } = 0.8m;
    public bool LoopAllowed { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public bool IsDefault { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

public sealed class AiStudioCatalogFilter
{
    public string? Search { get; set; }
    public string? ProviderCode { get; set; }
    public string? Gender { get; set; }
    public string? LanguageCode { get; set; }
    public string? Category { get; set; }
    public bool? IsActive { get; set; }
}

public sealed class AiStudioMusicImportUrlRequest
{
    public string Url { get; set; } = string.Empty;
}

public sealed record AiStudioUploadResult(string FileName, string StorageKey, string FileUrl, string MimeType, long FileSize);

public static class AiStudioCatalogRules
{
    public const long MaxAudioBytes = 50L * 1024 * 1024;

    public static IReadOnlyList<string> MusicCategories { get; } =
    [
        "corporate",
        "cinematic",
        "upbeat",
        "chill",
        "travel",
        "fashion",
        "technology",
        "inspiration",
        "other"
    ];

    public static IReadOnlyDictionary<string, string> RVideoCompatibilityVoiceCodes { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["a1"] = "vbee_phuthang",
            ["a2"] = "vbee_ngochuyen",
            ["a3"] = "vbee_minhduc",
            ["a4"] = "custom"
        };

    public static void ValidateVoice(AiStudioVoiceDto voice)
    {
        if (string.IsNullOrWhiteSpace(voice.Name)) throw new InvalidOperationException("VOICE_NAME_REQUIRED");
        if (string.IsNullOrWhiteSpace(voice.Code)) throw new InvalidOperationException("VOICE_CODE_REQUIRED");
        if (string.IsNullOrWhiteSpace(voice.ProviderCode)) throw new InvalidOperationException("VOICE_PROVIDER_REQUIRED");
        if (RequiresProviderVoiceId(voice.ProviderCode) && string.IsNullOrWhiteSpace(voice.ProviderVoiceId)) throw new InvalidOperationException("VOICE_PROVIDER_ID_REQUIRED");
        if (voice.DefaultRate <= 0) throw new InvalidOperationException("VOICE_DEFAULT_RATE_INVALID");
        if (voice.MinRate is decimal min && min <= 0) throw new InvalidOperationException("VOICE_MIN_RATE_INVALID");
        if (voice.MaxRate is decimal max && max <= 0) throw new InvalidOperationException("VOICE_MAX_RATE_INVALID");
        if (voice.MinRate is decimal minRate && voice.MaxRate is decimal maxRate && minRate > maxRate) throw new InvalidOperationException("VOICE_RATE_RANGE_INVALID");
        if (voice.MinRate is decimal low && voice.DefaultRate < low) throw new InvalidOperationException("VOICE_DEFAULT_RATE_OUT_OF_RANGE");
        if (voice.MaxRate is decimal high && voice.DefaultRate > high) throw new InvalidOperationException("VOICE_DEFAULT_RATE_OUT_OF_RANGE");
    }

    public static void ValidateMusic(AiStudioMusicDto music)
    {
        if (string.IsNullOrWhiteSpace(music.Name)) throw new InvalidOperationException("MUSIC_NAME_REQUIRED");
        if (string.IsNullOrWhiteSpace(music.Code)) throw new InvalidOperationException("MUSIC_CODE_REQUIRED");
        if (music.DefaultVolume is < 0 or > 1) throw new InvalidOperationException("MUSIC_VOLUME_INVALID");
        if (string.IsNullOrWhiteSpace(music.Category)) music.Category = "other";
    }

    public static bool HasLocalMusicFile(AiStudioMusicDto music)
        => !string.IsNullOrWhiteSpace(music.FileName)
           && !string.IsNullOrWhiteSpace(music.StorageKey)
           && !string.IsNullOrWhiteSpace(music.FileUrl)
           && music.FileUrl.StartsWith("/", StringComparison.Ordinal)
           && string.Equals(music.MimeType, "audio/mpeg", StringComparison.OrdinalIgnoreCase);

    public static void EnsureMusicCanBeActive(AiStudioMusicDto music)
    {
        if (music.IsActive && !HasLocalMusicFile(music))
        {
            throw new InvalidOperationException("MUSIC_FILE_REQUIRED");
        }
    }

    public static void ValidateMusicMp3Upload(string fileName, string? contentType, long length)
    {
        if (length <= 0) throw new InvalidOperationException("AUDIO_FILE_EMPTY");
        if (length > MaxAudioBytes) throw new InvalidOperationException("AUDIO_FILE_TOO_LARGE");

        if (!string.Equals(Path.GetExtension(fileName), ".mp3", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(NormalizeAudioMime(contentType, fileName), "audio/mpeg", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("MUSIC_MP3_REQUIRED");
        }
    }

    public static void ValidateAudioUpload(string fileName, string? contentType, long length, bool allowWaveAndM4a = true)
    {
        if (length <= 0) throw new InvalidOperationException("AUDIO_FILE_EMPTY");
        if (length > MaxAudioBytes) throw new InvalidOperationException("AUDIO_FILE_TOO_LARGE");

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var normalizedMime = NormalizeAudioMime(contentType, fileName);
        var allowedExtension = ext == ".mp3" || (allowWaveAndM4a && ext is ".wav" or ".m4a");
        var allowedMime = normalizedMime == "audio/mpeg"
                          || (allowWaveAndM4a && normalizedMime is "audio/wav" or "audio/x-wav" or "audio/mp4" or "audio/m4a");

        if (!allowedExtension || !allowedMime)
        {
            throw new InvalidOperationException("AUDIO_FILE_TYPE_INVALID");
        }
    }

    public static string NormalizeCode(string code)
        => string.Concat((code ?? string.Empty).Trim().ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) || c == '_' || c == '-' ? c : '_'));

    public static string NormalizeAudioMime(string? contentType, string fileName)
    {
        var mime = (contentType ?? string.Empty).Split(';')[0].Trim().ToLowerInvariant();
        if (mime is "audio/mp3" or "application/mp3" or "application/x-mpeg") return "audio/mpeg";
        if (mime is "audio/wave") return "audio/wav";
        if (mime is "audio/x-m4a") return "audio/m4a";
        if (!string.IsNullOrWhiteSpace(mime) && mime != "application/octet-stream") return mime;

        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".mp3" => "audio/mpeg",
            ".wav" => "audio/wav",
            ".m4a" => "audio/mp4",
            _ => mime
        };
    }

    private static bool RequiresProviderVoiceId(string providerCode)
        => !string.Equals(providerCode, "custom", StringComparison.OrdinalIgnoreCase);
}
