using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using TodoX.EditVideoTool.Media;
using TodoX.EditVideoTool.Server;

namespace TodoX.EditVideoTool.Mcp;

/// <summary>A land plot drawn on the map: either its corners, or a rectangle measured in metres from the site.</summary>
public sealed record GeoPlotInput(
    [property: Description("Short id shown on the label, e.g. A, B, C.")] string Id,
    [property: Description("Land use, e.g. 'Đất ở liền kề (ODT)', 'Cây xanh – công viên'.")] string Name,
    [property: Description("Area as text, e.g. '8,1 ha' or '120 m²'.")] string? Area = null,
    [property: Description("Facing, e.g. 'Hướng Đông Nam'.")] string? Direction = null,
    [property: Description("Hex colour of the glowing border; omit for TodoX's palette (red, green, yellow, blue...).")] string? Color = null,
    [property: Description("Corners as [lon, lat] pairs (at least 3), from the customer's map or planning drawing.")] double[][]? Corners = null,
    [property: Description("Or a rectangle: width east-west in metres.")] double? WidthMeters = null,
    [property: Description("Rectangle depth north-south in metres.")] double? DepthMeters = null,
    [property: Description("Rectangle centre: metres east of the site pin (negative = west).")] double? EastMeters = null,
    [property: Description("Rectangle centre: metres north of the site pin (negative = south).")] double? NorthMeters = null,
    [property: Description("Rectangle rotation in degrees, counter-clockwise.")] double? RotationDegrees = null);

/// <summary>An amenity near the site.</summary>
public sealed record GeoPoiInput(
    [property: Description("metro | school | uni | market | health | park | mall | bank")] string Kind,
    [property: Description("Name shown on the map, e.g. 'Ga Metro Phước Long'.")] string Name,
    [property: Description("Longitude.")] double Lon,
    [property: Description("Latitude.")] double Lat);

/// <summary>A place the video drives to from the site (route from TodoX's router).</summary>
public sealed record GeoDestinationInput(
    [property: Description("Name shown at the end of the route, e.g. 'TRUNG TÂM QUẬN 1', 'SÂN BAY TÂN SƠN NHẤT'.")] string Name,
    [property: Description("Longitude.")] double Lon,
    [property: Description("Latitude.")] double Lat);

public sealed partial class VideoTools
{
    internal static readonly string[] PlotColors = ["#FF4D6D", "#2EE59D", "#FFC83D", "#4C9DFF", "#C77DFF", "#FF8A2A"];
    internal static readonly string[] RouteColors = ["#FFC83D", "#18C5E8", "#FF5FB0"];
    private static readonly string[] PoiKinds = ["metro", "school", "uni", "market", "health", "park", "mall", "bank"];
    private static readonly CultureInfo Vi = new("vi-VN");

