namespace TodoX.Web.Services.AiProviders.Kie;

public static class KieTaskStatusMapper
{
    public static string Map(string? providerState)
        => providerState?.Trim().ToLowerInvariant() switch
        {
            "waiting" => KieTaskStatuses.Queued,
            "queuing" => KieTaskStatuses.Queued,
            "generating" => KieTaskStatuses.Rendering,
            "success" => KieTaskStatuses.Completed,
            "fail" => KieTaskStatuses.Failed,
            _ => KieTaskStatuses.Unknown
        };

    public static bool IsTerminal(string? status)
        => IsSuccess(status) || IsFailure(status);

    public static bool IsSuccess(string? status)
        => string.Equals(status, KieTaskStatuses.Completed, StringComparison.OrdinalIgnoreCase);

    public static bool IsFailure(string? status)
        => string.Equals(status, KieTaskStatuses.Failed, StringComparison.OrdinalIgnoreCase);

    public static bool IsTransient(string? status)
        => string.Equals(status, KieTaskStatuses.Queued, StringComparison.OrdinalIgnoreCase)
           || string.Equals(status, KieTaskStatuses.Rendering, StringComparison.OrdinalIgnoreCase)
           || string.Equals(status, KieTaskStatuses.Unknown, StringComparison.OrdinalIgnoreCase);
}
