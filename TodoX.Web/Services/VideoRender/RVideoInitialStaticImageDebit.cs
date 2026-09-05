using TodoX.Web.Services;

namespace TodoX.Web.Services.VideoRender;

public static class RVideoInitialStaticImageDebit
{
    public static int ResolveStaticDirectSceneCount(int estimatedImageCount, int aiImageWorkSceneCount)
        => Math.Max(0, estimatedImageCount - Math.Max(0, aiImageWorkSceneCount));

    public static int ResolveStaticDirectSceneCount(
        int estimatedImageCount,
        IReadOnlyList<RVideoEffectiveSceneImageSource> imageSources)
    {
        var staticDirectScenes = imageSources.Count(IsStaticDirectInput);
        return Math.Min(Math.Max(0, estimatedImageCount), staticDirectScenes);
    }

    public static decimal ResolveStaticDirectPoints(decimal imageRate, int staticDirectSceneCount)
        => Math.Max(0m, imageRate) * Math.Max(0, staticDirectSceneCount);

    public static Guid BuildReferenceId(Guid billingOperationId)
        => PointBillingReference.ForOperation(
            billingOperationId,
            "rvideo_static_image",
            "initial_static_direct",
            PointBillingIntent.InitialRender,
            billingOperationId);

    private static bool IsStaticDirectInput(RVideoEffectiveSceneImageSource source)
        => source.HasUsableInput
           && source.SelectedImageVersionId is null;
}
