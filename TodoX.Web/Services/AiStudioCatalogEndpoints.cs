using TodoX.Web.Models;

namespace TodoX.Web.Services;

public static class AiStudioCatalogEndpoints
{
    public static void MapAiStudioCatalogEndpoints(this WebApplication app)
    {
        var admin = app.MapGroup("/api/admin/ai-studio").RequireTodoXAdmin();
        admin.MapGet("/voices", ListAdminVoicesAsync);
        admin.MapGet("/voices/{id:guid}", GetAdminVoiceAsync);
        admin.MapPost("/voices", SaveVoiceAsync).DisableAntiforgery();
        admin.MapPut("/voices/{id:guid}", UpdateVoiceAsync).DisableAntiforgery();
        admin.MapDelete("/voices/{id:guid}", DisableVoiceAsync).DisableAntiforgery();
        admin.MapPost("/voices/{id:guid}/preview", UploadVoicePreviewAsync).DisableAntiforgery();

        admin.MapGet("/music", ListAdminMusicAsync);
        admin.MapGet("/music/{id:guid}", GetAdminMusicAsync);
        admin.MapPost("/music", SaveMusicAsync).DisableAntiforgery();
        admin.MapPut("/music/{id:guid}", UpdateMusicAsync).DisableAntiforgery();
        admin.MapDelete("/music/{id:guid}", DisableMusicAsync).DisableAntiforgery();
        admin.MapPost("/music/{id:guid}/file", UploadMusicFileAsync).DisableAntiforgery();
        admin.MapPost("/music/{id:guid}/import-url", ImportMusicFromUrlAsync).DisableAntiforgery();

        var runtime = app.MapGroup("/api/ai-studio");
        runtime.MapGet("/voices", (IAiStudioCatalogService service, CancellationToken ct) => service.ListVoicesAsync(activeOnly: true, ct: ct));
        runtime.MapGet("/voices/{code}", async (string code, IAiStudioCatalogService service, CancellationToken ct)
            => await service.GetVoiceByCodeAsync(code, activeOnly: true, ct) is { } voice
                ? Results.Json(new
                {
                    voice.Code,
                    voice.Name,
                    voice.ProviderCode,
                    voice.Gender,
                    voice.LanguageCode,
                    voice.Region,
                    voice.Description,
                    voice.PreviewFileUrl,
                    voice.DefaultRate,
                    voice.MinRate,
                    voice.MaxRate,
                    voice.IsDefault,
                    voice.SortOrder
                })
                : Results.NotFound());
        runtime.MapGet("/music", (IAiStudioCatalogService service, CancellationToken ct) => service.ListMusicAsync(activeOnly: true, ct: ct));
        runtime.MapGet("/music/{code}", async (string code, IAiStudioCatalogService service, CancellationToken ct)
            => await service.GetMusicByCodeAsync(code, activeOnly: true, ct) is { } music
                ? Results.Json(new
                {
                    music.Code,
                    music.Name,
                    music.Description,
                    music.Category,
                    music.FileUrl,
                    music.DurationSeconds,
                    music.MimeType,
                    music.DefaultVolume,
                    music.LoopAllowed,
                    music.IsDefault,
                    music.SortOrder
                })
                : Results.NotFound());
    }

    private static Task<IReadOnlyList<AiStudioVoiceDto>> ListAdminVoicesAsync(
        string? search,
        string? providerCode,
        string? gender,
        string? languageCode,
        bool? isActive,
        AuthStateService auth,
        IAiStudioCatalogService service,
        CancellationToken ct)
        => ExecuteAdminAsync(auth, () => service.ListVoicesAsync(new AiStudioCatalogFilter
        {
            Search = search,
            ProviderCode = providerCode,
            Gender = gender,
            LanguageCode = languageCode,
            IsActive = isActive
        }, ct: ct));

    private static async Task<IResult> GetAdminVoiceAsync(Guid id, AuthStateService auth, IAiStudioCatalogService service, CancellationToken ct)
        => await ExecuteAdminResultAsync(auth, async () => await service.GetVoiceAsync(id, ct) is { } voice ? Results.Json(voice) : Results.NotFound());

    private static Task<AiStudioVoiceDto> SaveVoiceAsync(AiStudioVoiceDto voice, AuthStateService auth, IAiStudioCatalogService service, CancellationToken ct)
        => ExecuteAdminAsync(auth, () => service.SaveVoiceAsync(voice, auth.CurrentUser!, ct));

    private static Task<AiStudioVoiceDto> UpdateVoiceAsync(Guid id, AiStudioVoiceDto voice, AuthStateService auth, IAiStudioCatalogService service, CancellationToken ct)
    {
        voice.Id = id;
        return ExecuteAdminAsync(auth, () => service.SaveVoiceAsync(voice, auth.CurrentUser!, ct));
    }

