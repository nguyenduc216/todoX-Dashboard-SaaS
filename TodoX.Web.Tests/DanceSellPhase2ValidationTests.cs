using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using TodoX.Web.Services.DanceSell;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class DanceSellPhase2ValidationTests
{
    [Theory]
    [InlineData("https://www.tiktok.com/@todo/video/123")]
    [InlineData("https://tiktok.com/@todo/video/123")]
    [InlineData("https://vm.tiktok.com/abc")]
    [InlineData("https://vt.tiktok.com/abc")]
    public void MotionSource_AllowsOnlyKnownTikTokHosts(string url)
    {
        var service = CreateMotionService();
        Assert.True(service.IsValidTikTokUrl(url));
    }

    [Theory]
    [InlineData("https://example.com/video/123")]
    [InlineData("http://www.tiktok.com/@todo/video/123")]
    [InlineData("https://evil-tiktok.com/@todo/video/123")]
    public void MotionSource_RejectsInvalidTikTokHosts(string url)
    {
        var service = CreateMotionService();
        Assert.False(service.IsValidTikTokUrl(url));
    }

    [Fact]
    public void ProviderUrl_RequiresAbsoluteHttps()
    {
        var service = CreateMotionService(new Dictionary<string, string?>
        {
            ["TodoX:PublicBaseUrl"] = "https://dashboard.example"
        });

        Assert.Equal("https://dashboard.example/uploads/a.mp4", service.ToProviderUrl("/uploads/a.mp4"));
        Assert.Throws<InvalidOperationException>(() => service.ToProviderUrl("http://cdn.example/a.mp4"));
    }

    [Fact]
    public void Phase2ManualSql_ExtendsStatusAndCreatesReferenceVersions()
    {
        var root = FindRepoRoot();
        var extendSql = File.ReadAllText(Path.Combine(root, "database/manual/kie-dance-sell-phase2/01_extend_dance_sell_jobs.sql"));
        var versionsSql = File.ReadAllText(Path.Combine(root, "database/manual/kie-dance-sell-phase2/02_create_reference_versions.sql"));

        Assert.Contains("'draft'", extendSql);
        Assert.Contains("prepared_reference_status", extendSql);
        Assert.Contains("dance_sell_reference_versions", versionsSql);
        Assert.Contains("dance_sell_reference_versions_one_selected_uk", versionsSql);
    }

    [Fact]
    public void JsonBusinessRequest_DoesNotExposeProviderSecretsOrArbitraryProviderUrl()
    {
        var properties = typeof(DanceSellJsonBusinessRequest).GetProperties().Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.DoesNotContain("ApiKey", properties);
        Assert.DoesNotContain("ProviderSecret", properties);
        Assert.DoesNotContain("ProviderUrl", properties);
        Assert.Contains("CharacterMediaId", properties);
        Assert.Contains("MotionVideoMediaId", properties);
    }

    private static DanceSellMotionSourceService CreateMotionService(Dictionary<string, string?>? values = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(values ?? new Dictionary<string, string?>())
            .Build();

        return new DanceSellMotionSourceService(
            media: null!,
            tikwm: null!,
            tenant: null!,
            config,
            OptionsMonitor(new DanceSellPhase2Options()));
    }

    private static IOptionsMonitor<T> OptionsMonitor<T>(T value)
        where T : class
        => new StaticOptionsMonitor<T>(value);

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "database")) && Directory.Exists(Path.Combine(dir.FullName, "TodoX.Web")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }

    private sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
        where T : class
    {
        public StaticOptionsMonitor(T value) => CurrentValue = value;
        public T CurrentValue { get; }
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
