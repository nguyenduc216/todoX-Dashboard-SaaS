using TodoX.Web.Components;
using TodoX.Web.Data;
using TodoX.Web.Models;
using TodoX.Web.Services;
using TodoX.Web.Services.Render;
using TodoX.Web.Services.Reup;
using TodoX.Web.Services.AiCharacters;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// TodoX UI services.
builder.Services.AddMudServices();
builder.Services.AddCors(options =>
{
    options.AddPolicy("ExtensionApi", policy => policy
        .AllowAnyOrigin()
        .AllowAnyHeader()
        .AllowAnyMethod());
});

// Data access to todo_saas (Foundation V2) via Npgsql + Dapper.
builder.Services.AddSingleton<TodoXConnectionFactory>();
builder.Services.AddSingleton<TodoXAutomationConnectionFactory>();
builder.Services.AddSingleton<TenantContext>();
builder.Services.AddSingleton<PasswordHasher>();
builder.Services.AddScoped<AccountRepository>();
builder.Services.AddScoped<CustomerRepository>();
builder.Services.AddScoped<PermissionRepository>();
builder.Services.AddScoped<NavigationMenuRepository>();
builder.Services.AddScoped<AuditRepository>();
builder.Services.AddScoped<BillingRepository>();
builder.Services.AddScoped<CatalogRepository>();
builder.Services.AddScoped<CatalogAdminRepository>();
builder.Services.AddScoped<MrTodoXAvatarService>();
builder.Services.AddScoped<SystemImageStorage>();
builder.Services.AddScoped<ServiceThumbnailRenderService>();
builder.Services.AddScoped<TodoX.Web.Services.Images.ServiceImageLayoutPlanner>();
builder.Services.AddScoped<TodoX.Web.Services.Images.ServiceImagePromptCompiler>();
builder.Services.AddScoped<TodoX.Web.Services.Images.ServiceImageQcService>();
builder.Services.AddScoped<SocialPageRepository>();
builder.Services.AddScoped<ReferenceVideoRepository>();
builder.Services.AddScoped<ExtensionTokenService>();
builder.Services.AddScoped<ExtensionPackageService>();
builder.Services.AddScoped<AutomationSettingsRepository>();
builder.Services.AddHttpClient<FacebookGraphService>();
builder.Services.AddScoped<FacebookSignedRequestService>();
builder.Services.AddScoped<FacebookOAuthService>();

