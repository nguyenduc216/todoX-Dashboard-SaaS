using TodoX.Web.Components;
using TodoX.Web.Data;
using TodoX.Web.Models;
using TodoX.Web.Services;
using TodoX.Web.Services.Render;
using TodoX.Web.Services.Platform;
using TodoX.Web.Services.Reup;
using TodoX.Web.Services.AiCharacters;
using TodoX.Web.Services.AiProviders.Kie;
using TodoX.Web.Services.DanceSell;
using TodoX.Web.Services.Landing;
using TodoX.Web.Services.SharedMedia;
using TodoX.Web.Services.Timelapse;
using TodoX.Web.Services.VideoRender;
using MudBlazor.Services;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using System.Reflection;

var builder = WebApplication.CreateBuilder(args);
var instanceStartedUtc = DateTimeOffset.UtcNow;

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
builder.Services.AddTodoXCorePlatform();
builder.Services.AddSingleton<PasswordHasher>();
builder.Services.AddScoped<AccountRepository>();
builder.Services.AddScoped<CustomerRepository>();
builder.Services.AddScoped<ServiceFavoriteRepository>();
builder.Services.AddScoped<PermissionRepository>();
builder.Services.AddScoped<NavigationMenuRepository>();
builder.Services.AddScoped<LandingContactLeadRepository>();
builder.Services.AddScoped<LandingIndustrySolutionRepository>();
builder.Services.AddScoped<LandingIndustryMediaService>();
builder.Services.AddSingleton<SharedMediaPathService>();
builder.Services.Configure<SharedMediaOptions>(builder.Configuration.GetSection(SharedMediaOptions.SectionName));
builder.Services.AddScoped<AuditRepository>();
builder.Services.AddScoped<BillingRepository>();
builder.Services.AddScoped<CatalogRepository>();
builder.Services.AddScoped<ICustomerDashboardService, CustomerDashboardService>();
builder.Services.AddScoped<CatalogAdminRepository>();
builder.Services.AddScoped<IAiStudioCatalogService, AiStudioCatalogService>();
builder.Services.AddScoped<IServiceSellPriceResolver, ServiceSellPriceResolver>();
builder.Services.AddScoped<IPointPricingService, PointPricingService>();
builder.Services.AddScoped<ITimelapseProfileRepository, TimelapseProfileRepository>();
builder.Services.AddScoped<ITimelapseWorkflowService, TimelapseWorkflowService>();
builder.Services.AddScoped<ITimelapseJobService, TimelapseJobService>();
builder.Services.Configure<TimelapseProviderWorkerOptions>(builder.Configuration.GetSection(TimelapseProviderWorkerOptions.SectionName));
builder.Services.AddScoped<TimelapseImageModelSelector>();
builder.Services.AddScoped<ITimelapseWorkerRepository, TimelapseWorkerRepository>();
builder.Services.AddScoped<ITimelapseCoreLifecycleBridge, TimelapseCoreLifecycleBridge>();
builder.Services.AddScoped<IConstructionTimelapseExecutionBridge, ConstructionTimelapseExecutionBridge>();
builder.Services.AddScoped<ICoreJobExecutionAdapter, ConstructionTimelapseAdapter>();
builder.Services.AddScoped<ITimelapseProviderRuntime, TimelapseProviderRuntime>();
builder.Services.AddScoped<ITimelapseFinalizerRuntime, TimelapseFinalizerRuntime>();
builder.Services.AddHttpClient<TodoX.Web.Services.AiProviders.IAi79TaskClient, TodoX.Web.Services.AiProviders.Ai79TaskClient>(client =>
{
    client.Timeout = TimeSpan.FromMinutes(3);
});
builder.Services.AddHttpClient("DanceSellDownload", client => client.Timeout = TimeSpan.FromMinutes(5))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
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
builder.Services.AddScoped<TodoX.Web.Services.Media.LocalMediaPathResolver>();
builder.Services.AddHttpClient("MediaBinaryDownload", client => client.Timeout = TimeSpan.FromSeconds(60))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = System.Net.DecompressionMethods.All
    });
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
builder.Services.Configure<DanceSellPhase2Options>(builder.Configuration.GetSection(DanceSellPhase2Options.SectionName));
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
builder.Services.AddScoped<TodoX.Web.Services.AiProviders.Gommo79AiImageService>();
builder.Services.AddScoped<IAiImageProviderFactory, AiImageProviderFactory>();
builder.Services.AddScoped<CharacterPromptBuilder>();
builder.Services.AddScoped<AiCharacterRepository>();
builder.Services.AddScoped<IAiCharacterService, AiCharacterService>();
builder.Services.AddScoped<TodoX.Web.Services.AiProviders.AiProviderRepository>();
builder.Services.AddScoped<TodoX.Web.Services.AiProviders.IAiProviderService, TodoX.Web.Services.AiProviders.AiProviderService>();
builder.Services.AddScoped<TodoX.Web.Services.AiProviders.AiProviderModelRepository>();
builder.Services.AddScoped<TodoX.Web.Services.AiProviders.IAiProviderModelService, TodoX.Web.Services.AiProviders.AiProviderModelService>();
builder.Services.AddScoped<TodoX.Web.Services.AiProviders.AiPricingRepository>();
builder.Services.AddScoped<TodoX.Web.Services.AiProviders.IAiPricingService, TodoX.Web.Services.AiProviders.AiPricingService>();
builder.Services.AddScoped<TodoX.Web.Services.AiProviders.IAiProviderSyncService, TodoX.Web.Services.AiProviders.AiProviderSyncService>();
builder.Services.AddScoped<TodoX.Web.Services.AiProviders.IProviderCredentialKeyStore, TodoX.Web.Services.AiProviders.ProviderCredentialKeyStore>();
builder.Services.AddScoped<TodoX.Web.Services.AiProviders.IProviderCredentialProtector, TodoX.Web.Services.AiProviders.ProviderCredentialProtector>();
builder.Services.AddScoped<TodoX.Web.Services.AiProviders.IProviderCredentialRepository, TodoX.Web.Services.AiProviders.ProviderCredentialRepository>();
builder.Services.AddScoped<TodoX.Web.Services.AiProviders.IProviderCredentialResolver, TodoX.Web.Services.AiProviders.ProviderCredentialResolver>();
builder.Services.AddScoped<TodoX.Web.Services.AiProviders.IAi79CredentialMigrationService, TodoX.Web.Services.AiProviders.Ai79CredentialMigrationService>();
builder.Services.AddHttpClient<TodoX.Web.Services.AiProviders.IAi79CatalogClient, TodoX.Web.Services.AiProviders.Ai79CatalogClient>();
builder.Services.AddHttpClient("AiStudioMusicImport", client => client.Timeout = Timeout.InfiniteTimeSpan)
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.Configure<TodoX.Web.Services.AiProviders.AiProviderCatalogSyncOptions>(builder.Configuration.GetSection(TodoX.Web.Services.AiProviders.AiProviderCatalogSyncOptions.SectionName));
builder.Services.AddHostedService<TodoX.Web.Services.AiProviders.AiProviderCatalogSyncWorker>();
builder.Services.AddScoped<TodoX.Web.Services.AiProviders.IAiBillingPayerResolver, TodoX.Web.Services.AiProviders.AiBillingPayerResolver>();
builder.Services.AddScoped<TodoX.Web.Services.AiProviders.IAiImageBillingService, TodoX.Web.Services.AiProviders.AiImageBillingService>();
builder.Services.AddScoped<TodoX.Web.Services.AiProviders.IAiImageBillingDashboardService, TodoX.Web.Services.AiProviders.AiImageBillingDashboardService>();
builder.Services.AddSingleton<TodoX.Web.Services.AiProviders.IYEScaleAccountService, TodoX.Web.Services.AiProviders.YEScaleAccountService>();
builder.Services.AddScoped<TodoX.Web.Services.AiProviders.IAiImageRenderRouter, TodoX.Web.Services.AiProviders.AiImageRenderRouter>();
if (TodoX.Web.Services.AiProviders.AiImageBillingFeatureFlags.IsReconciliationWorkerEnabled(builder.Configuration))
{
    builder.Services.AddHostedService<TodoX.Web.Services.AiProviders.AiImageBillingReconciliationWorker>();
}
builder.Services.Configure<TodoX.Web.Services.VideoRender.VideoRenderOptions>(builder.Configuration.GetSection("VideoRender"));
builder.Services.Configure<TodoX.Web.Services.VideoRender.VbeeOptions>(builder.Configuration.GetSection(TodoX.Web.Services.VideoRender.VbeeOptions.SectionName));
builder.Services.PostConfigure<TodoX.Web.Services.VideoRender.VbeeOptions>(options =>
{
    ApplyEnv("VBEE_API_BASE_URL", value => options.ApiBaseUrl = value);
    ApplyEnv("VBEE_TTS_PATH", value => options.TtsPath = value);
    ApplyEnv("VBEE_API_TOKEN", value => options.ApiToken = value);
    ApplyEnv("VBEE_APP_ID", value => options.AppId = value);
    ApplyEnv("VBEE_CALLBACK_URL", value => options.CallbackUrl = value);
    ApplyEnv("VBEE_CALLBACK_SECRET", value => options.CallbackSecret = value);
    ApplyEnvInt("VBEE_DEFAULT_SAMPLE_RATE", value => options.DefaultSampleRate = value);
    ApplyEnvInt("VBEE_DEFAULT_BITRATE", value => options.DefaultBitrate = value);
    ApplyEnvDecimal("VBEE_DEFAULT_SPEED_RATE", value => options.DefaultSpeedRate = value);
    ApplyEnvInt("VBEE_HTTP_TIMEOUT_SECONDS", value => options.HttpTimeoutSeconds = value);
    ApplyEnvInt("VBEE_POLL_INTERVAL_SECONDS", value => options.PollIntervalSeconds = value);
    ApplyEnvInt("VBEE_MAX_POLL_COUNT", value => options.MaxPollCount = value);
});
builder.Services.AddScoped<TodoX.Web.Services.VideoRender.IVbeeRuntimeConfigProvider, TodoX.Web.Services.VideoRender.VbeeRuntimeConfigProvider>();
builder.Services.AddHttpClient<TodoX.Web.Services.VideoRender.IVbeeVoiceClient, TodoX.Web.Services.VideoRender.VbeeVoiceClient>()
    .ConfigureHttpClient((sp, client) =>
    {
        var options = sp.GetRequiredService<IOptionsMonitor<TodoX.Web.Services.VideoRender.VbeeOptions>>().CurrentValue;
        client.Timeout = options.HttpTimeout;
    });
