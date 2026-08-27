using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TodoX.Web.Services.AiProviders;
using TodoX.Web.Services.DanceSell;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class RDanceReferencePromptRegressionTests
{
    [Fact]
    public void Ai79ProviderUsesRequestPromptAndKeepsSingleImageContract()
    {
        var source = ReadRepoFile("Services", "DanceSell", "DanceSellAiOperations.cs");
        var submit = source[source.IndexOf("public async Task<ProviderTaskSubmitResult> SubmitAsync", StringComparison.Ordinal)..];

        Assert.Contains("var prompt = string.IsNullOrWhiteSpace(request.Prompt)", submit);
        Assert.Contains("prompt,", submit);
        Assert.Contains("num_outputs = 1", submit);
        Assert.Contains("exactly ONE final image", submit);
        Assert.Contains("Do NOT create a collage", submit);
        Assert.Contains("Do NOT create a triptych", submit);
        Assert.Contains("Do NOT create multiple panels", submit);
        Assert.Contains("Do NOT duplicate the person", submit);
        Assert.DoesNotContain("prompt = BuildReferencePrompt(hasProduct)", submit);
    }

    [Fact]
    public void DanceSellServicePrefersPersistedImagePromptAndRegeneratesVersions()
    {
        var service = ReadRepoFile("Services", "DanceSell", "DanceSellPhase2Services.cs");
        var page = ReadRepoFile("Components", "Pages", "RDanceJobDetail.razor");

        Assert.Contains("!string.IsNullOrWhiteSpace(job.ImagePrompt)", service);
        Assert.Contains("Prompt = BuildReferencePrompt(job)", service);
        Assert.Contains("ImagePrompt = prompt", page);
        Assert.Contains("References.GenerateAsync(_job!.Id", page);
        Assert.Contains("VersionNo = versionNo", service);
        Assert.Contains("Prompt = BuildReferencePrompt(job)", service);
    }

    [Fact]
    public void ReferenceGenerationUsesSelectedRatioAndDoesNotHardCodeSixteenByNine()
    {
        var service = ReadRepoFile("Services", "DanceSell", "DanceSellPhase2Services.cs");

        Assert.Contains("var targetRatio = DanceSellRatioNormalizer.NormalizeDanceSellRatio(job.Ratio);", service);
        Assert.Contains("ratio = targetRatio", service);
        Assert.Contains("AspectRatio = targetRatio", service);
        Assert.DoesNotContain("ratio = \"16:9\"", service);
    }

    [Fact]
    public async Task Ai79ReferenceProviderSubmitsPortraitRatioFromRequest()
    {
        var (result, client) = await SubmitReferenceAsync("9:16");

        Assert.NotNull(client.LastSubmit);
        Assert.Equal("9:16", client.LastSubmit!.Options["ratio"]);
        Assert.Equal("9:16", ReadString(result.RequestJson, "ratio"));
        Assert.Contains("portrait 9:16", client.LastSubmit.Prompt);
    }

    [Fact]
    public async Task Ai79ReferenceProviderSubmitsLandscapeRatioFromRequest()
    {
        var (result, client) = await SubmitReferenceAsync("16:9");

        Assert.NotNull(client.LastSubmit);
        Assert.Equal("16:9", client.LastSubmit!.Options["ratio"]);
        Assert.Equal("16:9", ReadString(result.RequestJson, "ratio"));
        Assert.Contains("landscape 16:9", client.LastSubmit.Prompt);
    }

    [Fact]
    public void KlingMotionThreeForcesDefaultProviderRatioForAnyProjectRatio()
    {
        var route = new DanceSellProviderRouteDto { ModelName = DanceSellConstants.Model };

        Assert.Equal("default", DanceSellMotionProviderContract.ResolveProviderRatio(route, "9:16"));
        Assert.Equal("default", DanceSellMotionProviderContract.ResolveProviderRatio(route, "16:9"));
    }

    [Fact]
    public void ManualRetryCreatesFreshMotionAttemptAndRebindsRenderInput()
    {
        var service = ReadRepoFile("Services", "DanceSell", "DanceSellPhase2Services.cs");
        var render = ReadRepoFile("Services", "Render", "RenderJobService.cs");

        Assert.Contains("GetLatestOperationAsync(job.Id, DanceSellOperationTypes.MotionVideo", service);
        Assert.Contains("ParentOperationId = previousOperation?.Id", service);
        Assert.Contains("AttemptNo = attemptNo", service);
        Assert.Contains("OperationId = operation.Id", service);
        Assert.Contains("inputOverride ?? JsonSerializer.Deserialize<object>(current.InputJson)", render);
        Assert.Contains("await _operations.BeginMotionSubmitAttemptAsync(motionOperationId, requestJson, ct)", ReadRepoFile("Services", "DanceSell", "DanceSellRenderHandler.cs"));
    }

    [Fact]
    public void ManualRetryReusesOnlyVerifiedAssetsWithCurrentMediaIdentity()
    {
        var repository = ReadRepoFile("Services", "DanceSell", "DanceSellAiOperations.cs");
        var handler = ReadRepoFile("Services", "DanceSell", "DanceSellRenderHandler.cs");

        Assert.Contains("verificationMatched', 'true'", repository);
        Assert.Contains("await _operations.GetLatestAssetAsync(", handler);
        Assert.Contains("CloneProviderAsset", handler);
        Assert.Contains("danceJob.PreparedReferenceMediaId", handler);
        Assert.Contains("danceJob.MotionVideoMediaId", handler);
    }

    [Fact]
    public void NonKlingMotionKeepsSelectedProjectRatio()
    {
        var route = new DanceSellProviderRouteDto { ModelName = "other_motion_model" };

        Assert.Equal("9:16", DanceSellMotionProviderContract.ResolveProviderRatio(route, "9:16"));
        Assert.Equal("16:9", DanceSellMotionProviderContract.ResolveProviderRatio(route, "16:9"));
    }

    [Fact]
    public void ReferenceRegenerationAndSelectionAreRatioAware()
    {
        var service = ReadRepoFile("Services", "DanceSell", "DanceSellPhase2Services.cs");

        Assert.Contains("ratioChanged", service);
        Assert.Contains("ResetReferenceAsync(job.Id", service);
        Assert.Contains("ReadRequestRatio(version.RequestJson)", service);
        Assert.Contains("EnsureReferenceVersionRatioMatchesJob(version, job)", service);
        Assert.Contains("DANCE_SELL_REFERENCE_RATIO_MISMATCH", service);
        Assert.Contains("BuildComparisonRequestJson(job, candidate, prompt, route, started, targetRatio)", service);
    }

    [Fact]
    public void ProviderSubmitErrorUses79AiMessageAndRedactsSensitiveDetail()
    {
        var method = typeof(DanceSellRenderHandler).GetMethod(
            "GetCustomerSafeProviderMessage",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        var message = Assert.IsType<string>(method!.Invoke(null, ["submit", "79AI rejected ratio=9:16; access_token=test-token"]));

        Assert.Contains("79AI", message);
        Assert.Contains("Chi tiết:", message);
        Assert.DoesNotContain("chuẩn bị video nguồn", message);
        Assert.DoesNotContain("test-token", message);
    }

    [Fact]
    public void ReferencePromptDialogExposesSaveAndRegenerateActions()
    {
        var dialog = ReadRepoFile("Components", "Dialogs", "RDanceReferencePromptDialog.razor");

        Assert.Contains("Lưu prompt", dialog);
        Assert.Contains("Tạo lại ảnh", dialog);
        Assert.Contains("new RDanceReferencePromptDialogResult", dialog);
    }

    private static string ReadRepoFile(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { AppContext.BaseDirectory, "..", "..", "..", ".." }.Concat(parts).ToArray()));

    private static async Task<(ProviderTaskSubmitResult Result, CapturingAi79TaskClient Client)> SubmitReferenceAsync(string ratio)
    {
        var client = new CapturingAi79TaskClient();
        var provider = new Ai79DanceSellReferenceProvider(
            client,
            new StaticCredentialResolver(),
            NullLogger<Ai79DanceSellReferenceProvider>.Instance);

        var result = await provider.SubmitAsync(new DanceSellReferenceProviderRequest
        {
            Route = new DanceSellProviderRouteDto
            {
                ProviderCode = DanceSellConstants.ProviderCode,
                ModelName = DanceSellConstants.Ai79ReferenceModel
            },
            Prompt = "Keep one final image.",
            CharacterImageUrl = "https://example.test/person.jpg",
            ProductImageUrl = "https://example.test/product.jpg",
            AspectRatio = ratio
        }, CancellationToken.None);

        return (result, client);
    }

    private static string? ReadString(string json, string propertyName)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty(propertyName, out var value)
            ? value.GetString()
            : null;
    }

    private sealed class StaticCredentialResolver : IProviderCredentialResolver
    {
        public Task<ResolvedProviderCredential> ResolveAsync(string providerCode, string credentialRole, CancellationToken ct = default)
            => Task.FromResult(new ResolvedProviderCredential
            {
                ProviderAccountId = Guid.NewGuid(),
                ProviderCode = providerCode,
                CredentialRole = credentialRole,
                Secret = "test-token"
            });
    }

    private sealed class CapturingAi79TaskClient : IAi79TaskClient
    {
        public Ai79TaskSubmitRequest? LastSubmit { get; private set; }

        public Task<Ai79TaskSubmitResult> SubmitAsync(Ai79TaskSubmitRequest request, CancellationToken ct = default)
        {
            LastSubmit = request;
            return Task.FromResult(new Ai79TaskSubmitResult("task-123", """{"id":"task-123"}"""));
        }

        public Task<Ai79TaskSubmitResult> SubmitMultipartAsync(Ai79MultipartTaskSubmitRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<Ai79MediaUploadResult> UploadMediaAsync(Ai79MediaUploadRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<Ai79ProviderMediaListResult> ListImagesAsync(Ai79ProviderMediaListRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<Ai79ProviderMediaListResult> ListVideosAsync(Ai79ProviderMediaListRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<Ai79TaskSubmitResult> SubmitMotionControlAsync(Ai79MotionControlSubmitRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<Ai79ImageUploadResult> UploadImageAsync(Ai79ImageUploadRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<Ai79TaskStatusResult> GetStatusAsync(Ai79TaskStatusRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
