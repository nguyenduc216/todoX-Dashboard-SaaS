using TodoX.Web.Models;

namespace TodoX.Web.Services.Images;

public sealed class ServiceImageQcService
{
    public ServiceImageQcResult Check(UniversalServiceImageRenderPlan plan, string? finalImageUrl, bool hasBrandAsset)
    {
        var result = new ServiceImageQcResult { Passed = true, Score = 100 };

        if (string.IsNullOrWhiteSpace(finalImageUrl))
        {
            result.Errors.Add("Final image URL is missing.");
        }

        if (plan.QcPolicy.CheckDimensions && (plan.Layout.CanvasWidth <= 0 || plan.Layout.CanvasHeight <= 0))
        {
            result.Errors.Add("Canvas dimension metadata is invalid.");
        }

        if (plan.QcPolicy.CheckTextSafeArea)
        {
            if (plan.TextOverlay.Headline.Length > 56)
            {
                result.Warnings.Add("Headline may be too long; safe text engine will shrink/truncate.");
            }

            if (plan.TextOverlay.MicroBullets.Count > 2)
            {
                result.Warnings.Add("Too many micro bullets; reduce text density.");
            }
        }

        if (plan.QcPolicy.CheckFixedAssetTransparency && plan.FixedAssets.RequireTransparentBackground && !hasBrandAsset)
        {
            result.Warnings.Add("No uploaded brand robot asset. The configured default asset may be used if available.");
        }

        if (plan.QcPolicy.CheckFixedAssetAspectRatio && !plan.FixedAssets.PreserveAspectRatio)
        {
            result.Errors.Add("Fixed asset aspect ratio preservation is disabled.");
        }

        if (plan.QcPolicy.CheckConceptCompleteness && plan.CreativeBrief.RequiredConcepts.Count == 0)
        {
            result.Warnings.Add("Render plan has no required concepts.");
        }

        result.Passed = result.Errors.Count == 0;
        result.Score = Math.Max(0, 100 - result.Errors.Count * 30 - result.Warnings.Count * 8);
        return result;
    }
}
