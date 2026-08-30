using Dapper;
using Microsoft.Extensions.Options;
using TodoX.Web.Data;

namespace TodoX.Web.Services.VideoRender;

public interface IVbeeRuntimeConfigProvider
{
    Task<VbeeOptions> GetAsync(CancellationToken ct = default);
}

public sealed class VbeeRuntimeConfigProvider : IVbeeRuntimeConfigProvider
{
    internal const string LoadConfigSql =
        """
        SELECT
            config_key,
            config_value #>> '{}' AS config_value
          FROM public.todox_config
         WHERE config_key = ANY(@Keys);
        """;

    public const string TokenKey = "rvideo.vbee.token";
    public const string ApiBaseKey = "rvideo.vbee.api_base";
    public const string TtsUrlKey = "rvideo.vbee.tts_url";
    public const string AppIdKey = "rvideo.vbee.app_id";
    public const string BitrateKey = "rvideo.vbee.bitrate";
    public const string SpeedRateKey = "rvideo.vbee.speed_rate";
    public const string SampleRateKey = "rvideo.vbee.sample_rate";
    public const string CallbackSecretKey = "rvideo.vbee.callback_secret";

    private readonly TodoXAutomationConnectionFactory _factory;
    private readonly IConfiguration _configuration;
    private readonly IOptionsMonitor<VbeeOptions> _fallback;

    public VbeeRuntimeConfigProvider(
        TodoXAutomationConnectionFactory factory,
        IConfiguration configuration,
        IOptionsMonitor<VbeeOptions> fallback)
    {
        _factory = factory;
        _configuration = configuration;
        _fallback = fallback;
    }

    public async Task<VbeeOptions> GetAsync(CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<ConfigRow>(
            new CommandDefinition(
                LoadConfigSql,
                new { Keys = new[]
                {
                    TokenKey,
                    ApiBaseKey,
                    TtsUrlKey,
                    AppIdKey,
                    BitrateKey,
                    SpeedRateKey,
                    SampleRateKey,
                    CallbackSecretKey
                } },
                cancellationToken: ct));

        return Resolve(
            rows.ToDictionary(row => row.config_key, row => Normalize(row.config_value), StringComparer.OrdinalIgnoreCase),
            _configuration,
            _fallback.CurrentValue);
    }

    public static VbeeOptions Resolve(
        IReadOnlyDictionary<string, string?>? dbValues,
        IConfiguration configuration,
        VbeeOptions fallback)
    {
        dbValues ??= new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var resolved = new VbeeOptions
        {
            ApiBaseUrl = FirstNonBlank(dbValues.GetValueOrDefault(ApiBaseKey), fallback.ApiBaseUrl, configuration["Vbee:ApiBaseUrl"], configuration["VBEE_API_BASE_URL"], VbeeOptions.DefaultApiBaseUrl)!,
            TtsPath = ResolveTtsPath(dbValues.GetValueOrDefault(TtsUrlKey), dbValues.GetValueOrDefault(ApiBaseKey), fallback, configuration),
            ApiToken = FirstNonBlank(dbValues.GetValueOrDefault(TokenKey), fallback.ApiToken, configuration["Vbee:ApiToken"], configuration["VBEE_API_TOKEN"]),
            AppId = FirstNonBlank(dbValues.GetValueOrDefault(AppIdKey), fallback.AppId, configuration["Vbee:AppId"], configuration["VBEE_APP_ID"]),
            CallbackSecret = FirstNonBlank(dbValues.GetValueOrDefault(CallbackSecretKey), fallback.CallbackSecret, configuration["Vbee:CallbackSecret"], configuration["VBEE_CALLBACK_SECRET"]),
            DefaultSampleRate = FirstPositiveInt(dbValues.GetValueOrDefault(SampleRateKey), fallback.DefaultSampleRate),
            DefaultBitrate = FirstPositiveInt(dbValues.GetValueOrDefault(BitrateKey), fallback.DefaultBitrate),
            DefaultSpeedRate = FirstPositiveDecimal(dbValues.GetValueOrDefault(SpeedRateKey), fallback.DefaultSpeedRate),
            HttpTimeoutSeconds = fallback.HttpTimeoutSeconds,
            PollIntervalSeconds = fallback.PollIntervalSeconds,
            MaxPollCount = fallback.MaxPollCount,
            VoiceSampleRates = new Dictionary<string, int>(fallback.VoiceSampleRates, StringComparer.OrdinalIgnoreCase)
        };

        var callbackUrl = ResolveCallbackUrl(configuration, resolved.CallbackSecret);
        if (callbackUrl is not null)
        {
            resolved.CallbackUrl = callbackUrl;
        }

        return resolved;
    }

    public static string? ResolveCallbackUrl(IConfiguration configuration, string? callbackSecret)
    {
        var publicBaseUrl = FirstNonBlank(
            configuration["TodoX:PublicBaseUrl"],
            configuration["App:PublicBaseUrl"],
            configuration["Storage:PublicBaseUrl"]);
        if (string.IsNullOrWhiteSpace(publicBaseUrl))
        {
            return null;
        }

        var baseUri = new Uri(publicBaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        var callbackUri = new Uri(baseUri, VbeeOptions.DefaultCallbackPath.TrimStart('/'));
        return VbeeOptions.BuildAuthorizedCallbackUriOrNull(callbackUri.ToString(), callbackSecret)?.ToString();
    }

    public static string ResolveTtsPath(string? ttsUrl, string? apiBaseUrl, VbeeOptions fallback, IConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(ttsUrl))
        {
            return fallback.TtsPath;
        }

        if (!Uri.TryCreate(ttsUrl.Trim(), UriKind.Absolute, out var uri))
        {
            return fallback.TtsPath;
        }

        var configuredBase = FirstNonBlank(apiBaseUrl, configuration["Vbee:ApiBaseUrl"], configuration["VBEE_API_BASE_URL"], fallback.ApiBaseUrl, VbeeOptions.DefaultApiBaseUrl)!;
        if (Uri.TryCreate(configuredBase.TrimEnd('/') + "/", UriKind.Absolute, out var baseUri)
            && string.Equals(uri.GetLeftPart(UriPartial.Authority), baseUri.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase)
            && uri.AbsolutePath.StartsWith(baseUri.AbsolutePath.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase))
        {
            return "/" + uri.AbsolutePath[baseUri.AbsolutePath.TrimEnd('/').Length..].TrimStart('/');
        }

        return uri.PathAndQuery.StartsWith('/') ? uri.PathAndQuery : "/" + uri.PathAndQuery;
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static int FirstPositiveInt(string? value, int fallback)
        => int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;

    private static decimal FirstPositiveDecimal(string? value, decimal fallback)
        => decimal.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;

    private sealed class ConfigRow
    {
        public string config_key { get; set; } = string.Empty;
        public string? config_value { get; set; }
    }
}
