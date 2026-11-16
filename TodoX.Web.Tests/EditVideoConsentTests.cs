using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using TodoX.Web.Data;
using TodoX.Web.Services;
using TodoX.Web.Services.EditVideoTool;
using Xunit;

namespace TodoX.Web.Tests;

/// <summary>Every paid EditVideo command needs the person's OK on the exact TodoX price, once.</summary>
public sealed class EditVideoConsentTests
{
    private static EditVideoConsentService Service(string? appVersion, IMemoryCache cache)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:TodoXSaaS"] = "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=1"
        }).Build();
        var factory = new TodoXConnectionFactory(config);
        var http = new DefaultHttpContext();
        if (appVersion is not null)
        {
            http.Request.Headers[EditVideoAppVersionPolicy.HeaderName] = appVersion;
        }

        return new EditVideoConsentService(factory, new TenantContext(factory, config), cache, null, new HttpContextAccessor { HttpContext = http });
    }

    private static readonly EditVideoLicenseRow License = new() { Id = Guid.NewGuid(), CustomerId = Guid.NewGuid() };
    private static readonly EditVideoDeviceRow Device = new() { Id = Guid.NewGuid() };

    [Fact]
    public async Task NewAppMustConfirmTheExactPriceOnce()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var service = Service("1.1.3", cache);
        var fp = EditVideoConsentService.Fingerprint("video", "veo_omni", "720p", "8", "prompt");

        var (ticket, quote) = await service.CheckAsync(License, Device, "video", "veo_omni", "Video", fp, 4.8m, 1000m, 50m, null, null);
        Assert.Null(ticket);
        Assert.NotNull(quote);
        Assert.Equal(4800m, quote!.Vnd);

        // A different request or a changed price cannot use that quote.
        var other = await service.CheckAsync(License, Device, "video", "veo_omni", "Video", fp + "x", 4.8m, 1000m, 50m, quote.QuoteId, null);
        Assert.Null(other.Ticket);
        var (ok, again) = await service.CheckAsync(License, Device, "video", "veo_omni", "Video", fp, 4.8m, 1000m, 50m, quote.QuoteId,
            new EditVideoConsentInfo("desktop_dialog", "PC-user", "1.1.3"));
        // The mismatching attempt above did not consume the quote; this one does.
        Assert.NotNull(ok);
        Assert.Null(again);
        Assert.Equal(quote.QuoteId, ok!.Quote.QuoteId);

        // Single use.
        var reused = await service.CheckAsync(License, Device, "video", "veo_omni", "Video", fp, 4.8m, 1000m, 50m, quote.QuoteId, null);
        Assert.Null(reused.Ticket);
        Assert.NotNull(reused.Quote);
    }

    [Fact]
    public async Task OldAppsAreOnlyAskedWhenTheAdminSaysSo()
    {
        var (ticket, quote) = await Service("1.1.2", new MemoryCache(new MemoryCacheOptions()))
            .CheckAsync(License, Device, "image", "m", "Ảnh", "fp", 1m, 1000m, 5m, null, null);
        Assert.NotNull(ticket);
        Assert.Null(quote);
        Assert.Equal("", ticket!.Quote.QuoteId);
        Assert.NotEqual(EditVideoConsentService.Fingerprint("a", 1), EditVideoConsentService.Fingerprint("a", 2));
        Assert.Equal(409, EditVideoEndpoints.StatusFor(EditVideoConsentService.ConfirmRequired));
    }
}