    [McpServerTool(Name = "todox_geo_video", Destructive = false)]
    [Description("Make a real-estate LOCATION video ('geo layer') on TodoX's own map of Vietnam: the camera flies in to the site, " +
                 "plot boundaries glow in colour with labels (land use, area, facing) and a legend, amenities pop up with icons and distances, " +
                 "glowing driving routes to key places (real roads from TodoX's router), a compass and an end card with hotline. " +
                 "Needs exact coordinates: ask the customer for the site's [lon, lat] (on Google Maps: right-click the place, the first line is 'lat, lon') " +
                 "and for each amenity / destination; never guess coordinates. Plots: corners [lon,lat] or a rectangle in metres from the site. " +
                 "Renders an MP4 into the project's output folder (takes a few minutes). Free: no points.")]
    public async Task<string> GeoVideo(
        [Description(ProjectParam)] string project,
        [Description("Site longitude (e.g. 106.765).")] double siteLon,
        [Description("Site latitude (e.g. 10.817).")] double siteLat,
        [Description("Name on the site pin and end card, e.g. 'DỰ ÁN THE GLOBAL CITY'.")] string siteName,
        IProgress<ProgressNotificationValue> progress,
        CancellationToken ct,
        [Description("Place line, e.g. 'Phước Long · TP. Thủ Đức'.")] string? place = null,
        [Description("Big intro title; use \\n for a line break. Default 'VỊ TRÍ\\nĐẮC ĐỊA'.")] string? title = null,
        [Description("Small line above the title. Default 'VỊ TRÍ DỰ ÁN'.")] string? kicker = null,
        [Description("Land plots to draw (at most 6).")] GeoPlotInput[]? plots = null,
        [Description("Amenities around the site (at most 10); distances are measured from the site.")] GeoPoiInput[]? amenities = null,
        [Description("Places to drive to from the site (at most 3); the first is the main one.")] GeoDestinationInput[]? destinations = null,
        [Description("Hotline / call to action on the end card, e.g. 'Hotline: 0909 123 456'. Omit for none.")] string? hotline = null,
        [Description("Note shown while plots are on screen, e.g. 'Số liệu quy hoạch tham khảo'.")] string? plotNote = null,
        [Description("9:16 (default), 16:9 or 1:1.")] string? aspect = null,
        [Description("Brand accent colour, hex. Default TodoX gold.")] string? accentColor = null,
        [Description("The TodoX skill guiding this video, as stated at the end of that skill.")] string? skill = null,
        [Description("That skill's version number.")] int? skillVersion = null)
    {
        if (string.IsNullOrWhiteSpace(project))
        {
            return NeedProject;
        }

        await using var session = await SessionLogger.StartAsync(_server, _paths, "geo_video", ct);
        if (await ReadyProblemAsync(session, ct) is { } problem)
        {
            return problem;
        }

        var site = new[] { siteLon, siteLat };
        if (GeoProblem(site, plots, amenities, destinations) is { } wrong)
        {
            return wrong;
        }

        try
        {
            var folder = _projects.Find(project, create: true).Folder ?? throw new InvalidOperationException(NeedProject);
            var (width, height, ratio) = (aspect?.Trim()) switch
            {
                "16:9" => (1920, 1080, "16:9"),
                "1:1" => (1080, 1080, "1:1"),
                _ => (1080, 1920, "9:16")
            };
            var accent = HexOr(accentColor, "#FFC83D");

            // Driving routes from TodoX's router; a straight line ("đường chim bay") when it cannot answer.
            var routes = new List<object>();
            var rows = new List<string[]>();
            var fallback = 0;
            foreach (var (d, i) in (destinations ?? []).Take(3).Select((d, i) => (d, i)))
            {
                Report(progress, 2 + i * 2, $"Tìm đường tới {d.Name}");
                var r = await _server.RouteAsync(siteLon, siteLat, d.Lon, d.Lat, ct);
                double[][] coords;
                string distance, duration;
                if (r.Ok && r.Value.TryGetProperty("coords", out var c) && c.GetArrayLength() >= 2)
                {
                    coords = c.EnumerateArray().Select(p => new[] { p[0].GetDouble(), p[1].GetDouble() }).ToArray();
                    distance = Km(r.Value.GetProperty("distanceMeters").GetDouble());
                    duration = Minutes(r.Value.GetProperty("seconds").GetDouble());
                }
                else
                {
                    fallback++;
                    coords = [site, [d.Lon, d.Lat]];
                    distance = Km(Metres(site, [d.Lon, d.Lat])) + " (chim bay)";
                    duration = "";
                }

                routes.Add(new { name = d.Name.Trim(), coords, distance, duration, color = RouteColors[i % RouteColors.Length] });
                rows.Add([d.Name.Trim(), string.IsNullOrEmpty(duration) ? distance : $"{distance} · {duration}"]);
            }

            var pois = (amenities ?? []).Take(10).Select(a => new
            {
                kind = PoiKinds.Contains(a.Kind?.Trim().ToLowerInvariant()) ? a.Kind!.Trim().ToLowerInvariant() : "star",
                name = a.Name.Trim(),
                position = new[] { a.Lon, a.Lat },
                distance = Distance(Metres(site, [a.Lon, a.Lat]))
            }).ToList();
            rows.InsertRange(0, pois.OrderBy(p => Metres(site, p.position)).Take(3).Select(p => new[] { p.name, p.distance }));

            var props = new
            {
                width, height, fps = 30,
                styleUrl = _server.ServerUrl + "/maps/styles/todox-dark.json",
                accent,
                kicker = Or(kicker, "VỊ TRÍ DỰ ÁN"),
                title = Or(title, "VỊ TRÍ\nĐẮC ĐỊA").Replace("\\n", "\n"),
                place = place?.Trim() ?? "",
                site = new { name = siteName.Trim(), position = site },
                plots = (plots ?? []).Take(6).Select((p, i) => new
                {
                    id = p.Id.Trim(),
                    name = p.Name.Trim(),
                    area = p.Area?.Trim() ?? "",
                    direction = p.Direction?.Trim() ?? "",
                    color = HexOr(p.Color, PlotColors[i % PlotColors.Length]),
                    ring = PlotRing(site, p)
                }).ToList(),
                pois,
                routes,
                headings = new { plots = "QUY HOẠCH\nPHÂN LÔ", pois = "TIỆN ÍCH\nXUNG QUANH", routes = "KẾT NỐI\nGIAO THÔNG" },
                plotNote = plotNote?.Trim() ?? "",
                endCard = new { title = siteName.Trim(), subtitle = place?.Trim() ?? "", rows = rows.Take(5).ToList(), hotline = hotline?.Trim() ?? "" },
                attribution = "Bản đồ TodoX · © OpenStreetMap contributors"
            };

            var seconds = GeoSeconds(props.plots.Count, pois.Count, routes.Count);
            var outPath = Path.Combine(folder.Path, Projects.ProjectFolders.OutputDir, $"todox-geo-{DateTime.Now:yyyyMMdd-HHmmss}-{ratio.Replace(':', 'x')}.mp4");
            session.Info("geo_video", null, new { project = folder.Name, plots = props.plots.Count, pois = pois.Count, routes = routes.Count, fallback, ratio });
            var last = -1.0;
            var result = await _renderer.RenderCompositionAsync("GeoLayer", props, seconds, outPath, new Progress<RenderProgress>(p =>
            {
                var pct = 8 + p.Percent * 0.92;
                if (pct - last >= 2 || p.Percent >= 100)
                {
                    last = pct;
                    Report(progress, pct, p.Stage);
                }
            }), ct);

            var tag = SkillTag(skill, skillVersion);
            WriteVideoInfo(result.OutputPath, tag, "Geo layer", ratio, result.Seconds);
            await session.EndAsync("ok", new { geo = true, skill = tag?.Skill, skillVersion = tag?.Version }, ct);
            var listed = false;
            if (_sync is not null)
            {
                try
                {
                    listed = (await _sync.SyncInfoAsync(ct)).Error is null;
                }
                catch (Exception ex) when (ex is IOException or HttpRequestException or TaskCanceledException)
                {
                }
            }

            return JsonSerializer.Serialize(new
            {
                output = result.OutputPath,
                project = folder.Name,
                seconds = Math.Round(result.Seconds, 1),
                routes = fallback == 0 ? null : "Dịch vụ tìm đường TodoX chưa trả lời nên tuyến đường được vẽ theo đường chim bay; khoảng cách ghi '(chim bay)'.",
                dashboard = listed
                    ? "Video đã hiện trong Jobs của tôi (chưa đồng bộ) — nhắc người dùng bấm Đồng bộ trong TodoX EditVideo để đưa file lên."
                    : "Chưa báo được lên Dashboard; TodoX EditVideo sẽ báo lại lần sau."
            }, StatusTools.Json);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or ArgumentException)
        {
            session.Error("geo_video_failed", ex.Message);
            return ex.Message;
        }
    }

