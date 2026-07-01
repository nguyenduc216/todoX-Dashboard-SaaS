using TodoX.Web.Components;
using TodoX.Web.Services;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// TodoX UI services.
builder.Services.AddMudServices();

// Sprint 2B application services.
// This Sprint uses an in-memory store so the UI can be tested immediately.
// PostgreSQL schema/seed scripts are included in /database and will be wired to EF Core in the next Sprint.
builder.Services.AddSingleton<TodoXMockDataStore>();
builder.Services.AddScoped<AuthStateService>();
builder.Services.AddScoped<AccountService>();

var app = builder.Build();

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
