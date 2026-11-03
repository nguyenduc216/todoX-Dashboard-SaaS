using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace TodoX.EditVideoTool.Licensing;

public sealed record DeviceSession(string License, string AccessToken, DateTimeOffset AccessTokenExpiresAt, DateTimeOffset HeartbeatDueAt);

public sealed record LicenseApiResult<T>(T? Value, string? ErrorCode, string? Message, JsonElement? Detail = null)
{
    public bool Ok => ErrorCode is null;
}

/// <summary>Talks to TodoX.Web /api/editvideo/v1. Only small JSON is exchanged — never media.</summary>
public sealed class LicenseApiClient
{
    public const string NetworkError = "network_error";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;

    public LicenseApiClient(HttpClient http)
    {
        _http = http;
    }

    public static string DefaultServerUrl =>
        Environment.GetEnvironmentVariable("TODOX_EDITVIDEO_SERVER")?.TrimEnd('/') is { Length: > 0 } fromEnv
            ? fromEnv
            : "https://dashboard.todox.vn";

    public Task<LicenseApiResult<DeviceSession>> ActivateAsync(string serverUrl, string licenseKey, IDeviceIdentity device, string deviceKind, string appVersion, CancellationToken ct = default) =>
        SendAsync<DeviceSession>(HttpMethod.Post, serverUrl, "activate", null, device.DeviceHash, new
        {
            licenseKey,
            deviceHash = device.DeviceHash,
            deviceKind,
            deviceName = device.DeviceName,
            os = device.Os,
            appVersion
        }, ct);

    public Task<LicenseApiResult<DeviceSession>> HeartbeatAsync(string serverUrl, string accessToken, IDeviceIdentity device, string appVersion, CancellationToken ct = default) =>
        SendAsync<DeviceSession>(HttpMethod.Post, serverUrl, "heartbeat", accessToken, device.DeviceHash,
            new { deviceHash = device.DeviceHash, appVersion }, ct);

    public Task<LicenseApiResult<JsonElement>> DeactivateAsync(string serverUrl, string accessToken, IDeviceIdentity device, CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Post, serverUrl, "deactivate", accessToken, device.DeviceHash, new { }, ct);

    private async Task<LicenseApiResult<T>> SendAsync<T>(HttpMethod method, string serverUrl, string path, string? accessToken, string deviceHash, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, $"{serverUrl.TrimEnd('/')}/api/editvideo/v1/{path}");
        request.Headers.Add("X-TodoX-Device", deviceHash);
        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: Json);
        }

        try
        {
            using var response = await _http.SendAsync(request, ct);
            var envelope = await response.Content.ReadFromJsonAsync<Envelope<T>>(Json, ct);
            if (envelope is null)
            {
                return new LicenseApiResult<T>(default, NetworkError, $"Server trả về rỗng (HTTP {(int)response.StatusCode}).");
            }

            return envelope.Success
                ? new LicenseApiResult<T>(envelope.Data, null, null)
                : new LicenseApiResult<T>(default, envelope.Code ?? "server_error", envelope.Message, envelope.Detail);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or NotSupportedException)
        {
            return new LicenseApiResult<T>(default, NetworkError, "Không kết nối được máy chủ TodoX.");
        }
    }

    private sealed record Envelope<T>(bool Success, T? Data, string? Code, string? Message, JsonElement? Detail);
}
