using System.Text.Json;

namespace TodoX.Web.Models.Catalog;

public sealed class ServiceJobDefaults
{
    public int Version { get; set; } = 1;
    public string Type { get; set; } = "service_job_defaults";
    public string? AspectRatio { get; set; }
    public string? Resolution { get; set; }
    public int? TotalSeconds { get; set; }
    public int? SceneSeconds { get; set; }
    public string? ExecutionMode { get; set; }
    public string? CharacterMode { get; set; }
    public bool? UseReferenceImageForAllScenes { get; set; }
    public string? VoiceMode { get; set; }
    public string? VoiceCatalogCode { get; set; }
    public decimal? VoiceVolume { get; set; }
    public decimal? DefaultTtsRate { get; set; }
    public string? MusicMode { get; set; }
    public string? MusicCatalogCode { get; set; }
    public decimal? MusicVolume { get; set; }
    public string? ProfileCode { get; set; }
    public int? SceneCount { get; set; }
    public string? VideoMode { get; set; }
    public string? Ratio { get; set; }
    public bool? RequireVideoConfirmation { get; set; }
    public bool? AutoFinish { get; set; }
    public string? ModelMode { get; set; }
    public string? CharacterProductMode { get; set; }
}

public static class ServiceJobDefaultsCodec
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static ServiceJobDefaults FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            var value = JsonSerializer.Deserialize<ServiceJobDefaults>(json, Options);
            return value is { Type: "service_job_defaults", Version: >= 1 } ? value : new();
        }
        catch (JsonException)
        {
            return new();
        }
    }

    public static string ToJson(ServiceJobDefaults value)
    {
        value.Version = Math.Max(1, value.Version);
        value.Type = "service_job_defaults";
        return JsonSerializer.Serialize(value, Options);
    }
}