// Sprint 2F: media, image render (Vertex), avatar + chibi.
builder.Services.AddScoped<TodoX.Web.Services.Media.IMediaFileService, TodoX.Web.Services.Media.MediaFileService>();
builder.Services.AddScoped<TodoX.Web.Services.Settings.SettingsApiRepository>();
builder.Services.AddScoped<TodoX.Web.Services.Settings.PromptTemplateRepository>();
builder.Services.AddScoped<TodoX.Web.Services.Settings.IPromptTemplateService, TodoX.Web.Services.Settings.PromptTemplateService>();
builder.Services.AddHttpClient<TodoX.Web.Services.ImageRender.VertexClient>();
builder.Services.AddHttpClient<TodoX.Web.Services.ImageRender.IBrandAssetCompositeService, TodoX.Web.Services.ImageRender.BrandAssetCompositeService>();
builder.Services.AddScoped<TodoX.Web.Services.ImageRender.IImageRenderService, TodoX.Web.Services.ImageRender.VertexImageRenderService>();
builder.Services.AddScoped<TodoX.Web.Services.ImageRender.MarketingImageRenderLogRepository>();
builder.Services.AddHttpClient<TodoX.Web.Services.ImageRender.IMarketingBriefAnalyzer, TodoX.Web.Services.ImageRender.GeminiMarketingBriefAnalyzer>();
builder.Services.AddScoped<TodoX.Web.Services.ImageRender.MarketingImageRenderService>();
builder.Services.AddScoped<TodoX.Web.Services.Profile.IAvatarService, TodoX.Web.Services.Profile.AvatarService>();
builder.Services.AddHttpClient<TodoX.Web.Services.Profile.GeminiPromptService>();
builder.Services.AddScoped<TodoX.Web.Services.Profile.AvatarRenderActivityLogService>();
builder.Services.AddScoped<TodoX.Web.Services.Profile.IImageAICreativeRenderService, TodoX.Web.Services.Profile.ImageAICreativeRenderService>();
builder.Services.AddScoped<TodoX.Web.Services.Profile.IChibiAvatarService, TodoX.Web.Services.Profile.ChibiAvatarService>();
builder.Services.AddScoped<TodoX.Web.Services.AvatarTemplates.IAvatarTemplateService, TodoX.Web.Services.AvatarTemplates.AvatarTemplateService>();
builder.Services.AddScoped<ITodoXImageProviderService, TodoXImageProviderService>();
builder.Services.AddHttpClient<IOpenRouterImageService, OpenRouterImageService>();
builder.Services.AddScoped<IAiImageProviderFactory, AiImageProviderFactory>();
builder.Services.AddScoped<CharacterPromptBuilder>();
builder.Services.AddScoped<AiCharacterRepository>();
builder.Services.AddScoped<IAiCharacterService, AiCharacterService>();
builder.Services.AddScoped<TodoX.Web.Services.AiProviders.AiProviderRepository>();
builder.Services.AddScoped<TodoX.Web.Services.AiProviders.IAiProviderService, TodoX.Web.Services.AiProviders.AiProviderService>();
builder.Services.AddScoped<TodoX.Web.Services.AiProviders.IAiImageRenderRouter, TodoX.Web.Services.AiProviders.AiImageRenderRouter>();
builder.Services.Configure<TodoX.Web.Services.VideoRender.VideoRenderOptions>(builder.Configuration.GetSection("VideoRender"));
builder.Services.AddScoped<TodoX.Web.Services.VideoRender.VideoRenderRepository>();
builder.Services.AddScoped<TodoX.Web.Services.VideoRender.VideoRenderMockHandler>();
builder.Services.AddScoped<TodoX.Web.Services.VideoRender.VideoRenderMergeHandler>();
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<AuthStateService>();
builder.Services.AddScoped<StartupSeedFixer>();
builder.Services.AddScoped<TokenSettingsService>();
builder.Services.AddScoped<WalletService>();
builder.Services.AddScoped<IRenderJobService, RenderJobService>();
builder.Services.AddScoped<IRenderJobDispatcher, RenderJobDispatcher>();
builder.Services.AddHostedService<RenderJobWorker>();
builder.Services.Configure<ReupCampaignOptions>(builder.Configuration.GetSection("ReupCampaign"));
builder.Services.AddHttpClient<TikwmVideoResolver>(client => client.Timeout = TimeSpan.FromSeconds(120));
builder.Services.AddHttpClient<FacebookPageTokenChecker>(client => client.Timeout = TimeSpan.FromSeconds(60));
builder.Services.AddHttpClient<FacebookPageVideoPublisher>(client => client.Timeout = TimeSpan.FromMinutes(10));
builder.Services.AddHttpClient<ReupVideoCacheService>(client => client.Timeout = TimeSpan.FromMinutes(10));
builder.Services.AddScoped<ReupCampaignRepository>();
builder.Services.AddScoped<ReupLogService>();
builder.Services.AddSingleton<ReupStorageService>();
builder.Services.AddSingleton<ReupTaskPageGate>();
builder.Services.AddHostedService<ReupCampaignWorker>();
var app = builder.Build();

