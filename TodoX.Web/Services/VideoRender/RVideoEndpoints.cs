using System.Text;
using TodoX.Web.Models;

namespace TodoX.Web.Services.VideoRender;

public static class RVideoEndpoints
{
    public static void MapRVideoEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/rvideo");
        group.MapGet("/projects/{projectId:long}/settings", async (long projectId, RVideoJobSettingsRepository repository, AuthStateService auth, CancellationToken ct)
            => await RequireUserAsync(auth, async () => await repository.GetAsync(projectId, ct) is { } settings ? Results.Json(settings) : Results.NotFound()));
        group.MapPut("/projects/{projectId:long}/settings", async (long projectId, RVideoJobSettingsRequest request, RVideoJobSettingsRepository repository, AuthStateService auth, CancellationToken ct)
            => await RequireUserAsync(auth, async () => Results.Json(await repository.SaveAsync(projectId, request, ct))));
        group.MapGet("/jobs/{jobId:guid}/settings", async (Guid jobId, IRVideoJobService jobs, AuthStateService auth, CancellationToken ct)
            => await RequireUserAsync(auth, async () => auth.CurrentUser is { IsCustomer: true } user && await jobs.GetByJobIdAsync(jobId, user, ct) is { Settings: { } settings } ? Results.Json(settings) : Results.NotFound()));
        group.MapPut("/jobs/{jobId:guid}/settings", async (Guid jobId, RVideoJobSettingsRequest request, IRVideoJobService jobs, RVideoJobSettingsRepository repository, AuthStateService auth, CancellationToken ct)
            => await RequireUserAsync(auth, async () => auth.CurrentUser is { IsCustomer: true } user && await jobs.ResolveProjectIdAsync(jobId, user, ct) is long projectId ? Results.Json(await repository.SaveAsync(projectId, request, ct)) : Results.NotFound()));
        group.MapPost("/scenes/import", async (HttpRequest request, RVideoSceneJsonService service, AuthStateService auth, CancellationToken ct) =>
        {
            if (auth.CurrentUser?.IsAuthenticated != true) return Results.Unauthorized();
            using var reader = new StreamReader(request.Body, Encoding.UTF8);
            var json = await reader.ReadToEndAsync(ct);
            return Results.Json(service.Import(json));
        }).DisableAntiforgery();
        group.MapPost("/scenes/export", async (RVideoSceneExportRequest request, RVideoSceneJsonService service, AuthStateService auth) =>
            auth.CurrentUser?.IsAuthenticated == true
                ? Results.Text(service.Export(request.VideoTitle, request.Scenes), "application/json", Encoding.UTF8)
                : Results.Unauthorized());
    }

    private static async Task<IResult> RequireUserAsync(AuthStateService auth, Func<Task<IResult>> action)
        => auth.CurrentUser?.IsAuthenticated == true ? await action() : Results.Unauthorized();
}

public sealed class RVideoSceneExportRequest
{
    public string? VideoTitle { get; set; }
    public IReadOnlyList<RVideoSceneEditorItem> Scenes { get; set; } = Array.Empty<RVideoSceneEditorItem>();
}
