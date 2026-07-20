using TodoX.Web.Components;
using TodoX.Web.Data;
using TodoX.Web.Models;
using TodoX.Web.Services;
using TodoX.Web.Services.Render;
using TodoX.Web.Services.Reup;
using TodoX.Web.Services.AiCharacters;
using TodoX.Web.Services.AiProviders.Kie;
using TodoX.Web.Services.DanceSell;
using MudBlazor.Services;
using Microsoft.Extensions.FileProviders;

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
builder.Services.Configure<TodoX.Web.Services.AiProviders.YEScaleOptions>(builder.Configuration.GetSection("AiProviders:YEScale"));
builder.Services.AddHttpClient<TodoX.Web.Services.AiProviders.IYEScaleTaskClient, TodoX.Web.Services.AiProviders.YEScaleTaskClient>();
builder.Services.Configure<KieOptions>(builder.Configuration.GetSection(KieOptions.SectionName));
builder.Services.PostConfigure<KieOptions>(options =>
{
    ApplyEnv("KIE_API_BASE_URL", value => options.ApiBaseUrl = value);
    ApplyEnv("KIE_API_KEY", value => options.ApiKey = value);
    ApplyEnv("KIE_CALLBACK_URL", value => options.CallbackUrl = value);
    ApplyEnv("KIE_CALLBACK_SECRET", value => options.CallbackSecret = value);
    ApplyEnv("KIE_MOTION_CONTROL_MODEL", value => options.MotionControlModel = value);
    ApplyEnv("KIE_DEFAULT_MODE", value => options.DefaultMode = value);
    ApplyEnvInt("KIE_HTTP_TIMEOUT_SECONDS", value => options.HttpTimeoutSeconds = value);
    ApplyEnvInt("KIE_POLL_INTERVAL_SECONDS", value => options.PollIntervalSeconds = value);
    ApplyEnvInt("KIE_MAX_POLL_COUNT", value => options.MaxPollCount = value);
    ApplyEnvInt("KIE_SUBMIT_MAX_RETRY", value => options.SubmitMaxRetry = value);
    ApplyEnvInt("KIE_RATE_LIMIT_REQUESTS_PER_10S", value => options.RateLimitRequestsPer10S = value);
    ApplyEnvInt("KIE_MAX_CONCURRENT_TASKS", value => options.MaxConcurrentTasks = value);
});
builder.Services.AddHttpClient<IKieClient, KieClient>();
builder.Services.AddScoped<IKiePayloadBuilder, KiePayloadBuilder>();
builder.Services.AddSingleton<IKieRateLimiter, InMemoryKieRateLimiter>();
builder.Services.AddScoped<TodoX.Web.Services.AiProviders.IYEScaleImageService, TodoX.Web.Services.AiProviders.YEScaleImageService>();
builder.Services.AddScoped<IAiImageProviderFactory, AiImageProviderFactory>();
builder.Services.AddScoped<CharacterPromptBuilder>();
builder.Services.AddScoped<AiCharacterRepository>();
builder.Services.AddScoped<IAiCharacterService, AiCharacterService>();
builder.Services.AddScoped<TodoX.Web.Services.AiProviders.AiProviderRepository>();
builder.Services.AddScoped<TodoX.Web.Services.AiProviders.IAiProviderService, TodoX.Web.Services.AiProviders.AiProviderService>();
builder.Services.AddScoped<TodoX.Web.Services.AiProviders.IAiBillingPayerResolver, TodoX.Web.Services.AiProviders.AiBillingPayerResolver>();
builder.Services.AddScoped<TodoX.Web.Services.AiProviders.IAiImageBillingService, TodoX.Web.Services.AiProviders.AiImageBillingService>();
builder.Services.AddScoped<TodoX.Web.Services.AiProviders.IAiImageBillingDashboardService, TodoX.Web.Services.AiProviders.AiImageBillingDashboardService>();
builder.Services.AddSingleton<TodoX.Web.Services.AiProviders.IYEScaleAccountService, TodoX.Web.Services.AiProviders.YEScaleAccountService>();
builder.Services.AddScoped<TodoX.Web.Services.AiProviders.IAiImageRenderRouter, TodoX.Web.Services.AiProviders.AiImageRenderRouter>();
builder.Services.AddHostedService<TodoX.Web.Services.AiProviders.AiImageBillingReconciliationWorker>();
builder.Services.Configure<TodoX.Web.Services.VideoRender.VideoRenderOptions>(builder.Configuration.GetSection("VideoRender"));
builder.Services.AddSingleton<TodoX.Web.Services.VideoRender.ITodoXVideoPromptParser, TodoX.Web.Services.VideoRender.TodoXVideoPromptParser>();
builder.Services.AddSingleton<TodoX.Web.Services.VideoRender.IVideoPromptValidator, TodoX.Web.Services.VideoRender.VideoPromptValidator>();
builder.Services.AddScoped<TodoX.Web.Services.VideoRender.VideoRenderRepository>();
builder.Services.AddScoped<TodoX.Web.Services.VideoRender.ISceneMediaVersioningService, TodoX.Web.Services.VideoRender.SceneMediaVersioningService>();
builder.Services.AddScoped<TodoX.Web.Services.VideoRender.IYEScaleVideoPricingResolver, TodoX.Web.Services.VideoRender.YEScaleVideoPricingResolver>();
builder.Services.AddScoped<IDanceSellRepository, DanceSellRepository>();
builder.Services.AddScoped<IDanceSellCompletionService, DanceSellCompletionService>();
builder.Services.AddScoped<IRenderJobHandler, TodoX.Web.Services.VideoRender.SceneVideoRenderHandler>();
builder.Services.AddScoped<IRenderJobHandler, TodoX.Web.Services.VideoRender.SceneVideoWorkerHandler>();
builder.Services.AddScoped<IRenderJobHandler, TodoX.Web.Services.VideoRender.VideoRenderMergeHandler>();
builder.Services.AddScoped<IRenderJobHandler, TodoX.Web.Services.Render.SceneImageBatchRenderHandler>();
builder.Services.AddScoped<IRenderJobHandler, DanceSellRenderHandler>();
builder.Services.AddScoped<TodoX.Web.Services.Render.ISceneImageRenderService, TodoX.Web.Services.Render.SceneImageRenderService>();
builder.Services.AddSingleton<TodoX.Web.Services.Render.GoogleVertexRateLimiter>();
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<AuthStateService>();
builder.Services.AddScoped<StartupSeedFixer>();
builder.Services.AddScoped<TokenSettingsService>();
builder.Services.AddScoped<WalletService>();
builder.Services.AddScoped<IRenderJobService, RenderJobService>();
builder.Services.AddScoped<IRenderJobDispatcher, RenderJobDispatcher>();
builder.Services.AddHostedService<RenderJobWorker>();
builder.Services.AddHostedService<TodoX.Web.Services.Render.SceneVideoJobWorker>();
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