// Load tenant and repair placeholder seed credentials (writes data only, never schema).
using (var scope = app.Services.CreateScope())
{
    var tenant = scope.ServiceProvider.GetRequiredService<TenantContext>();
    await tenant.EnsureLoadedAsync();
    var fixer = scope.ServiceProvider.GetRequiredService<StartupSeedFixer>();
    await fixer.RunAsync();

    // Sprint 2G: seed point pricing defaults and ensure every customer has a point wallet.
    var tokenSettings = scope.ServiceProvider.GetRequiredService<TokenSettingsService>();
    await tokenSettings.EnsureDefaultsAsync();
    var mrTodoX = scope.ServiceProvider.GetRequiredService<MrTodoXAvatarService>();
    await mrTodoX.EnsureDefaultsAsync();
    var avatarTemplates = scope.ServiceProvider.GetRequiredService<TodoX.Web.Services.AvatarTemplates.IAvatarTemplateService>();
    await avatarTemplates.EnsureSchemaAsync();
    var wallets = scope.ServiceProvider.GetRequiredService<WalletService>();
    await wallets.SeedCustomerWalletsAsync();
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseCors();

// Serve runtime-uploaded files (avatars, chibi, references) from wwwroot/uploads.
app.UseStaticFiles();

app.UseAntiforgery();

static IResult UnauthorizedJson(string message) => Results.Json(new
{
    success = false,
    message
}, statusCode: StatusCodes.Status401Unauthorized);

app.MapPost("/facebook/data-deletion", async (
    HttpRequest request,
    IConfiguration config,
    FacebookSignedRequestService signedRequestService,
    ILoggerFactory loggerFactory) =>
{
    var logger = loggerFactory.CreateLogger("FacebookDataDeletion");
    string? signedRequest = null;

    if (request.HasFormContentType)
    {
        var form = await request.ReadFormAsync();
        signedRequest = form["signed_request"].FirstOrDefault();
    }
    else
    {
        signedRequest = await new StreamReader(request.Body).ReadToEndAsync();
    }

    var appSecret = config["Facebook:AppSecret"];
    var payload = !string.IsNullOrWhiteSpace(signedRequest) && !string.IsNullOrWhiteSpace(appSecret)
        ? signedRequestService.ParseAndValidate(signedRequest, appSecret)
        : null;
    var confirmationCode = $"fbdel-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}";
    var publicBaseUrl = (config["TodoX:PublicBaseUrl"] ?? "https://dashboard.todox.vn").TrimEnd('/');
    var statusUrl = $"{publicBaseUrl}/data-deletion?code={Uri.EscapeDataString(confirmationCode)}";

    logger.LogWarning(
        "Facebook data deletion request received. ConfirmationCode={ConfirmationCode}, SignedRequestPresent={SignedRequestPresent}, SignedRequestValid={SignedRequestValid}",
        confirmationCode,
        !string.IsNullOrWhiteSpace(signedRequest),
        payload is not null);

    // TODO: Store deletion request in database/audit log and mark related Facebook tokens for deletion
    // after signed_request user_id extraction is wired to the final Meta production workflow.
    payload?.Dispose();

    return Results.Json(new
    {
        url = statusUrl,
        confirmation_code = confirmationCode
    });
}).DisableAntiforgery();

var extensionApi = app.MapGroup("/api/extension")
    .RequireCors("ExtensionApi");

extensionApi.MapGet("/me", async (
    HttpRequest request,
    ExtensionTokenService tokens,
    CancellationToken ct) =>
{
    var token = ExtensionTokenService.ReadToken(request);
    var validation = await tokens.ValidateAsync(token, ct);
    if (!validation.IsValid)
    {
        return UnauthorizedJson("Extension token không hợp lệ hoặc đã hết hạn.");
    }

    return Results.Json(new ExtensionMeResponse
    {
        CustomerId = validation.CustomerId,
        UserId = validation.UserId,
        CustomerName = validation.CustomerName,
        UserEmail = validation.UserEmail,
        IsActive = true
    });
});

extensionApi.MapPost("/reference-videos", async (
    HttpRequest request,
    ReferenceVideoCreateRequest body,
    ExtensionTokenService tokens,
    ReferenceVideoRepository videos,
    CancellationToken ct) =>
{
    var token = ExtensionTokenService.ReadToken(request);
    var validation = await tokens.ValidateAsync(token, ct);
    if (!validation.IsValid)
    {
        return UnauthorizedJson("Extension token không hợp lệ hoặc đã hết hạn.");
    }

    if (string.IsNullOrWhiteSpace(body.SourceUrl))
    {
        return Results.Json(new
        {
            success = false,
            message = "Thiếu link video."
        }, statusCode: StatusCodes.Status400BadRequest);
    }

    var id = await videos.UpsertAsync(validation.CustomerId, validation.UserId, body, ct);
    return Results.Json(new
    {
        success = true,
        message = "Đã thêm link video vào TodoX.",
        data = new { id }
    });
}).DisableAntiforgery();

extensionApi.MapGet("/download", async (
    AuthStateService auth,
    ExtensionPackageService packages,
    CancellationToken ct) =>
{
    var user = auth.CurrentUser;
    if (user?.IsAuthenticated != true || user.CustomerId is null || !user.Can("extension.download"))
    {
        return UnauthorizedJson("Bạn cần đăng nhập và có quyền tải Chrome Extension.");
    }

    var package = await packages.CreateForUserAsync(user.CustomerId.Value, user.UserId, ct);
    return Results.File(package.Bytes, package.ContentType, package.FileName);
});

static bool IsHttpAdmin(AuthStateService auth)
    => auth.CurrentUser?.IsRoot == true
       || auth.CurrentUser?.Role is TodoXUserRole.Admin or TodoXUserRole.SystemOperator;

var adminAvatarApi = app.MapGroup("/api/admin/avatar-templates");

adminAvatarApi.MapGet("/", async (
    AuthStateService auth,
    TodoX.Web.Services.AvatarTemplates.IAvatarTemplateService templates,
    CancellationToken ct) =>
{
    if (!IsHttpAdmin(auth)) return UnauthorizedJson("Bạn cần quyền quản trị để xem avatar mẫu.");
    return Results.Json(await templates.ListAsync(publicOnly: false, ct));
});

adminAvatarApi.MapGet("/{id:guid}", async (
    Guid id,
    AuthStateService auth,
    TodoX.Web.Services.AvatarTemplates.IAvatarTemplateService templates,
    CancellationToken ct) =>
{
    if (!IsHttpAdmin(auth)) return UnauthorizedJson("Bạn cần quyền quản trị để xem avatar mẫu.");
    var item = await templates.GetAsync(id, ct);
    return item is null ? Results.NotFound() : Results.Json(item);
});

adminAvatarApi.MapPost("/render-preview", async (
    TodoX.Web.Services.AvatarTemplates.AvatarTemplateRenderPreviewRequest body,
    AuthStateService auth,
    TodoX.Web.Services.AvatarTemplates.IAvatarTemplateService templates,
    CancellationToken ct) =>
{
    if (!IsHttpAdmin(auth) || auth.CurrentUser is null) return UnauthorizedJson("Bạn cần quyền quản trị để render avatar mẫu.");
    var result = await templates.RenderPreviewAsync(body.Template, auth.CurrentUser.UserId, auth.CurrentUser.CustomerId, ct);
    return Results.Json(result);
}).DisableAntiforgery();

adminAvatarApi.MapPost("/", async (
    TodoX.Web.Services.AvatarTemplates.AvatarTemplateEditModel body,
    AuthStateService auth,
    TodoX.Web.Services.AvatarTemplates.IAvatarTemplateService templates,
    CancellationToken ct) =>
{
    if (!IsHttpAdmin(auth)) return UnauthorizedJson("Bạn cần quyền quản trị để lưu avatar mẫu.");
    var saved = await templates.SaveAsync(body, auth.CurrentUser?.UserId, ct);
    return Results.Json(saved);
}).DisableAntiforgery();

adminAvatarApi.MapPut("/{id:guid}", async (
    Guid id,
    TodoX.Web.Services.AvatarTemplates.AvatarTemplateEditModel body,
    AuthStateService auth,
    TodoX.Web.Services.AvatarTemplates.IAvatarTemplateService templates,
    CancellationToken ct) =>
{
    if (!IsHttpAdmin(auth)) return UnauthorizedJson("Bạn cần quyền quản trị để cập nhật avatar mẫu.");
    body.Id = id;
    var saved = await templates.SaveAsync(body, auth.CurrentUser?.UserId, ct);
    return Results.Json(saved);
}).DisableAntiforgery();

adminAvatarApi.MapDelete("/{id:guid}", async (
    Guid id,
    AuthStateService auth,
    TodoX.Web.Services.AvatarTemplates.IAvatarTemplateService templates,
    CancellationToken ct) =>
{
    if (!IsHttpAdmin(auth)) return UnauthorizedJson("Bạn cần quyền quản trị để xóa avatar mẫu.");
    await templates.DeleteAsync(id, auth.CurrentUser?.UserId, ct);
    return Results.Json(new { success = true });
});

adminAvatarApi.MapPost("/{id:guid}/toggle-public", async (
    Guid id,
    AuthStateService auth,
    TodoX.Web.Services.AvatarTemplates.IAvatarTemplateService templates,
    CancellationToken ct) =>
{
    if (!IsHttpAdmin(auth)) return UnauthorizedJson("Bạn cần quyền quản trị để đổi trạng thái avatar mẫu.");
    await templates.TogglePublicAsync(id, auth.CurrentUser?.UserId, ct);
    return Results.Json(new { success = true });
}).DisableAntiforgery();

static bool CanReup(AuthStateService auth, string action)
    => auth.CurrentUser?.Can($"reup_campaigns.{action}") == true;

static Guid RequireCustomer(AuthStateService auth)
    => auth.CurrentUser?.CustomerId
       ?? throw new InvalidOperationException("Tài khoản hiện tại chưa gắn customer_id.");

var reupApi = app.MapGroup("/api/reup");

reupApi.MapGet("/campaigns", async (
    AuthStateService auth,
    ReupCampaignRepository repo,
    CancellationToken ct) =>
{
    if (!CanReup(auth, "view")) return UnauthorizedJson("Bạn chưa có quyền xem chiến dịch reup.");
    var customerId = RequireCustomer(auth);
    return Results.Json(await repo.ListCampaignsAsync(customerId, ct));
});

reupApi.MapPost("/campaigns", async (
    CreateReupCampaignRequest body,
    AuthStateService auth,
    ReupCampaignRepository repo,
    AuditRepository audit,
    CancellationToken ct) =>
{
    if (!CanReup(auth, "create")) return UnauthorizedJson("Bạn chưa có quyền tạo chiến dịch reup.");
    var customerId = RequireCustomer(auth);
    var id = await repo.CreateCampaignAsync(customerId, auth.CurrentUser!.UserId, body, ct);
    await audit.LogAsync(auth.CurrentUser, "reup_campaigns", "create", id.ToString(), message: body.Name, feature: "campaign");
    return Results.Json(new { success = true, id });
}).DisableAntiforgery();

reupApi.MapGet("/campaigns/{id:guid}", async (
    Guid id,
    AuthStateService auth,
    ReupCampaignRepository repo,
    CancellationToken ct) =>
{
    if (!CanReup(auth, "view")) return UnauthorizedJson("Bạn chưa có quyền xem chiến dịch reup.");
    var item = await repo.GetCampaignAsync(RequireCustomer(auth), id, ct);
    return item is null ? Results.NotFound() : Results.Json(item);
});

reupApi.MapPut("/campaigns/{id:guid}", async (
    Guid id,
    UpdateReupCampaignRequest body,
    AuthStateService auth,
    ReupCampaignRepository repo,
    CancellationToken ct) =>
{
    if (!CanReup(auth, "update")) return UnauthorizedJson("Bạn chưa có quyền sửa chiến dịch reup.");
    await repo.UpdateCampaignAsync(RequireCustomer(auth), id, body, ct);
    return Results.Json(new { success = true });
}).DisableAntiforgery();

reupApi.MapDelete("/campaigns/{id:guid}", async (
    Guid id,
    AuthStateService auth,
    ReupCampaignRepository repo,
    CancellationToken ct) =>
{
    if (!CanReup(auth, "delete")) return UnauthorizedJson("Bạn chưa có quyền xóa chiến dịch reup.");
    await repo.DeleteCampaignAsync(RequireCustomer(auth), id, ct);
    return Results.Json(new { success = true });
});

reupApi.MapPost("/campaigns/{id:guid}/run", async (
    Guid id,
    AuthStateService auth,
    ReupCampaignRepository repo,
    AuditRepository audit,
    CancellationToken ct) =>
{
    if (!CanReup(auth, "run")) return UnauthorizedJson("Bạn chưa có quyền chạy chiến dịch reup.");
    await repo.RunCampaignAsync(RequireCustomer(auth), id, ct);
    await audit.LogAsync(auth.CurrentUser, "reup_campaigns", "run", id.ToString(), feature: "campaign");
    return Results.Json(new { success = true });
}).DisableAntiforgery();

reupApi.MapPost("/campaigns/{id:guid}/stop", async (
    Guid id,
    AuthStateService auth,
    ReupCampaignRepository repo,
    AuditRepository audit,
    CancellationToken ct) =>
{
    if (!CanReup(auth, "stop")) return UnauthorizedJson("Bạn chưa có quyền dừng chiến dịch reup.");
    await repo.StopCampaignAsync(RequireCustomer(auth), id, ct);
    await audit.LogAsync(auth.CurrentUser, "reup_campaigns", "stop", id.ToString(), feature: "campaign", severity: "warning");
    return Results.Json(new { success = true });
}).DisableAntiforgery();

reupApi.MapGet("/campaigns/{id:guid}/tasks", async (
    Guid id,
    AuthStateService auth,
    ReupCampaignRepository repo,
    CancellationToken ct) =>
{
    if (!CanReup(auth, "view")) return UnauthorizedJson("Bạn chưa có quyền xem task reup.");
    return Results.Json(await repo.GetTasksAsync(RequireCustomer(auth), id, ct));
});

reupApi.MapGet("/campaigns/{id:guid}/logs", async (
    Guid id,
    AuthStateService auth,
    ReupCampaignRepository repo,
    CancellationToken ct) =>
{
    if (!CanReup(auth, "view")) return UnauthorizedJson("Bạn chưa có quyền xem log reup.");
    return Results.Json(await repo.GetLogsAsync(RequireCustomer(auth), id, ct));
});

reupApi.MapPost("/tasks/{taskId:guid}/retry", async (
    Guid taskId,
    AuthStateService auth,
    ReupCampaignRepository repo,
    AuditRepository audit,
    CancellationToken ct) =>
{
    if (!CanReup(auth, "retry")) return UnauthorizedJson("Bạn chưa có quyền retry task reup.");
    await repo.RetryTaskAsync(RequireCustomer(auth), taskId, ct);
    await audit.LogAsync(auth.CurrentUser, "reup_campaigns", "retry", taskId.ToString(), feature: "task", severity: "warning");
    return Results.Json(new { success = true });
}).DisableAntiforgery();

reupApi.MapPost("/duplicate-check", async (
    ReupDuplicateCheckRequest body,
    AuthStateService auth,
    ReupCampaignRepository repo,
    CancellationToken ct) =>
{
    if (!CanReup(auth, "view")) return UnauthorizedJson("Bạn chưa có quyền xem cảnh báo đăng lại.");
    return Results.Json(await repo.CheckDuplicatesAsync(RequireCustomer(auth), body.ReferenceVideoIds, body.SocialPageIds, ct));
}).DisableAntiforgery();

reupApi.MapGet("/reference-videos", async (
    AuthStateService auth,
    ReupCampaignRepository repo,
    CancellationToken ct) =>
{
    if (!CanReup(auth, "view")) return UnauthorizedJson("Bạn chưa có quyền xem video reup.");
    return Results.Json(await repo.ListTikTokVideosAsync(RequireCustomer(auth), ct));
});

reupApi.MapGet("/facebook-pages", async (
    AuthStateService auth,
    ReupCampaignRepository repo,
    CancellationToken ct) =>
{
    if (!CanReup(auth, "view")) return UnauthorizedJson("Bạn chưa có quyền xem Facebook Page.");
    return Results.Json(await repo.ListFacebookPagesAsync(RequireCustomer(auth), ct));
});

var publicAvatarApi = app.MapGroup("/api/public/avatar-builder");

publicAvatarApi.MapGet("/templates", async (
    TodoX.Web.Services.AvatarTemplates.IAvatarTemplateService templates,
    CancellationToken ct) => Results.Json(await templates.ListAsync(publicOnly: true, ct)));

publicAvatarApi.MapPost("/upload", async (
    HttpRequest request,
    TodoX.Web.Services.AvatarTemplates.IAvatarTemplateService templates,
    CancellationToken ct) =>
{
    if (!request.HasFormContentType)
    {
        return Results.BadRequest(new { success = false, message = "Yêu cầu upload phải là multipart/form-data." });
    }

    var form = await request.ReadFormAsync(ct);
    var file = form.Files.FirstOrDefault();
    if (file is null || file.Length == 0)
    {
        return Results.BadRequest(new { success = false, message = "Thiếu file ảnh." });
    }
    if (file.Length > 10 * 1024 * 1024)
    {
        return Results.BadRequest(new { success = false, message = "Ảnh vượt quá 10MB." });
    }

    await using var stream = file.OpenReadStream();
    using var ms = new MemoryStream();
    await stream.CopyToAsync(ms, ct);
    var media = await templates.SavePublicUploadAsync(ms.ToArray(), file.FileName, file.ContentType, ct);
    return Results.Json(new
    {
        success = true,
        mediaId = media.Id,
        url = media.PublicUrl ?? media.FileUrl
    });
}).DisableAntiforgery();

publicAvatarApi.MapPost("/render", async (
    TodoX.Web.Services.AvatarTemplates.PublicAvatarBuilderRenderRequest body,
    TodoX.Web.Services.AvatarTemplates.IAvatarTemplateService templates,
    ILoggerFactory loggerFactory,
    CancellationToken ct) =>
{
    var logger = loggerFactory.CreateLogger("PublicAvatarBuilderRender");
    try
    {
        var result = await templates.RenderPublicAsync(body, ct);
        return Results.Json(result);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "PUBLIC_AVATAR_RENDER_FAILED templateId={TemplateId}", body.TemplateId);
        return Results.Json(new TodoX.Web.Services.AvatarTemplates.PublicAvatarBuilderRenderResult
        {
            Ok = false,
            Status = "failed",
            Error = ex.Message
        }, statusCode: StatusCodes.Status500InternalServerError);
    }
}).DisableAntiforgery();

