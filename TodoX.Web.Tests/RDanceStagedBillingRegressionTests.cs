using Xunit;

namespace TodoX.Web.Tests;

public sealed class RDanceStagedBillingRegressionTests
{
    [Fact]
    public void ReferenceGenerationChargesImageBeforeProviderSubmission()
    {
        var source = ReadRepoFile("Services", "DanceSell", "DanceSellPhase2Services.cs");
        var generateStart = source.IndexOf("public async Task<DanceSellReferenceVersionDto> GenerateAsync", StringComparison.Ordinal);
        var generateEnd = source.IndexOf("public async Task<DanceSellJobDto> AutoPrepareAsync", StringComparison.Ordinal);
        var generate = source[generateStart..generateEnd];
        var charge = generate.IndexOf("_wallets.ChargeAsync", StringComparison.Ordinal);
        var submit = generate.IndexOf("provider.SubmitAsync", StringComparison.Ordinal);

        Assert.True(charge >= 0 && charge < submit);
        Assert.DoesNotContain("ResolveRdanceStaticInputCount", generate);
        Assert.DoesNotContain("ResolveBillableStaticImageCount", generate);
        Assert.Contains("pointEstimate.Image.Points", generate);
        Assert.Contains("INSUFFICIENT_POINTS", generate);
        Assert.Contains("dance_sell_reference_image", generate);
    }

    [Fact]
    public void QueueRenderUsesStaticInputCountWithoutReferenceChargeGating()
    {
        var source = ReadRepoFile("Services", "DanceSell", "DanceSellPhase2Services.cs");
        var queue = source[source.IndexOf("public async Task<DanceSellJobDto> QueueRenderAsync", StringComparison.Ordinal)..];
        var retry = source[source.IndexOf("public async Task<DanceSellJobDto> RetryAsync", StringComparison.Ordinal)..];

        Assert.Equal(1, queue.Split(new[] { "_wallets.ChargeAsync" }, StringSplitOptions.None).Length - 1);
        Assert.Contains("var chargeStaticImagePoints = await _tokenSettings.GetChargeStaticImagePointsAsync();", queue);
        Assert.Contains("var staticInputCount = StaticImageBillingPolicy.ResolveRdanceStaticInputCount(job);", queue);
        Assert.Contains("var billableStaticImageCount = StaticImageBillingPolicy.ResolveBillableStaticImageCount(staticInputCount, chargeStaticImagePoints);", queue);
        Assert.Contains("var isDirectReference = string.Equals(job.ReferenceMode, DanceSellReferenceModes.DirectReference, StringComparison.OrdinalIgnoreCase);", queue);
        Assert.Contains("var imageCount = isDirectReference ? billableStaticImageCount : 1;", queue);
        Assert.Contains("durationSeconds = await ResolveMotionDurationSecondsAsync(job, motionRoute, estimate, ct)", queue);
        Assert.Contains("new PointPricingEstimateRequest(", queue);
        Assert.Contains("durationSeconds,", queue);
        Assert.Contains("RequestJson = DanceSellRepository.ToJson(new { job.Id, job.PreparedReferenceUrl, job.MotionVideoUrl, job.Prompt, businessMode = job.Mode, providerMode, job.CharacterOrientation, job.Ratio, durationSeconds })", queue);
        Assert.Contains("await _operations.UpsertOperationAsync", queue);
        Assert.Contains("await _renderJobs.EnqueueAsync", queue);
        Assert.Contains("await _repo.QueueForRenderAsync", queue);
        Assert.Contains("RequestJson = DanceSellRepository.ToJson(new { job.Id, job.PreparedReferenceUrl, job.MotionVideoUrl, job.Prompt, businessMode = job.Mode, providerMode, job.CharacterOrientation, job.Ratio, durationSeconds = retryDurationSeconds })", retry);
        Assert.DoesNotContain("_wallets.ChargeAsync", retry);
        Assert.DoesNotContain("alreadyChargedImage", queue);
        Assert.DoesNotContain("referenceOperation", queue);
        Assert.Contains("imagePointsToChargeNow", queue);
        Assert.Contains("videoPointsToChargeNow", queue);
        Assert.Contains("voicePointsToChargeNow", queue);
        Assert.Contains("chargeNow", queue);
        Assert.Contains("logicalTotalPoints", queue);
        Assert.Contains("total_planned_points = pointEstimate.TotalPoints", queue);
        Assert.Contains("total_charged_points = chargeNow", queue);
        Assert.Contains("planned_points = pointEstimate.Image.Points", queue);
        Assert.Contains("charged_points = imagePointsToChargeNow", queue);
        Assert.Contains("planned_points = pointEstimate.Video.Points", queue);
        Assert.Contains("charged_points = videoPointsToChargeNow", queue);
        Assert.Contains("planned_points = pointEstimate.Voice.Points", queue);
        Assert.Contains("charged_points = voicePointsToChargeNow", queue);
        Assert.Contains("PointCostEstimate = chargeNow", queue);
        var detail = ReadRepoFile("Components", "Pages", "RDanceJobDetail.razor");
        Assert.DoesNotContain("DanceOperations.GetLatestOperationAsync(_job.Id, DanceSellOperationTypes.ReferenceImage", detail);
        Assert.Contains("var imageCount = string.Equals(_job.ReferenceMode, DanceSellReferenceModes.DirectReference, StringComparison.OrdinalIgnoreCase)", detail);
    }