    internal static string? GeoProblem(double[] site, GeoPlotInput[]? plots, GeoPoiInput[]? pois, GeoDestinationInput[]? destinations)
    {
        static bool InVietnam(double lon, double lat) => lon is >= 102 and <= 118 && lat is >= 7.5 and <= 23.6;
        if (!InVietnam(site[0], site[1]))
        {
            return "Toạ độ vị trí dự án không nằm trong Việt Nam. Cần siteLon (kinh độ, ~102–110) và siteLat (vĩ độ, ~8–23); " +
                   "trên Google Maps số đầu là vĩ độ, số sau là kinh độ.";
        }

        foreach (var p in pois ?? [])
        {
            if (!InVietnam(p.Lon, p.Lat) || Metres(site, [p.Lon, p.Lat]) > 30000)
            {
                return $"Tiện ích \"{p.Name}\" có toạ độ sai hoặc cách dự án hơn 30 km. Kiểm tra lại kinh độ/vĩ độ.";
            }
        }

        foreach (var d in destinations ?? [])
        {
            if (!InVietnam(d.Lon, d.Lat))
            {
                return $"Điểm đến \"{d.Name}\" không nằm trong Việt Nam. Kiểm tra lại kinh độ/vĩ độ.";
            }
        }

        foreach (var p in plots ?? [])
        {
            var corners = p.Corners?.Length >= 3;
            var rect = p.WidthMeters > 0 && p.DepthMeters > 0;
            if (!corners && !rect)
            {
                return $"Lô {p.Id}: cần Corners (ít nhất 3 điểm [kinh độ, vĩ độ]) hoặc WidthMeters + DepthMeters.";
            }

            if (corners && p.Corners!.Any(c => c.Length < 2 || Metres(site, c) > 5000))
            {
                return $"Lô {p.Id}: có góc cách dự án hơn 5 km — kiểm tra thứ tự [kinh độ, vĩ độ].";
            }
        }

        return null;
    }

