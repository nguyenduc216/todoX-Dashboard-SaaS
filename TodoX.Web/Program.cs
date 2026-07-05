using TodoX.Web.Components;
using TodoX.Web.Data;
using TodoX.Web.Models;
using TodoX.Web.Services;
using TodoX.Web.Services.Render;
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
builder.Services.AddScoped<AuditRepository>();
builder.Services.AddScoped<BillingRepository>();
builder.Services.AddScoped<CatalogRepository>();
builder.Services.AddScoped<CatalogAdminRepository>();
builder.Services.AddScoped<MrTodoXAvatarService>();
builder.Services.AddScoped<SystemImageStorage>();
builder.Services.AddScoped<ServiceThumbnailRenderService>();
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
builder.Services.AddScoped<TodoX.Web.Services.ImageRender.IImageRenderService, TodoX.Web.Services.ImageRender.VertexImageRenderService>();
builder.Services.AddScoped<TodoX.Web.Services.Profile.IAvatarService, TodoX.Web.Services.Profile.AvatarService>();
builder.Services.AddHttpClient<TodoX.Web.Services.Profile.GeminiPromptService>();
builder.Services.AddScoped<TodoX.Web.Services.Profile.AvatarRenderActivityLogService>();
builder.Services.AddScoped<TodoX.Web.Services.Profile.IChibiAvatarService, TodoX.Web.Services.Profile.ChibiAvatarService>();
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<AuthStateService>();
builder.Services.AddScoped<StartupSeedFixer>();
builder.Services.AddScoped<TokenSettingsService>();
builder.Services.AddScoped<WalletService>();
builder.Services.AddScoped<IRenderJobService, RenderJobService>();
builder.Services.AddScoped<IRenderJobDispatcher, RenderJobDispatcher>();
builder.Services.AddHostedService<RenderJobWorker>();
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

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