app.MapPost("/api/image-ai-creative-render", async (
    TodoX.Web.Services.Profile.ImageAICreativeRenderRequest body,
    TodoX.Web.Services.Profile.IImageAICreativeRenderService creativeRender,
    ILoggerFactory loggerFactory,
    CancellationToken ct) =>
{
    var logger = loggerFactory.CreateLogger("ImageAICreativeRenderApi");
    logger.LogInformation("API_IMAGE_AI_CREATIVE_RENDER_RECEIVED userId={UserId} customerId={CustomerId} scenario={Scenario} logCode={LogCode}",
        body.UserId, body.CustomerId, body.Scenario, body.LogCode);

    if (body.UserId == Guid.Empty)
    {
        logger.LogWarning("API_IMAGE_AI_CREATIVE_RENDER_FAILED missing user id scenario={Scenario}", body.Scenario);
        return Results.BadRequest(new
        {
            success = false,
            message = "Thieu UserId de render ImageAICreativeRender."
        });
    }

    try
    {
        var result = await creativeRender.RenderAsync(body, ct);
        logger.LogInformation("API_IMAGE_AI_CREATIVE_RENDER_COMPLETED userId={UserId} scenario={Scenario} logCode={LogCode} status={Status} imageCount={ImageCount}",
            body.UserId, body.Scenario, result.LogCode, result.Status, result.Images.Count);
        return Results.Json(result);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "API_IMAGE_AI_CREATIVE_RENDER_FAILED userId={UserId} scenario={Scenario} logCode={LogCode}",
            body.UserId, body.Scenario, body.LogCode);
        return Results.Json(new
        {
            success = false,
            status = "failed",
            error = ex.Message
        }, statusCode: StatusCodes.Status500InternalServerError);
    }
})
.WithName("ImageAICreativeRender")
.DisableAntiforgery();