    [Fact]
    public void ReferenceChargeUsesStableLogicalReferenceAndDirectReferenceStaysFree()
    {
        var source = ReadRepoFile("Services", "DanceSell", "DanceSellPhase2Services.cs");

        Assert.Contains("BuildReferenceChargeReference(job.Id, versionNo)", source);
        Assert.Contains("reference_image:initial_render:v", source);
        Assert.Contains("if (job.ReferenceMode == DanceSellReferenceModes.DirectReference)", source);
        Assert.Contains("DANCE_SELL_REFERENCE_GENERATION_NOT_REQUIRED", source);
    }

    [Fact]
    public void SceneVideoRetryUsesUserRerenderIntentAfterConfirmation()
    {
        var page = ReadRepoFile("Components", "Pages", "RenderVideoJobs.razor");
        var handler = ReadRepoFile("Services", "VideoRender", "SceneVideoRenderHandler.cs");

        Assert.Contains("OnRetry=\"@(() => RenderSceneVideoAsync(scene))\"", page);
        Assert.Contains("=> ConfirmAndRenderSceneVideoAsync(scene, PointBillingIntent.UserRerender);", page);
        Assert.Contains("BillingIntent = input.BillingIntent", handler);
    }

    [Fact]
    public void LegacyDurationResolution_UsesPersistedOrDerivableMotionMetadataOnly()
    {
        var source = ReadRepoFile("Services", "DanceSell", "DanceSellPhase2Services.cs");
        var resolver = source[source.IndexOf("private async Task<int> ResolveMotionDurationSecondsAsync", StringComparison.Ordinal)..];

        Assert.Contains("ReadInt(job.RequestJson, \"durationSeconds\"", resolver);
        Assert.Contains("ReadInt(route.ConfigJson, \"durationSeconds\"", resolver);
        Assert.Contains("job.MotionVideoMediaId is Guid mediaId", resolver);
        Assert.Contains("_repo.PersistMotionDurationAsync(job.Id, derived.Value, ct)", resolver);
        Assert.Contains("throw new InvalidOperationException(\"DANCE_SELL_VIDEO_DURATION_REQUIRED\")", resolver);
        Assert.DoesNotContain("EstimateAsync", resolver);
    }

    private static string ReadRepoFile(params string[] parts)
        => File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "TodoX.Web",
            Path.Combine(parts)));
}
