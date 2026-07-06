using System.Text.Json;
using TodoX.Web.Models;
using TodoX.Web.Services.ImageRender;

namespace TodoX.Web.Services.Images;

public sealed class ServiceImageQcService
{
    public ServiceImageQcResult Check(
        UniversalServiceImageRenderPlan plan,
        string? finalImageUrl,
        bool hasBrandAsset,
        IReadOnlyList<RenderLogEntry>? renderLogs = null)
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

        ApplyRenderLogQc(renderLogs, result);

        result.Score = Math.Clamp(100 - result.Errors.Count * 25 - result.Warnings.Count * 10, 0, 100);
        result.Passed = result.Errors.Count == 0 && result.Score >= 80;
        return result;
    }

    private static void ApplyRenderLogQc(IReadOnlyList<RenderLogEntry>? logs, ServiceImageQcResult result)
    {
        if (logs is null || logs.Count == 0)
        {
            return;
        }

        foreach (var log in logs)
        {
            if (log.Data is null)
            {
                continue;
            }

            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(log.Data));
            var root = doc.RootElement;
            if (log.Step.Equals("FIXED_ASSET_COMPOSITED", StringComparison.OrdinalIgnoreCase))
            {
                if (TryGetDouble(root, "aspect", "aspectRatioDelta", out var delta) && delta > 0.10)
                {
                    result.Errors.Add($"Robot/logo bị kéo dãn sai tỷ lệ ({delta:P0}).");
                }

                if (TryGetDouble(root, "transparency", "assetOpaqueBrightPixelRatio", out var brightRatio) && brightRatio > 0.18)
                {
                    result.Warnings.Add("Robot/logo có thể còn nền trắng. Hãy upload PNG nền trong suốt.");
                }

                if (TryGetBool(root, "layoutRecomputedForActualCanvas", out var recomputed) && !recomputed)
                {
                    result.Errors.Add("Layout chưa được recompute theo canvas thực tế.");
                }
            }
            else if (log.Step.Equals("TEXT_OVERLAY_APPLIED", StringComparison.OrdinalIgnoreCase)
                && root.TryGetProperty("textBoxes", out var boxes)
                && boxes.ValueKind == JsonValueKind.Array)
            {
                foreach (var box in boxes.EnumerateArray())
                {
                    var name = box.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "text" : "text";
                    if (box.TryGetProperty("fit", out var fitEl) && fitEl.ValueKind == JsonValueKind.False)
                    {
                        result.Errors.Add($"Text '{name}' không fit vùng an toàn.");
                    }

                    if (box.TryGetProperty("warning", out var warningEl)
                        && warningEl.ValueKind == JsonValueKind.String
                        && !string.IsNullOrWhiteSpace(warningEl.GetString()))
                    {
                        result.Warnings.Add($"Text '{name}': {warningEl.GetString()}.");
                    }
                }
            }
        }
    }

    private static bool TryGetDouble(JsonElement root, string parentName, string propertyName, out double value)
    {
        value = 0;
        if (!root.TryGetProperty(parentName, out var parent) || !parent.TryGetProperty(propertyName, out var element))
        {
            return false;
        }

        return element.TryGetDouble(out value);
    }

    private static bool TryGetBool(JsonElement root, string propertyName, out bool value)
    {
        value = false;
        if (!root.TryGetProperty(propertyName, out var element))
        {
            return false;
        }

        if (element.ValueKind == JsonValueKind.True)
        {
            value = true;
            return true;
        }

        if (element.ValueKind == JsonValueKind.False)
        {
            value = false;
            return true;
        }

        return false;
    }
}