static CurrentUserSession? ApiUser(AuthStateService auth) => auth.CurrentUser?.IsAuthenticated == true ? auth.CurrentUser : null;

var characterApi = app.MapGroup("/api/characters");

characterApi.MapGet("/", async (
    string? keyword,
    string? status,
    AuthStateService auth,
    IAiCharacterService characters,
    CancellationToken ct) =>
{
    var user = ApiUser(auth);
    if (user is null) return UnauthorizedJson("Ban can dang nhap de xem AI Characters.");
    try
    {
        return Results.Json(await characters.GetCharactersAsync(user, keyword, status, ct));
    }
    catch (Exception ex)
    {
        return Results.Json(new { success = false, message = ex.Message }, statusCode: StatusCodes.Status400BadRequest);
    }
});

characterApi.MapGet("/active", async (
    AuthStateService auth,
    IAiCharacterService characters,
    CancellationToken ct) =>
{
    var user = ApiUser(auth);
    if (user is null) return UnauthorizedJson("Ban can dang nhap de xem AI Characters.");
    try
    {
        return Results.Json(await characters.GetActiveCharactersAsync(user, ct));
    }
    catch (Exception ex)
    {
        return Results.Json(new { success = false, message = ex.Message }, statusCode: StatusCodes.Status400BadRequest);
    }
});

