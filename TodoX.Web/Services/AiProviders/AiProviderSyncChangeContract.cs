namespace TodoX.Web.Services.AiProviders;

public static class AiProviderSyncChangeContract
{
    public const string Provider = "provider";
    public const string Model = "model";
    public const string Capability = "capability";
    public const string Price = "price";
    public const string Status = "status";

    public static readonly string[] EntityTypes =
    [
        Provider,
        Model,
        Capability,
        Price,
        Status
    ];

    public static readonly string[] ChangeTypes =
    [
        "insert",
        "update",
        "status_change",
        "price_change",
        "disable",
        "enable",
        "no_change",
        "MODEL_ADDED",
        "MODEL_UPDATED",
        "MODEL_STATUS_CHANGED",
        "MODE_ADDED",
        "DURATION_ADDED",
        "DURATION_REMOVED",
        "RESOLUTION_ADDED",
        "PRICE_ADDED",
        "PRICE_CHANGED",
        "PRICE_DISABLED"
    ];

    public static bool IsValidEntityType(string? entityType)
        => EntityTypes.Contains(entityType, StringComparer.Ordinal);

    public static bool IsValidChangeType(string? changeType)
        => ChangeTypes.Contains(changeType, StringComparer.Ordinal);

    public static void EnsureEntityType(string entityType)
    {
        if (!IsValidEntityType(entityType))
        {
            throw new InvalidOperationException($"Unsupported sync change entity_type: {entityType}");
        }
    }

    public static void EnsureChangeType(string changeType)
    {
        if (!IsValidChangeType(changeType))
        {
            throw new InvalidOperationException($"Unsupported sync change change_type: {changeType}");
        }
    }
}