builder.Services.AddScoped<TodoX.Web.Services.VideoRender.IRVideoSceneAudioAutoChainService, TodoX.Web.Services.VideoRender.RVideoSceneAudioAutoChainService>();
builder.Services.AddScoped<TodoX.Web.Services.VideoRender.IRVideoSceneMediaFinalizerService, TodoX.Web.Services.VideoRender.RVideoSceneMediaFinalizerService>();
builder.Services.AddScoped<TodoX.Web.Services.VideoRender.IRVideoSceneVideoCompletionService, TodoX.Web.Services.VideoRender.RVideoSceneVideoCompletionService>();
builder.Services.AddSingleton<TodoX.Web.Services.VideoRender.ITodoXVideoPromptParser, TodoX.Web.Services.VideoRender.TodoXVideoPromptParser>();
builder.Services.AddSingleton<TodoX.Web.Services.VideoRender.IVideoPromptValidator, TodoX.Web.Services.VideoRender.VideoPromptValidator>();
builder.Services.AddScoped<TodoX.Web.Services.VideoRender.VideoRenderRepository>();
builder.Services.AddScoped<TodoX.Web.Services.VideoRender.IRVideoTrustedPayerContextService, TodoX.Web.Services.VideoRender.RVideoTrustedPayerContextService>();
builder.Services.AddScoped<TodoX.Web.Services.VideoRender.RVideoJobSettingsRepository>();
builder.Services.AddScoped<TodoX.Web.Services.VideoRender.IRVideoJobService, TodoX.Web.Services.VideoRender.RVideoJobService>();
builder.Services.AddScoped<TodoX.Web.Services.VideoRender.IRVideoProjectFinalizationService, TodoX.Web.Services.VideoRender.RVideoProjectFinalizationService>();
builder.Services.AddSingleton<TodoX.Web.Services.VideoRender.RVideoSceneJsonService>();
builder.Services.AddScoped<TodoX.Web.Services.VideoRender.ISceneMediaVersioningService, TodoX.Web.Services.VideoRender.SceneMediaVersioningService>();
builder.Services.AddScoped<TodoX.Web.Services.VideoRender.IRVideo79AiVideoService, TodoX.Web.Services.VideoRender.RVideo79AiVideoService>();
builder.Services.AddScoped<TodoX.Web.Services.VideoRender.IVideoProviderRoutingService, TodoX.Web.Services.VideoRender.VideoProviderRoutingService>();
builder.Services.AddScoped<TodoX.Web.Services.VideoRender.IVideoGenerationProviderAdapterResolver, TodoX.Web.Services.VideoRender.VideoGenerationProviderAdapterResolver>();
builder.Services.AddScoped<TodoX.Web.Services.VideoRender.IVideoGenerationProviderAdapter, TodoX.Web.Services.VideoRender.Ai79VideoGenerationProviderAdapter>();
builder.Services.AddScoped<TodoX.Web.Services.VideoRender.IVideoRenderPricingResolver, TodoX.Web.Services.VideoRender.VideoRenderPricingResolver>();
builder.Services.AddScoped<TodoX.Web.Services.VideoRender.IVideoRenderEligibilityService, TodoX.Web.Services.VideoRender.VideoRenderEligibilityService>();
builder.Services.AddScoped<TodoX.Web.Services.VideoRender.IYEScaleVideoPricingResolver, TodoX.Web.Services.VideoRender.YEScaleVideoPricingResolver>();
builder.Services.AddScoped<IDanceSellRepository, DanceSellRepository>();
builder.Services.AddScoped<IRDanceDownloadTicketService, RDanceDownloadTicketService>();
builder.Services.AddScoped<IDanceSellCompletionService, DanceSellCompletionService>();
builder.Services.AddScoped<IDanceSellMotionSourceService, DanceSellMotionSourceService>();
builder.Services.AddScoped<IDanceSellReferenceImageService, DanceSellReferenceImageService>();
builder.Services.AddScoped<IDanceSellReferenceComparisonService, DanceSellReferenceComparisonService>();
builder.Services.AddScoped<IDanceSellPhase2Service, DanceSellPhase2Service>();
builder.Services.AddScoped<IDanceSellProviderCatalog, DanceSellProviderCatalog>();
builder.Services.AddScoped<IDanceSellOperationRepository, DanceSellOperationRepository>();
builder.Services.AddScoped<IDanceSellCostEstimator, DanceSellCostEstimator>();
builder.Services.AddScoped<IAiOperationBillingService, AiOperationBillingService>();
builder.Services.AddScoped<IAiProviderBalanceClient, KieBalanceClient>();
builder.Services.AddScoped<IAiProviderBalanceClientFactory, AiProviderBalanceClientFactory>();
builder.Services.AddScoped<IDanceSellReferenceProvider, KieDanceSellReferenceProvider>();
builder.Services.AddScoped<IDanceSellReferenceProvider, Ai79DanceSellReferenceProvider>();
builder.Services.AddScoped<IDanceSellReferenceProviderFactory, DanceSellReferenceProviderFactory>();
builder.Services.AddScoped<IRenderJobHandler, TodoX.Web.Services.VideoRender.SceneVideoRenderHandler>();
builder.Services.AddScoped<IRenderJobHandler, TodoX.Web.Services.VideoRender.SceneAudioRenderHandler>();
builder.Services.AddScoped<IRenderJobHandler, TodoX.Web.Services.VideoRender.SceneAudioMuxHandler>();
builder.Services.AddScoped<IRenderJobHandler, TodoX.Web.Services.VideoRender.SceneVideoWorkerHandler>();
builder.Services.AddScoped<IRenderJobHandler, TodoX.Web.Services.VideoRender.VideoRenderMergeHandler>();
builder.Services.AddScoped<IRVideoSceneVideoAutoChainService, RVideoSceneVideoAutoChainService>();
builder.Services.AddScoped<IRenderJobHandler, TodoX.Web.Services.Render.SceneImageBatchRenderHandler>();
builder.Services.AddScoped<IRenderJobHandler, TodoX.Web.Services.Render.SceneImageRenderWorkItemHandler>();
builder.Services.AddScoped<IRenderJobHandler, DanceSellRenderHandler>();
builder.Services.AddScoped<TodoX.Web.Services.Render.ISceneImageRenderService, TodoX.Web.Services.Render.SceneImageRenderService>();
builder.Services.AddSingleton<TodoX.Web.Services.Render.GoogleVertexRateLimiter>();
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<AuthStateService>();
builder.Services.AddScoped<StartupSeedFixer>();
builder.Services.AddScoped<TokenSettingsService>();
builder.Services.AddScoped<WalletService>();
builder.Services.AddSingleton<IPointBalanceChangeNotifier, PointBalanceChangeNotifier>();
builder.Services.AddScoped<IRenderJobService, RenderJobService>();
builder.Services.AddScoped<IRenderJobDispatcher, RenderJobDispatcher>();
builder.Services.AddHostedService<RenderJobWorker>();
builder.Services.AddHostedService<TodoX.Web.Services.Render.SceneVideoJobWorker>();
builder.Services.AddHostedService<TodoX.Web.Services.VideoRender.SceneVideoReconciliationWorker>();
builder.Services.AddHostedService<TodoX.Web.Services.VideoRender.RVideoLifecycleWorker>();
builder.Services.AddHostedService<TodoX.Web.Services.Timelapse.TimelapseImageWorker>();
builder.Services.AddHostedService<TodoX.Web.Services.Timelapse.TimelapseVideoWorker>();
builder.Services.AddHostedService<TodoX.Web.Services.Timelapse.TimelapseFinalizerWorker>();
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

