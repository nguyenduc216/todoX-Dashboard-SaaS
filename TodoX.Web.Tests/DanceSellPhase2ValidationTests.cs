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
        Assert.Contains("PollGeneratingReferenceAsync", service, StringComparison.Ordinal);
        Assert.Contains("BuildReferencePrompt", service, StringComparison.Ordinal);
        Assert.Contains("await GenerateAsync(job.Id, user, ct)", service, StringComparison.Ordinal);
        Assert.Contains("Status = DanceSellReferenceStatuses.Ready", service, StringComparison.Ordinal);
        Assert.DoesNotContain("Status = DanceSellReferenceStatuses.Approved", GetMethodSection(service, "PrepareCharacterReferenceAsync"), StringComparison.Ordinal);
        Assert.Contains("await _repo.ResetReferenceAsync(job.Id", service, StringComparison.Ordinal);
        Assert.Contains("prepared_reference_media_id=NULL", repository, StringComparison.Ordinal);
        Assert.Contains("prepared_reference_approved_at=NULL", repository, StringComparison.Ordinal);
        Assert.Contains("reference_approved_at=NULL", repository, StringComparison.Ordinal);
        Assert.Contains("SET is_selected = false", GetMethodSection(repository, "ResetReferenceAsync"), StringComparison.Ordinal);
        Assert.Contains("await _repo.ResetReferenceAsync(job.Id, ct: ct)", GetMethodSection(service, "UploadCharacterAsync"), StringComparison.Ordinal);
        Assert.Contains("await _repo.ResetReferenceAsync(job.Id, ct: ct)", GetMethodSection(service, "UploadProductAsync"), StringComparison.Ordinal);
    }

    [Fact]
    public void ReferenceApprovalCanBeRemovedWithoutDeletingReferenceHistory()
    {
        var root = FindRepoRoot();
        var service = File.ReadAllText(Path.Combine(root, "TodoX.Web/Services/DanceSell/DanceSellPhase2Services.cs"));
        var endpoints = File.ReadAllText(Path.Combine(root, "TodoX.Web/Services/DanceSell/DanceSellPhase2Endpoints.cs"));

        var unapprove = GetMethodSection(service, "UnapproveAsync");
        Assert.Contains("RequireOwnedJobAsync", unapprove, StringComparison.Ordinal);
        Assert.Contains("DanceSellReferenceStatuses.Approved", unapprove, StringComparison.Ordinal);
        Assert.Contains("await _repo.UnapproveReferenceAsync(job.Id, ct)", unapprove, StringComparison.Ordinal);
        Assert.Contains("/jobs/{id:guid}/reference/unapprove", endpoints, StringComparison.Ordinal);
        Assert.Contains("service.UnapproveAsync(id, user, ct)", endpoints, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderRequiresApprovedReferenceAfterSourceInvalidation()
    {
        var root = FindRepoRoot();
        var service = File.ReadAllText(Path.Combine(root, "TodoX.Web/Services/DanceSell/DanceSellPhase2Services.cs"));
        var validation = GetMethodSection(service, "ValidateReadyForRender");

        Assert.Contains("job.PreparedReferenceStatus != DanceSellReferenceStatuses.Approved", validation, StringComparison.Ordinal);
        Assert.Contains("DANCE_SELL_REFERENCE_NOT_APPROVED", validation, StringComparison.Ordinal);
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
            "\"provider_submit\"",
            "\"estimate_cost\"",
            "\"next_attempt\"",
            "\"create_operation\"",
        })
        {
            Assert.Contains(stage, generate, StringComparison.Ordinal);
        }

        Assert.Contains("await _repo.UpdateReferenceStatusAsync(job.Id, DanceSellReferenceStatuses.Generating", generate, StringComparison.Ordinal);
        Assert.Contains("catch (Exception ex)", generate, StringComparison.Ordinal);
        Assert.Contains("DanceSellReferenceStatuses.Failed", generate, StringComparison.Ordinal);
        Assert.Contains("DANCE_SELL_REFERENCE_AI_ROUTE_REQUIRED", generate, StringComparison.Ordinal);
        Assert.Contains("CharacterMediaId = job.CharacterMediaId", generate, StringComparison.Ordinal);
        Assert.Contains("ProductMediaId = job.ProductMediaId", generate, StringComparison.Ordinal);
        Assert.Contains("RequestJson = submitted.RequestJson", generate, StringComparison.Ordinal);
        Assert.Contains("ResponseJson = DanceSellRepository.ToJson(new { submitted.TaskId, submitted.ResponseJson })", generate, StringComparison.Ordinal);
        Assert.Contains("CompleteReferenceVersionAsync", service, StringComparison.Ordinal);
        Assert.Contains("FailReferenceVersionAsync", service, StringComparison.Ordinal);
    }

    [Fact]
    public void ReferencePrompt_ForcesTryOnSemanticsAndBansCollageOutput()
    {
        var root = FindRepoRoot();
        var service = File.ReadAllText(Path.Combine(root, "TodoX.Web/Services/DanceSell/DanceSellPhase2Services.cs"));
        var prompt = GetMethodSection(service, "BuildReferencePrompt");
        var generate = GetMethodSection(service, "GenerateAsync");

        Assert.Contains("Create ONE single photorealistic fashion try-on image.", prompt, StringComparison.Ordinal);
        Assert.Contains("Preserve the same person from the character reference image.", prompt, StringComparison.Ordinal);
        Assert.Contains("Preserve the same face, hairstyle, expression, skin tone, body proportions, pose direction, hand position, and overall studio or background feel as much as possible.", prompt, StringComparison.Ordinal);
        Assert.Contains("The model MUST WEAR the exact clothing/product shown in the product image.", prompt, StringComparison.Ordinal);
        Assert.Contains("The final outfit must visually read as the same outfit from the product image, not a similar one.", prompt, StringComparison.Ordinal);
        Assert.Contains("same person", prompt, StringComparison.Ordinal);
        Assert.Contains("face", prompt, StringComparison.Ordinal);
        Assert.Contains("hairstyle", prompt, StringComparison.Ordinal);
        Assert.Contains("body proportions", prompt, StringComparison.Ordinal);
        Assert.Contains("A human viewer should immediately recognize that the model is wearing the exact outfit from the product image.", prompt, StringComparison.Ordinal);

        foreach (var blocked in new[]
        {
            "Do not create a collage.",
            "Do not create side-by-side images.",
            "Do not create a split-screen composition.",
            "Do not create an inset product thumbnail.",
            "Do not display the product separately.",
            "Do not invent a different outfit."
        })
        {
            Assert.Contains(blocked, prompt, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("BuildCompositeAsync", generate, StringComparison.Ordinal);
        Assert.Contains("DANCE_SELL_REFERENCE_AI_ROUTE_REQUIRED", generate, StringComparison.Ordinal);
        Assert.Contains("provider.SubmitAsync", generate, StringComparison.Ordinal);
    }

    [Fact]
    public void ReferenceComparison_IsAdminOnlyAndDoesNotTouchProductionApproval()
    {
        var root = FindRepoRoot();
        var models = File.ReadAllText(Path.Combine(root, "TodoX.Web/Services/DanceSell/DanceSellModels.cs"));
        var service = File.ReadAllText(Path.Combine(root, "TodoX.Web/Services/DanceSell/DanceSellPhase2Services.cs"));
        var endpoints = File.ReadAllText(Path.Combine(root, "TodoX.Web/Services/DanceSell/DanceSellPhase2Endpoints.cs"));
        var program = File.ReadAllText(Path.Combine(root, "TodoX.Web/Program.cs"));
        var comparison = service[service.IndexOf("public sealed class DanceSellReferenceComparisonService", StringComparison.Ordinal)..service.IndexOf("public interface IDanceSellPhase2Service", StringComparison.Ordinal)];

        foreach (var expected in new[]
        {
            "new DanceSellReferenceComparisonCandidate(\"79ai\", \"o1\", \"IMAGE O1 - Kling\")",
            "new DanceSellReferenceComparisonCandidate(\"79ai\", \"seedream_4_0\", \"Seedream 4.0\")",
            "new DanceSellReferenceComparisonCandidate(\"79ai\", \"google_image_gen_banana_pro\", \"Nano Banana Pro\")"
        })
        {
            Assert.Contains(expected, models, StringComparison.Ordinal);
        }

        Assert.Contains("IDanceSellReferenceComparisonService", service, StringComparison.Ordinal);
        Assert.Contains("EnsureAdmin(user)", comparison, StringComparison.Ordinal);
        Assert.Contains("/api/admin", endpoints, StringComparison.Ordinal);
        Assert.Contains("/dance-sell/jobs/{id:guid}/reference-comparison/run", endpoints, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IDanceSellReferenceComparisonService, DanceSellReferenceComparisonService>", program, StringComparison.Ordinal);
        Assert.Contains("var prompt = DanceSellReferenceImageService.BuildReferencePrompt(job)", comparison, StringComparison.Ordinal);
        Assert.Contains("Prompt = prompt", comparison, StringComparison.Ordinal);
        Assert.Contains("CharacterMediaId = job.CharacterMediaId", comparison, StringComparison.Ordinal);
        Assert.Contains("ProductMediaId = job.ProductMediaId", comparison, StringComparison.Ordinal);
        Assert.Contains("ratio = \"9:16\"", comparison, StringComparison.Ordinal);
        Assert.Contains("resolution = \"2k\"", comparison, StringComparison.Ordinal);
        Assert.Contains("IsSelected = false", comparison, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateReferenceStatusAsync", comparison, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectReferenceVersionAsync", comparison, StringComparison.Ordinal);
        Assert.DoesNotContain("ApproveAsync", comparison, StringComparison.Ordinal);
    }

    [Fact]
    public void ReferenceComparison_StoresIndependentResultsAndManualScores()
    {
        var root = FindRepoRoot();
        var service = File.ReadAllText(Path.Combine(root, "TodoX.Web/Services/DanceSell/DanceSellPhase2Services.cs"));
        var repository = File.ReadAllText(Path.Combine(root, "TodoX.Web/Services/DanceSell/DanceSellRepository.cs"));
        var page = File.ReadAllText(Path.Combine(root, "TodoX.Web/Components/Pages/RDanceJobDetail.razor"));
        var comparison = service[service.IndexOf("public sealed class DanceSellReferenceComparisonService", StringComparison.Ordinal)..service.IndexOf("public interface IDanceSellPhase2Service", StringComparison.Ordinal)];

        Assert.Contains("foreach (var candidate in DanceSellReferenceComparisonCandidates.All)", comparison, StringComparison.Ordinal);
        Assert.Contains("results.Add(new DanceSellReferenceComparisonResultDto", comparison, StringComparison.Ordinal);
        Assert.Contains("catch (Exception ex)", comparison, StringComparison.Ordinal);
        Assert.Contains("TryCreateFailedVersionAsync", comparison, StringComparison.Ordinal);
        Assert.Contains("operationId", comparison, StringComparison.Ordinal);
        Assert.Contains("TaskId", comparison, StringComparison.Ordinal);
        Assert.Contains("PollAsync(Guid jobId, Guid versionId", comparison, StringComparison.Ordinal);
        Assert.Contains("ScoreAsync(Guid jobId, Guid versionId", comparison, StringComparison.Ordinal);
        Assert.Contains("manualScore", repository, StringComparison.Ordinal);
        Assert.Contains("jsonb_set", repository, StringComparison.Ordinal);
        Assert.Contains("UpdateReferenceVersionScoreAsync", repository, StringComparison.Ordinal);
        Assert.Contains("CanRunReferenceComparison", page, StringComparison.Ordinal);
        Assert.Contains("DanceSellSecurity.IsAdmin", page, StringComparison.Ordinal);
        Assert.Contains("A/B reference model test", page, StringComparison.Ordinal);
        Assert.Contains("RunReferenceComparisonAsync", page, StringComparison.Ordinal);
        Assert.Contains("PollReferenceComparisonAsync", page, StringComparison.Ordinal);
        Assert.Contains("ScoreReferenceComparisonAsync", page, StringComparison.Ordinal);
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
        Assert.Contains("Task<DanceSellProviderOperationDto?> GetLatestActiveOperationAsync", operations, StringComparison.Ordinal);
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
            start = source.IndexOf($"public async Task {methodName}(", StringComparison.Ordinal);
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

        if (start < 0)
        {
            start = source.IndexOf($"private void {methodName}(", StringComparison.Ordinal);
        }

        if (start < 0)
        {
            start = source.IndexOf($"private static void {methodName}(", StringComparison.Ordinal);
        }

        if (start < 0)
        {
            start = source.IndexOf($"private static string {methodName}(", StringComparison.Ordinal);
        }

        if (start < 0)
        {
            start = source.IndexOf($"internal static string {methodName}(", StringComparison.Ordinal);
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
