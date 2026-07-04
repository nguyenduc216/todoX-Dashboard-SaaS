using TodoX.Web.Components;
using TodoX.Web.Data;
using TodoX.Web.Services;
using TodoX.Web.Services.Render;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// TodoX UI services.
builder.Services.AddMudServices();

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
builder.Services.AddScoped<SocialPageRepository>();
builder.Services.AddScoped<AutomationSettingsRepository>();
builder.Services.AddHttpClient<FacebookGraphService>();

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

// Serve runtime-uploaded files (avatars, chibi, references) from wwwroot/uploads.
app.UseStaticFiles();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