characterApi.MapGet("/{id:long}", async (
    long id,
    AuthStateService auth,
    IAiCharacterService characters,
    CancellationToken ct) =>
{
    var user = ApiUser(auth);
    if (user is null) return UnauthorizedJson("Ban can dang nhap de xem AI Character.");
    var item = await characters.GetCharacterAsync(user, id, ct);
    return item is null ? Results.NotFound() : Results.Json(item);
});

characterApi.MapPost("/", async (
    CreateCharacterRequest body,
    AuthStateService auth,
    IAiCharacterService characters,
    ILoggerFactory loggerFactory,
    CancellationToken ct) =>
{
    var user = ApiUser(auth);
    if (user is null) return UnauthorizedJson("Ban can dang nhap de tao AI Character.");
    var logger = loggerFactory.CreateLogger("AiCharacterApi");
    try
    {
        var created = await characters.CreateCharacterAsync(body, user, ct);
        logger.LogInformation("API_AI_CHARACTER_CREATED id={Id} userId={UserId}", created.Id, user.UserId);
        return Results.Json(created);
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "API_AI_CHARACTER_CREATE_FAILED userId={UserId}", user.UserId);
        return Results.Json(new { success = false, message = ex.Message }, statusCode: StatusCodes.Status400BadRequest);
    }
}).DisableAntiforgery();

