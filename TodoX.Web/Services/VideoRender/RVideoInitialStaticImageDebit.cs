using TodoX.Web.Services;

namespace TodoX.Web.Services.VideoRender;

public static class RVideoInitialStaticImageDebit
{
    public static int ResolveStaticDirectSceneCount(
        bool chargeStaticImagePoints,
        int staticDirectSceneCount)
        => chargeStaticImagePoints
            ? Math.Max(0, staticDirectSceneCount)
            : 0;

    public static int ResolveStaticDirectSceneCount(
        bool chargeStaticImagePoints,
        IReadOnlyList<RVideoEffectiveSceneImageSource> imageSources)
    {
        if (!chargeStaticImagePoints)
        {
            return 0;
        }

        return imageSources.Count(IsStaticDirectInput);
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
