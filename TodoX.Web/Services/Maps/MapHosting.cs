using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

namespace TodoX.Web.Services.Maps;

/// <summary>
/// Self-hosted map data for EditVideo's geo layer (OpenStreetMap / Protomaps): the Vietnam map archive
/// (vietnam.pmtiles, read by the map renderer with HTTP range requests), label fonts and map sprites.
/// The files live outside the deployed site so a deploy never touches them: by default the folder "maps"
/// next to the site folder (e.g. H:\WEBDEPLOYED\todoX\maps beside ...\saas), or Maps:Root in appsettings.
/// Served read-only at /maps/... with CORS open (the video renderer runs on the customer's computer).
/// </summary>
public static class MapHosting
{
    public const string RequestPath = "/maps";

    internal static string ResolveRoot(string? configured, string contentRoot) =>
        Path.GetFullPath(string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(contentRoot, "..", "maps")
            : Path.IsPathRooted(configured) ? configured : Path.Combine(contentRoot, configured));

    internal static FileExtensionContentTypeProvider ContentTypes()
    {
        var types = new FileExtensionContentTypeProvider(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".pmtiles"] = "application/vnd.pmtiles",
            [".pbf"] = "application/x-protobuf",
            [".json"] = "application/json",
            [".png"] = "image/png",
            [".txt"] = "text/plain; charset=utf-8"
        });
        return types;
    }

    public static void UseTodoXMaps(this WebApplication app)
    {
        var root = ResolveRoot(app.Configuration["Maps:Root"], app.Environment.ContentRootPath);
        if (!Directory.Exists(root))
        {
            app.Logger.LogInformation("Map data folder {Root} not found; /maps is not served.", root);
            return;
        }

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(root),
            RequestPath = RequestPath,
            ContentTypeProvider = ContentTypes(),
            ServeUnknownFileTypes = false,
            OnPrepareResponse = ctx =>
            {
                var headers = ctx.Context.Response.Headers;
                headers["Access-Control-Allow-Origin"] = "*";
                headers["Access-Control-Expose-Headers"] = "Content-Range, Content-Length, ETag, Accept-Ranges";
                headers["Cache-Control"] = "public, max-age=86400";
            }
        });
        app.Logger.LogInformation("Serving map data from {Root} at {Path}.", root, RequestPath);
    }
}