    private static Task DisableVoiceAsync(Guid id, AuthStateService auth, IAiStudioCatalogService service, CancellationToken ct)
        => ExecuteAdminAsync(auth, () => service.DisableVoiceAsync(id, auth.CurrentUser!, ct));

    private static Task<AiStudioVoiceDto> UploadVoicePreviewAsync(Guid id, HttpRequest request, AuthStateService auth, IAiStudioCatalogService service, CancellationToken ct)
        => ExecuteAdminFileAsync(request, auth, (file, bytes) => service.UploadVoicePreviewAsync(id, bytes, file.FileName, file.ContentType, auth.CurrentUser!, ct), ct);

    private static Task<IReadOnlyList<AiStudioMusicDto>> ListAdminMusicAsync(
        string? search,
        string? category,
        bool? isActive,
        AuthStateService auth,
        IAiStudioCatalogService service,
        CancellationToken ct)
        => ExecuteAdminAsync(auth, () => service.ListMusicAsync(new AiStudioCatalogFilter
        {
            Search = search,
            Category = category,
            IsActive = isActive
        }, ct: ct));

    private static async Task<IResult> GetAdminMusicAsync(Guid id, AuthStateService auth, IAiStudioCatalogService service, CancellationToken ct)
        => await ExecuteAdminResultAsync(auth, async () => await service.GetMusicAsync(id, ct) is { } music ? Results.Json(music) : Results.NotFound());

    private static Task<AiStudioMusicDto> SaveMusicAsync(AiStudioMusicDto music, AuthStateService auth, IAiStudioCatalogService service, CancellationToken ct)
        => ExecuteAdminAsync(auth, () => service.SaveMusicAsync(music, auth.CurrentUser!, ct));

    private static Task<AiStudioMusicDto> UpdateMusicAsync(Guid id, AiStudioMusicDto music, AuthStateService auth, IAiStudioCatalogService service, CancellationToken ct)
    {
        music.Id = id;
        return ExecuteAdminAsync(auth, () => service.SaveMusicAsync(music, auth.CurrentUser!, ct));
    }

    private static Task DisableMusicAsync(Guid id, AuthStateService auth, IAiStudioCatalogService service, CancellationToken ct)
        => ExecuteAdminAsync(auth, () => service.DisableMusicAsync(id, auth.CurrentUser!, ct));

    private static Task<AiStudioMusicDto> UploadMusicFileAsync(Guid id, HttpRequest request, AuthStateService auth, IAiStudioCatalogService service, CancellationToken ct)
        => ExecuteAdminFileAsync(request, auth, (file, bytes) => service.UploadMusicFileAsync(id, bytes, file.FileName, file.ContentType, auth.CurrentUser!, ct), ct);

    private static Task<AiStudioMusicDto> ImportMusicFromUrlAsync(Guid id, AiStudioMusicImportUrlRequest request, AuthStateService auth, IAiStudioCatalogService service, CancellationToken ct)
        => ExecuteAdminAsync(auth, () => service.ImportMusicFromUrlAsync(id, request.Url, auth.CurrentUser!, ct));

    private static async Task<T> ExecuteAdminAsync<T>(AuthStateService auth, Func<Task<T>> action)
    {
        EnsureAdmin(auth);
        return await action();
    }

    private static async Task ExecuteAdminAsync(AuthStateService auth, Func<Task> action)
    {
        EnsureAdmin(auth);
        await action();
    }

    private static async Task<IResult> ExecuteAdminResultAsync(AuthStateService auth, Func<Task<IResult>> action)
    {
        EnsureAdmin(auth);
        return await action();
    }

    private static async Task<T> ExecuteAdminFileAsync<T>(HttpRequest request, AuthStateService auth, Func<IFormFile, byte[], Task<T>> action, CancellationToken ct)
    {
        EnsureAdmin(auth);
        if (!request.HasFormContentType) throw new InvalidOperationException("AI_STUDIO_INVALID_UPLOAD");
        var form = await request.ReadFormAsync(ct);
        var file = form.Files.FirstOrDefault();
        if (file is null || file.Length == 0) throw new InvalidOperationException("AI_STUDIO_INVALID_UPLOAD");
        if (file.Length > AiStudioCatalogRules.MaxAudioBytes) throw new InvalidOperationException("AUDIO_FILE_TOO_LARGE");
        await using var stream = file.OpenReadStream();
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);
        return await action(file, ms.ToArray());
    }

    private static void EnsureAdmin(AuthStateService auth)
    {
        var user = auth.CurrentUser;
        if (user?.IsAuthenticated != true || !(user.IsRoot || user.Role is TodoXUserRole.Admin or TodoXUserRole.SystemOperator))
        {
            throw new InvalidOperationException("AI_STUDIO_ADMIN_REQUIRED");
        }
    }
}