    /// <summary>Corners of a plot as [lon, lat]; a rectangle is laid out in metres around the site.</summary>
    internal static double[][] PlotRing(double[] site, GeoPlotInput p)
    {
        if (p.Corners is { Length: >= 3 } corners)
        {
            return corners.Select(c => new[] { c[0], c[1] }).ToArray();
        }

        double w = p.WidthMeters!.Value / 2, h = p.DepthMeters!.Value / 2, e0 = p.EastMeters ?? 0, n0 = p.NorthMeters ?? 0;
        var a = (p.RotationDegrees ?? 0) * Math.PI / 180;
        return new[] { (-w, -h), (w, -h), (w, h), (-w, h) }.Select(((double x, double y) c) =>
        {
            var e = e0 + c.x * Math.Cos(a) - c.y * Math.Sin(a);
            var n = n0 + c.x * Math.Sin(a) + c.y * Math.Cos(a);
            return new[] { site[0] + e / (111320 * Math.Cos(site[1] * Math.PI / 180)), site[1] + n / 110540 };
        }).ToArray();
    }

    internal static double Metres(double[] a, double[] b)
    {
        var r = Math.PI / 180;
        var x = (b[0] - a[0]) * r * Math.Cos((a[1] + b[1]) / 2 * r);
        var y = (b[1] - a[1]) * r;
        return Math.Sqrt(x * x + y * y) * 6371e3;
    }

    internal static string Distance(double m) => m < 1000 ? $"{Math.Round(m / 10) * 10:0} m" : Km(m);

    internal static string Km(double m) => (m / 1000).ToString(m < 10000 ? "0.0" : "0", Vi) + " km";

    /// <summary>GraphHopper's car times assume free-flowing roads; Vietnamese city traffic is slower, so a range is shown.</summary>
    internal static string Minutes(double seconds)
    {
        var low = Math.Max(1, (int)Math.Round(seconds * 1.2 / 60));
        var high = Math.Max(low + 1, (int)Math.Round(seconds * 1.7 / 60));
        return $"≈ {low}–{high} phút";
    }

    /// <summary>Length of the GeoLayer video in seconds (same scene plan as the composition).</summary>
    internal static double GeoSeconds(int plots, int pois, int routes) =>
        (230 + (plots > 0 ? 150 + plots * 45 : 0) + (pois > 0 ? 150 + pois * 14 : 0) + (routes > 0 ? 230 : 0) + 180) / 30.0;

    private static string HexOr(string? value, string fallback) =>
        value?.Trim() is { } v && System.Text.RegularExpressions.Regex.IsMatch(v, "^#[0-9a-fA-F]{6}$") ? v.ToUpperInvariant() : fallback;

    private static string Or(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