static void ApplyEnv(string key, Action<string> apply)
{
    var value = Environment.GetEnvironmentVariable(key);
    if (!string.IsNullOrWhiteSpace(value))
    {
        apply(value.Trim());
    }
}

static void ApplyEnvInt(string key, Action<int> apply)
{
    var value = Environment.GetEnvironmentVariable(key);
    if (int.TryParse(value, out var parsed))
    {
        apply(parsed);
    }
}

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

var videoStorageRoot = app.Configuration["VideoRender:StorageRoot"];
var videoPublicBase = app.Configuration["VideoRender:PublicBase"];
if (!string.IsNullOrWhiteSpace(videoStorageRoot) && !string.IsNullOrWhiteSpace(videoPublicBase))
{
    var physicalRoot = Path.IsPathRooted(videoStorageRoot)
        ? videoStorageRoot
        : Path.Combine(app.Environment.ContentRootPath, videoStorageRoot);
    var requestPath = videoPublicBase.StartsWith("http", StringComparison.OrdinalIgnoreCase)
        ? new Uri(videoPublicBase).AbsolutePath
        : videoPublicBase;
    if (!string.IsNullOrWhiteSpace(requestPath))
    {
        Directory.CreateDirectory(physicalRoot);
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(physicalRoot),
            RequestPath = requestPath.TrimEnd('/')
        });
    }
}

app.UseAntiforgery();

app.MapStaticAssets();

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

app.MapDanceSellPhase1Endpoints();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
