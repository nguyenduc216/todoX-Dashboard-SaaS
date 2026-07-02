using TodoX.Web.Components;
using TodoX.Web.Data;
using TodoX.Web.Services;
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
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<AuthStateService>();
builder.Services.AddScoped<StartupSeedFixer>();

var app = builder.Build();

// Load tenant and repair placeholder seed credentials (writes data only, never schema).
using (var scope = app.Services.CreateScope())
{
    var tenant = scope.ServiceProvider.GetRequiredService<TenantContext>();
    await tenant.EnsureLoadedAsync();
    var fixer = scope.ServiceProvider.GetRequiredService<StartupSeedFixer>();
    await fixer.RunAsync();
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