characterApi.MapPut("/{id:long}", async (
    long id,
    UpdateCharacterRequest body,
    AuthStateService auth,
    IAiCharacterService characters,
    CancellationToken ct) =>
{
    var user = ApiUser(auth);
    if (user is null) return UnauthorizedJson("Ban can dang nhap de cap nhat AI Character.");
    await characters.UpdateCharacterAsync(id, body, user, ct);
    return Results.Json(new { success = true });
}).DisableAntiforgery();

characterApi.MapPost("/{id:long}/disable", async (
    long id,
    AuthStateService auth,
    IAiCharacterService characters,
    CancellationToken ct) =>
{
    var user = ApiUser(auth);
    if (user is null) return UnauthorizedJson("Ban can dang nhap de disable AI Character.");
    await characters.DisableCharacterAsync(id, user, ct);
    return Results.Json(new { success = true });
}).DisableAntiforgery();

characterApi.MapPost("/generate", async (
    GenerateCharacterImageRequest body,
    AuthStateService auth,
    IAiCharacterService characters,
    CancellationToken ct) =>
{
    var user = ApiUser(auth);
    if (user is null) return UnauthorizedJson("Ban can dang nhap de render AI Character.");
    var result = await characters.GenerateImageAsync(body, user, ct);
    return Results.Json(result, statusCode: result.Status == "completed" ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest);
}).DisableAntiforgery();

characterApi.MapPost("/{id:long}/render-variant", async (
    long id,
    GenerateCharacterImageRequest body,
    AuthStateService auth,
    IAiCharacterService characters,
    CancellationToken ct) =>
{
    var user = ApiUser(auth);
    if (user is null) return UnauthorizedJson("Ban can dang nhap de render AI Character.");
    body.CharacterId = id;
    var result = await characters.GenerateImageAsync(body, user, ct);
    return Results.Json(result, statusCode: result.Status == "completed" ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest);
}).DisableAntiforgery();

characterApi.MapPost("/{id:long}/set-master-image/{renderId:long}", async (
    long id,
    long renderId,
    AuthStateService auth,
    IAiCharacterService characters,
    CancellationToken ct) =>
{
    var user = ApiUser(auth);
    if (user is null) return UnauthorizedJson("Ban can dang nhap de chon anh master.");
    await characters.SetMasterImageAsync(id, renderId, user, ct);
    return Results.Json(new { success = true });
}).DisableAntiforgery();