app.Logger.LogInformation(
    "TIMELAPSE_HOSTED_SERVICES_CONFIGURED imageWorker=true videoWorker=true finalizerWorker=true renderWorker=true sceneVideoWorker=true rVideoLifecycleWorker=true reupWorker=true");

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

static void ApplyEnvDecimal(string key, Action<decimal> apply)
{
    var value = Environment.GetEnvironmentVariable(key);
    if (decimal.TryParse(value, out var parsed))
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

var sharedMediaOptions = app.Services.GetRequiredService<IOptions<SharedMediaOptions>>().Value;
if (!string.IsNullOrWhiteSpace(sharedMediaOptions.StorageRoot))
{
    try
    {
        var physicalRoot = Path.GetFullPath(sharedMediaOptions.StorageRoot);
        Directory.CreateDirectory(physicalRoot);
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(physicalRoot),
            RequestPath = "/" + (sharedMediaOptions.RequestPath ?? "/media").Trim('/')
        });
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "SharedMedia static file mapping is not ready.");
    }
}

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
app.MapDanceSellPhase2Endpoints();
app.MapAiStudioCatalogEndpoints();
app.MapRVideoEndpoints();
app.MapSceneAudioEndpoints();

app.MapGet("/system/version", (IConfiguration configuration) =>
{
    var assembly = typeof(Program).Assembly;
    var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
    var plus = informationalVersion.IndexOf('+');
    var version = plus >= 0 ? informationalVersion[..plus] : informationalVersion;
    var metadata = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(x => x.Key, x => x.Last().Value, StringComparer.OrdinalIgnoreCase);
    var buildMetadata = (string key) =>
        metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : "unknown";

    return Results.Json(new
    {
        application = "todoX Dashboard SaaS",
        environment = app.Environment.EnvironmentName,
        version,
        commit = buildMetadata("BuildCommit"),
        branch = buildMetadata("BuildBranch"),
        buildTimeUtc = buildMetadata("BuildTimeUtc"),
        instanceStartedUtc,
        features = new
        {
            renderQueueEnabled = configuration.GetValue("RenderQueue:Enabled", false),
            rvideoLifecycleRegistered = app.Services.GetServices<IHostedService>().OfType<RVideoLifecycleWorker>().Any(),
            legacyPointBillingEnabled = LegacyPointBillingFeatureFlags.IsEnabled(configuration)
        }
    });
});

app.MapPost("/api/ai/cost/estimate", async (
    TodoX.Web.Services.AiProviders.IAiPricingService pricing,
    EstimateCostRequestDto request,
    CancellationToken ct) =>
{
    var result = await pricing.EstimateAsync(request, ct);
    return Results.Json(result);
});

if (app.Configuration.GetValue("CoreApi:Enabled", false))
{
    app.MapTodoXCoreApiV1();
}

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
