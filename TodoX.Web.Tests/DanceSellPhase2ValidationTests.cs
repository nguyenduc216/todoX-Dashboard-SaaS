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
    public void ReferenceRuntimeMigration_AddsOnlyCurrentRuntimeOperationContracts()
    {
        var root = FindRepoRoot();
        var sql = File.ReadAllText(Path.Combine(root, "database/manual/rdance-fashion/02_fix_reference_runtime_contract.sql"));

        Assert.Contains("CREATE TABLE IF NOT EXISTS dance_sell.dance_sell_provider_operations", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE TABLE IF NOT EXISTS public.todox_ai_operation_assets", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dance_sell_provider_operations_attempt_idx", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("todox_ai_operation_assets_unique_idx", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("provider_capability_id uuid NULL", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("provider_account_id uuid NULL", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("timelapse", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AutoReferenceService_IsBackendSafeAndInvalidatesChangedSources()
    {
        var root = FindRepoRoot();
        var service = File.ReadAllText(Path.Combine(root, "TodoX.Web/Services/DanceSell/DanceSellPhase2Services.cs"));
        var repository = File.ReadAllText(Path.Combine(root, "TodoX.Web/Services/DanceSell/DanceSellRepository.cs"));

        Assert.Contains("Task<DanceSellJobDto> AutoPrepareAsync", service, StringComparison.Ordinal);
        Assert.Contains("ReferenceLocks.GetOrAdd", service, StringComparison.Ordinal);
        Assert.Contains("ReferenceVersionMatches", service, StringComparison.Ordinal);
        Assert.Contains("IsCurrentSourceAsync", service, StringComparison.Ordinal);
        Assert.Contains("RecoverStaleGenerationAsync", service, StringComparison.Ordinal);
        Assert.Contains("StaleReferenceGenerationThreshold", service, StringComparison.Ordinal);
        Assert.Contains("PrepareCharacterReferenceAsync", service, StringComparison.Ordinal);
        Assert.Contains("await GenerateAsync(job.Id, user, ct)", service, StringComparison.Ordinal);
        Assert.Contains("Status = DanceSellReferenceStatuses.Ready", service, StringComparison.Ordinal);
        Assert.DoesNotContain("Status = DanceSellReferenceStatuses.Approved", GetMethodSection(service, "PrepareCharacterReferenceAsync"), StringComparison.Ordinal);
        Assert.Contains("await _repo.ResetReferenceAsync(job.Id", service, StringComparison.Ordinal);
        Assert.Contains("prepared_reference_media_id=NULL", repository, StringComparison.Ordinal);
        Assert.Contains("prepared_reference_approved_at=NULL", repository, StringComparison.Ordinal);
        Assert.Contains("reference_approved_at=NULL", repository, StringComparison.Ordinal);
    }

    [Fact]
    public void ReferenceGenerateAsync_CatchesAllPostGeneratingSetupStages()
    {
        var root = FindRepoRoot();
        var service = File.ReadAllText(Path.Combine(root, "TodoX.Web/Services/DanceSell/DanceSellPhase2Services.cs"));
        var generate = GetMethodSection(service, "GenerateAsync");

        foreach (var stage in new[]
        {
            "\"resolve_route\"",
            "\"estimate_cost\"",
            "\"next_attempt\"",
            "\"create_operation\"",
            "\"read_character\"",
            "\"read_product\"",
            "\"composite\"",
            "\"save_media\""
        })
        {
            Assert.Contains(stage, generate, StringComparison.Ordinal);
        }

        Assert.Contains("await _repo.UpdateReferenceStatusAsync(job.Id, DanceSellReferenceStatuses.Generating", generate, StringComparison.Ordinal);
        Assert.Contains("catch (Exception ex)", generate, StringComparison.Ordinal);
        Assert.Contains("DanceSellReferenceStatuses.Failed", generate, StringComparison.Ordinal);
        Assert.Contains("DanceSell optional reference operation metadata failed", service, StringComparison.Ordinal);
        Assert.Contains("TryCreateFailedReferenceVersionAsync", service, StringComparison.Ordinal);
    }

    [Fact]
    public void StaleGeneratingRecovery_RequiresNoActiveVersionOrOperationBeforeRetry()
    {
        var root = FindRepoRoot();
        var service = File.ReadAllText(Path.Combine(root, "TodoX.Web/Services/DanceSell/DanceSellPhase2Services.cs"));
        var recovery = GetMethodSection(service, "RecoverStaleGenerationAsync");
        var operations = File.ReadAllText(Path.Combine(root, "TodoX.Web/Services/DanceSell/DanceSellAiOperations.cs"));

        Assert.Contains("job.PreparedReferenceStatus != DanceSellReferenceStatuses.Generating", recovery, StringComparison.Ordinal);
        Assert.Contains("StaleReferenceGenerationThreshold", recovery, StringComparison.Ordinal);
        Assert.Contains("hasActiveVersion", recovery, StringComparison.Ordinal);
        Assert.Contains("HasActiveOperationAsync", recovery, StringComparison.Ordinal);
        Assert.Contains("DanceSellReferenceStatuses.Failed", recovery, StringComparison.Ordinal);
        Assert.Contains("stale_generation", recovery, StringComparison.Ordinal);
        Assert.Contains("Task<bool> HasActiveOperationAsync", operations, StringComparison.Ordinal);
        Assert.Contains("status IN ('queued','submitted','generating')", operations, StringComparison.Ordinal);
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

    private static string GetMethodSection(string source, string methodName)
    {
        var start = source.IndexOf($"public async Task<DanceSellReferenceVersionDto> {methodName}(", StringComparison.Ordinal);
        if (start < 0)
        {
            start = source.IndexOf($"public async Task<DanceSellJobDto> {methodName}(", StringComparison.Ordinal);
        }

        if (start < 0)
        {
            start = source.IndexOf($"private async Task<bool> {methodName}(", StringComparison.Ordinal);
        }

        if (start < 0)
        {
            start = source.IndexOf($"private async Task<DanceSellJobDto> {methodName}(", StringComparison.Ordinal);
        }

        if (start < 0)
        {
            start = source.IndexOf($"private async Task {methodName}(", StringComparison.Ordinal);
        }

        Assert.True(start >= 0, $"Could not locate {methodName}.");
        var nextMethod = source.IndexOf("\n    private ", start + methodName.Length, StringComparison.Ordinal);
        return nextMethod > start ? source[start..nextMethod] : source[start..];
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