// ---------------------------------------------------------------------------
// AI Provider Manager — admin management + user-facing selectable providers.
// ---------------------------------------------------------------------------
var aiProviderAdminApi = app.MapGroup("/api/admin/ai-providers");

aiProviderAdminApi.MapGet("/", async (
    AuthStateService auth,
    TodoX.Web.Services.AiProviders.IAiProviderService providers,
    CancellationToken ct) =>
{
    if (!IsHttpAdmin(auth)) return UnauthorizedJson("Bạn cần quyền quản trị để xem AI Provider.");
    return Results.Json(await providers.GetProvidersAsync(ct));
});

aiProviderAdminApi.MapGet("/{id:long}", async (
    long id,
    AuthStateService auth,
    TodoX.Web.Services.AiProviders.IAiProviderService providers,
    CancellationToken ct) =>
{
    if (!IsHttpAdmin(auth)) return UnauthorizedJson("Bạn cần quyền quản trị để xem AI Provider.");
    var item = await providers.GetProviderAsync(id, ct);
    return item is null ? Results.NotFound() : Results.Json(item);
});

aiProviderAdminApi.MapPut("/{id:long}", async (
    long id,
    UpdateAiProviderRequest body,
    AuthStateService auth,
    TodoX.Web.Services.AiProviders.IAiProviderService providers,
    CancellationToken ct) =>
{
    if (!IsHttpAdmin(auth) || auth.CurrentUser is null) return UnauthorizedJson("Bạn cần quyền quản trị để sửa AI Provider.");
    try
    {
        return Results.Json(await providers.UpdateProviderAsync(id, body, auth.CurrentUser, ct));
    }
    catch (Exception ex)
    {
        return Results.Json(new { success = false, message = ex.Message }, statusCode: StatusCodes.Status400BadRequest);
    }
}).DisableAntiforgery();

aiProviderAdminApi.MapGet("/{id:long}/capabilities", async (
    long id,
    AuthStateService auth,
    TodoX.Web.Services.AiProviders.IAiProviderService providers,
    CancellationToken ct) =>
{
    if (!IsHttpAdmin(auth)) return UnauthorizedJson("Bạn cần quyền quản trị để xem capability.");
    return Results.Json(await providers.GetCapabilitiesAsync(id, null, ct));
});

aiProviderAdminApi.MapGet("/capabilities", async (
    long? providerId,
    string? capabilityCode,
    AuthStateService auth,
    TodoX.Web.Services.AiProviders.IAiProviderService providers,
    CancellationToken ct) =>
{
    if (!IsHttpAdmin(auth)) return UnauthorizedJson("Bạn cần quyền quản trị để xem capability.");
    return Results.Json(await providers.GetCapabilitiesAsync(providerId, capabilityCode, ct));
});

aiProviderAdminApi.MapPut("/capabilities/{id:long}", async (
    long id,
    UpdateAiProviderCapabilityRequest body,
    AuthStateService auth,
    TodoX.Web.Services.AiProviders.IAiProviderService providers,
    CancellationToken ct) =>
{
    if (!IsHttpAdmin(auth) || auth.CurrentUser is null) return UnauthorizedJson("Bạn cần quyền quản trị để sửa capability.");
    try
    {
        return Results.Json(await providers.UpdateCapabilityAsync(id, body, auth.CurrentUser, ct));
    }
    catch (Exception ex)
    {
        return Results.Json(new { success = false, message = ex.Message }, statusCode: StatusCodes.Status400BadRequest);
    }
}).DisableAntiforgery();

aiProviderAdminApi.MapPost("/capabilities/{id:long}/set-default", async (
    long id,
    AuthStateService auth,
    TodoX.Web.Services.AiProviders.IAiProviderService providers,
    CancellationToken ct) =>
{
    if (!IsHttpAdmin(auth) || auth.CurrentUser is null) return UnauthorizedJson("Bạn cần quyền quản trị để đặt provider mặc định.");
    try
    {
        await providers.SetDefaultCapabilityAsync(id, auth.CurrentUser, ct);
        return Results.Json(new { success = true });
    }
    catch (Exception ex)
    {
        return Results.Json(new { success = false, message = ex.Message }, statusCode: StatusCodes.Status400BadRequest);
    }
}).DisableAntiforgery();

// User-facing: providers a signed-in user may choose for a capability (no secrets).
app.MapGet("/api/ai-providers/selectable", async (
    string capabilityCode,
    AuthStateService auth,
    TodoX.Web.Services.AiProviders.IAiProviderService providers,
    CancellationToken ct) =>
{
    if (ApiUser(auth) is null) return UnauthorizedJson("Bạn cần đăng nhập để chọn AI Provider.");
    if (string.IsNullOrWhiteSpace(capabilityCode)) return Results.Json(Array.Empty<ProviderOptionDto>());
    return Results.Json(await providers.GetSelectableProvidersAsync(capabilityCode, ct));
});

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
