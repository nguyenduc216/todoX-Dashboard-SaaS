using Xunit;

namespace TodoX.Web.Tests;

public sealed class RDanceStagedBillingRegressionTests
{
    [Fact]
    public void ReferenceGenerationChargesImageBeforeProviderSubmission()
    {
        var source = ReadRepoFile("Services", "DanceSell", "DanceSellPhase2Services.cs");
        var generate = source[source.IndexOf("public async Task<DanceSellReferenceVersionDto> GenerateAsync", StringComparison.Ordinal)..];
        var charge = generate.IndexOf("_wallets.ChargeAsync", StringComparison.Ordinal);
        var submit = generate.IndexOf("provider.SubmitAsync", StringComparison.Ordinal);

        Assert.True(charge >= 0 && charge < submit);
        Assert.Contains("imageCount", generate);
        Assert.Contains("INSUFFICIENT_POINTS", generate);
        Assert.Contains("dance_sell_reference_image", generate);
    }

    [Fact]
    public void QueueRenderSubtractsAlreadyChargedReferenceImage()
    {
        var source = ReadRepoFile("Services", "DanceSell", "DanceSellPhase2Services.cs");
        var queue = source[source.IndexOf("public async Task<DanceSellJobDto> QueueRenderAsync", StringComparison.Ordinal)..];

        Assert.Contains("alreadyChargedImage", queue);
        Assert.Contains("remainingPoints", queue);
        Assert.Contains("logicalTotalPoints", queue);
        Assert.Contains("total_planned_points = logicalTotalPoints", queue);
        Assert.Contains("PointCostEstimate = logicalTotalPoints", queue);
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
    public void SceneVideoRetryIsSystemRetryPathAndHasNoUserRerenderBilling()
    {
        var page = ReadRepoFile("Components", "Pages", "RenderVideoJobs.razor");
        var handler = ReadRepoFile("Services", "VideoRender", "SceneVideoRenderHandler.cs");

        Assert.Contains("OnRetry=\"@(() => EnqueueSceneVideoAsync(scene))\"", page);
        Assert.DoesNotContain("USER_RERENDER", handler);
        Assert.DoesNotContain("user_rerender", handler, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadRepoFile(params string[] parts)
        => File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "TodoX.Web",
            Path.Combine(parts)));
}
