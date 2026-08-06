var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    context.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; img-src 'self' data: https://todox.vn; " +
        "style-src 'self' 'unsafe-inline'; script-src 'self'; " +
        "font-src 'self'; connect-src 'self'; frame-ancestors 'self'; base-uri 'self'; form-action 'self'";

    await next();
});

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        var path = context.File.Name;
        if (path is not null && path.Contains('.'))
        {
            context.Context.Response.Headers.CacheControl = "public,max-age=604800";
        }
    }
});

app.MapGet("/health", () => Results.Ok(new
{
    service = "TodoX.Landing",
    status = "ok"
}));

app.MapFallbackToFile("index.html");
app.Run();
