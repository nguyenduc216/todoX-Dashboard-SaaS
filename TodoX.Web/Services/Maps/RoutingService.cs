using System.Globalization;
using System.Text.Json;

namespace TodoX.Web.Services.Maps;

/// <summary>A driving route for the geo-layer video: the line to draw, its length and time.</summary>
public sealed record MapRoute(IReadOnlyList<double[]> Coords, double DistanceMeters, double Seconds);

/// <summary>
/// Driving routes inside Vietnam from TodoX's own GraphHopper (OpenStreetMap data, free, on the server beside the map
/// folder; see tools/routing). It listens on localhost only (Maps:RoutingUrl, default http://localhost:8989) and is
/// reached through the EditVideo API, so only licensed apps use it.
/// </summary>
public sealed class RoutingService
{
    public const string Unavailable = "routing_unavailable";
    public const string NotFound = "route_not_found";

    private readonly HttpClient _http;
    private readonly string _baseUrl;

    public RoutingService(HttpClient http, IConfiguration config)
    {
        _http = http;
        _baseUrl = (config["Maps:RoutingUrl"] is { Length: > 0 } url ? url : "http://localhost:8989").TrimEnd('/');
    }

    /// <summary>Vietnam with its islands; points outside are refused before calling the router.</summary>
    internal static bool InVietnam(double lon, double lat) => lon is >= 102 and <= 118 && lat is >= 7.5 and <= 23.6;

    internal static string Url(string baseUrl, double fromLon, double fromLat, double toLon, double toLat) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{baseUrl}/route?point={fromLat:0.######},{fromLon:0.######}&point={toLat:0.######},{toLon:0.######}&profile=car&points_encoded=false&instructions=false&calc_points=true");

    internal static MapRoute? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("paths", out var paths) || paths.GetArrayLength() == 0)
        {
            return null;
        }

        var path = paths[0];
        var coords = path.GetProperty("points").GetProperty("coordinates").EnumerateArray()
            .Select(c => new[] { Math.Round(c[0].GetDouble(), 6), Math.Round(c[1].GetDouble(), 6) }).ToList();
        return new MapRoute(coords, path.GetProperty("distance").GetDouble(), path.GetProperty("time").GetDouble() / 1000);
    }

    public async Task<(MapRoute? Route, string? Error, string? Message)> RouteAsync(double fromLon, double fromLat, double toLon, double toLat, CancellationToken ct)
    {
        if (!InVietnam(fromLon, fromLat) || !InVietnam(toLon, toLat))
        {
            return (null, "invalid_request", "Điểm đi và điểm đến phải nằm trong Việt Nam (kinh độ, vĩ độ).");
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            using var response = await _http.GetAsync(Url(_baseUrl, fromLon, fromLat, toLon, toLat), timeout.Token);
            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                return (null, NotFound, "Không tìm được đường giữa hai điểm này (điểm có thể nằm xa đường xe chạy).");
            }

            return Parse(body) is { } route ? (route, null, null) : (null, NotFound, "Không tìm được đường giữa hai điểm này.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return (null, Unavailable, "Dịch vụ tìm đường của TodoX đang tạm dừng. Thử lại sau.");
        }
    }
}
