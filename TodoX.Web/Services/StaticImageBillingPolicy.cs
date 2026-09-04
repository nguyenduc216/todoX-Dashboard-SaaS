using TodoX.Web.Models.Catalog;
using TodoX.Web.Models.Timelapse;
using TodoX.Web.Services.DanceSell;

namespace TodoX.Web.Services;

public static class StaticImageBillingPolicy
{
    public static int ResolveRdanceStaticInputCount(DanceSellJobDto job)
        => CountDistinctIfPresent(job.CharacterMediaId, job.ProductMediaId, job.DirectReferenceMediaId);

    public static int ResolveTimelapseStaticInputCount(TimelapseJobSnapshot snapshot)
        => CountIfPresent(snapshot.OriginalImage.MediaId, snapshot.OriginalImage.PublicUrl, snapshot.OriginalImage.ObjectKey)
           + (snapshot.StartImage is null
               ? 0
               : CountIfPresent(snapshot.StartImage.MediaId, snapshot.StartImage.PublicUrl, snapshot.StartImage.ObjectKey));

    public static int ResolveBillableStaticImageCount(int staticInputCount, bool chargeStaticImagePoints)
        => chargeStaticImagePoints ? Math.Max(0, staticInputCount) : 0;

    private static int CountIfPresent(Guid? mediaId, params string?[] values)
        => mediaId is Guid id && id != Guid.Empty && values.Any(value => !string.IsNullOrWhiteSpace(value)) ? 1 : 0;

    private static int CountDistinctIfPresent(params Guid?[] mediaIds)
        => mediaIds.Select(mediaId => mediaId.GetValueOrDefault())
            .Where(mediaId => mediaId != Guid.Empty)
            .Distinct()
            .Count();
}
