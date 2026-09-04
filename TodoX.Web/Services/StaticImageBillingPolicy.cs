using TodoX.Web.Models.Catalog;
using TodoX.Web.Models.Timelapse;
using TodoX.Web.Services.DanceSell;
using TodoX.Web.Services.Render;
using TodoX.Web.Services.VideoRender;

namespace TodoX.Web.Services;

public static class StaticImageBillingPolicy
{
    public static int ResolveRdanceStaticInputCount(DanceSellJobDto job)
        => CountDistinctInputs(
            BuildInputKey(job.CharacterMediaId, job.CharacterObjectKey, job.CharacterImageUrl),
            BuildInputKey(job.ProductMediaId, job.ProductObjectKey, job.ProductImageUrl),
            BuildInputKey(job.DirectReferenceMediaId, job.DirectReferenceObjectKey, job.DirectReferenceUrl));

    public static int ResolveTimelapseStaticInputCount(TimelapseJobSnapshot snapshot)
        => CountDistinctInputs(
            BuildInputKey(snapshot.OriginalImage.MediaId, snapshot.OriginalImage.ObjectKey, snapshot.OriginalImage.PublicUrl),
            snapshot.StartImage is null
                ? null
                : BuildInputKey(snapshot.StartImage.MediaId, snapshot.StartImage.ObjectKey, snapshot.StartImage.PublicUrl));

    public static int ResolveRVideoStaticInputCount(IEnumerable<RVideoEffectiveSceneImageSource> sources)
        => CountDistinctInputs(sources
            .Where(source => source.HasUsableInput)
            .Select(source => BuildInputKey(source.SelectedImageVersionId, source.SourceImageObjectKey, source.SourceImageUrl))
            .ToArray());

    public static int ResolveBillableStaticImageCount(int staticInputCount, bool chargeStaticImagePoints)
        => chargeStaticImagePoints ? Math.Max(0, staticInputCount) : 0;

    private static int CountDistinctInputs(params string?[] keys)
        => keys.Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.Ordinal)
            .Count();

    private static string? BuildInputKey(Guid? mediaId, string? objectKey, string? url)
    {
        if (mediaId is Guid id && id != Guid.Empty)
        {
            return $"m:{id:N}";
        }

        if (!string.IsNullOrWhiteSpace(objectKey))
        {
            return $"k:{objectKey.Trim()}";
        }

        if (!string.IsNullOrWhiteSpace(url))
        {
            return $"u:{url.Trim()}";
        }

        return null;
    }
}
