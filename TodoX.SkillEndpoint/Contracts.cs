namespace TodoX.SkillEndpoint;

public sealed record RepairPlanRequest(
    string? Reason = null,
    int[]? SceneIndexes = null,
    bool IncludeProviderLookup = true,
    bool IncludeBillingCheck = true);

public sealed record RetryJobRequest(
    int[]? SceneIndexes = null,
    string Mode = "failed_only",
    bool ReuseSuccessfulMedia = true,
    bool ReconcileBeforeRetry = true,
    string? Reason = null);

public sealed record ResumeJobRequest(
    string From = "auto",
    bool ReuseSuccessfulMedia = true,
    bool ReconcileProviderTasks = true,
    string? Reason = null);

public sealed record ExecuteRepairRequest(
    string RepairCode,
    bool Confirm,
    int[]? SceneIndexes = null,
    string? ExpectedJobStatus = null,
    string? Reason = null);

public sealed record ReconcileJobRequest(
    int[]? SceneIndexes = null,
    bool QueryProvider = true,
    bool UpdateLocalState = true,
    bool ReconcileBilling = true,
    string? Reason = null);
