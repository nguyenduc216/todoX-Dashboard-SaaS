using System.Text.Json;

namespace TodoX.Web.Services.DanceSell;

public static class DanceSellPointDisplay
{
    public static decimal? ResolveDisplayPoints(DanceSellJobDto job, DanceSellProviderOperationDto? latestMotionOperation)
    {
        var chargedPoints = TryReadChargedPoints(latestMotionOperation);
        return chargedPoints ?? job.TotalTodoxPointsEstimated;
    }

    public static decimal? TryReadChargedPoints(DanceSellProviderOperationDto? operation)
    {
        if (operation is null)
        {
            return null;
        }

        return TryReadDecimal(operation.PricingSnapshotJson, "total_charged_points", "totalChargedPoints", "charged_points", "total_points")
            ?? operation.TodoxPointsCharged;
    }

    public static decimal? TryReadEstimatedPoints(DanceSellProviderOperationDto? operation)
        => TryReadDecimal(operation?.PricingSnapshotJson, "total_planned_points", "totalPlannedPoints", "estimated_points")
           ?? operation?.TodoxPointsEstimated;

    private static decimal? TryReadDecimal(string? rawJson, params string[] propertyNames)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(rawJson);
            foreach (var propertyName in propertyNames)
            {
                if (!document.RootElement.TryGetProperty(propertyName, out var value))
                {
                    continue;
                }

                if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
                {
                    return number;
                }

                if (value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(), out var parsed))
                {
                    return parsed;
                }
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }
}
