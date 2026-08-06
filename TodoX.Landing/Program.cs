using System.Diagnostics;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using TodoX.Landing.Data;
using TodoX.Landing.Models;
using TodoX.Landing.Services;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 32 * 1024;
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});
builder.Services.AddSingleton<LandingConnectionFactory>();
builder.Services.AddScoped<LandingContactRepository>();
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("contact-leads", httpContext =>
    {
        var key = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(10),
            QueueLimit = 0,
            AutoReplenishment = true
        });
    });
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

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

app.UseForwardedHeaders();
app.UseRateLimiter();
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

app.MapGet("/health/ready", async (LandingContactRepository repository, CancellationToken ct) =>
{
    var ready = await repository.IsReadyAsync(ct);
    return ready
        ? Results.Ok(new { service = "TodoX.Landing", status = "ready" })
        : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
});

app.MapPost("/api/contact-leads", async (
    HttpContext http,
    ContactLeadCreateRequest request,
    LandingContactRepository repository,
    ILoggerFactory loggerFactory,
    CancellationToken ct) =>
{
    var logger = loggerFactory.CreateLogger("LandingContactLeads");
    var requestId = Activity.Current?.Id ?? http.TraceIdentifier;
    var started = Stopwatch.GetTimestamp();

    static ContactLeadCreateResponse Success(string? leadCode = null) => new()
    {
        Success = true,
        LeadCode = leadCode,
        Message = "TodoX đã nhận thông tin. Chúng tôi sẽ liên hệ tư vấn trong thời gian sớm nhất."
    };

    if (!string.IsNullOrWhiteSpace(request.Website))
    {
        logger.LogInformation("Landing lead honeypot ignored. requestId={RequestId}", requestId);
        return Results.Ok(Success());
    }

    var (validation, lead) = ContactLeadValidator.Validate(request);
    if (!validation.IsValid || lead is null)
    {
        logger.LogInformation("Landing lead validation failed. requestId={RequestId}", requestId);
        return Results.ValidationProblem(validation.Errors);
    }

    try
    {
        var leadCode = await repository.InsertAsync(lead, new ContactLeadInsertContext
        {
            RequestId = requestId,
            IpAddress = http.Connection.RemoteIpAddress?.ToString(),
            UserAgent = http.Request.Headers.UserAgent.ToString()
        }, ct);

        logger.LogInformation("Landing lead created. requestId={RequestId} leadCode={LeadCode} elapsedMs={ElapsedMs}",
            requestId, leadCode, Stopwatch.GetElapsedTime(started).TotalMilliseconds);

        return Results.Ok(Success(leadCode));
    }
    catch (LandingSchemaUnavailableException)
    {
        logger.LogWarning("Landing lead database unavailable. requestId={RequestId}", requestId);
        return Results.Problem(
            title: "Landing contact service is temporarily unavailable.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Landing lead insert failed. requestId={RequestId}", requestId);
        return Results.Problem(
            title: "Unable to receive contact lead.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}).RequireRateLimiting("contact-leads");

app.MapFallbackToFile("index.html");
app.Run();
