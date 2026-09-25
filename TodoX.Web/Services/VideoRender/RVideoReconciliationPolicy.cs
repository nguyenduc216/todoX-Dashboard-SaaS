using TodoX.Web.Services.Render;

namespace TodoX.Web.Services.VideoRender;

internal sealed record RVideoReconciliationRetryDecision(
    int CurrentAttempt,
    bool ShouldSchedule,
    bool PersistentEligible);

internal sealed record RVideoProviderPollDecision(
    bool ShouldSchedule,
    string Reason,
    string? TransitionStatus,
    string? TransitionErrorCode,
    string? ProviderTaskId);

internal static class RVideoReconciliationPolicy
{
    internal const string ProviderPollTimeoutErrorCode = "SCENE_VIDEO_PROVIDER_POLL_TIMEOUT";

    internal static RVideoReconciliationRetryDecision EvaluateRetry(int scheduledRetries, int maxAttempts)
    {
        var currentAttempt = Math.Max(0, scheduledRetries) + 1;
        var shouldSchedule = currentAttempt < Math.Max(1, maxAttempts);
        return new RVideoReconciliationRetryDecision(currentAttempt, shouldSchedule, shouldSchedule);
    }

    internal static RVideoProviderPollDecision EvaluateProviderPoll(
        string? status,
        int providerPollCount,
        DateTimeOffset? providerPollStartedAt,
        string? providerTaskId,
        bool enforceReconciliationLimit,
        bool enforceProviderPollTimeout,
        int maxReconciliationRetries,
        int providerPollTimeoutMinutes,
        DateTimeOffset now)
    {
        if (!IsPollableStatus(status))
        {
            return Blocked("status_not_pollable", providerTaskId);
        }

        if (enforceReconciliationLimit && providerPollCount >= Math.Max(1, maxReconciliationRetries))
        {
            return Blocked("reconciliation_limit_exhausted", providerTaskId);
        }

        if (enforceProviderPollTimeout
            && providerPollStartedAt is DateTimeOffset startedAt
            && startedAt.AddMinutes(Math.Max(1, providerPollTimeoutMinutes)) <= now)
        {
            return new RVideoProviderPollDecision(
                false,
                "provider_poll_timeout",
                RenderJobStatuses.PendingReconciliation,
                ProviderPollTimeoutErrorCode,
                providerTaskId);
        }

        return Blocked("concurrent_state_change", providerTaskId);
    }

    internal static bool IsPersistentEligible(
        string? status,
        string? errorCode,
        int scheduledRetries,
        int maxAttempts)
    {
        if (!IsPollableStatus(status))
        {
            return false;
        }

        if (status is not (RenderJobStatuses.PendingReconciliation or RenderJobStatuses.Failed))
        {
            return true;
        }

        return !string.Equals(errorCode, ProviderPollTimeoutErrorCode, StringComparison.OrdinalIgnoreCase)
               && EvaluateRetry(scheduledRetries, maxAttempts).PersistentEligible;
    }

    internal static bool IsProviderPollTimeoutQuarantine(
        string? status,
        string? errorCode,
        string? providerTaskId)
        => string.Equals(status, RenderJobStatuses.PendingReconciliation, StringComparison.OrdinalIgnoreCase)
           && string.Equals(errorCode, ProviderPollTimeoutErrorCode, StringComparison.OrdinalIgnoreCase)
           && !string.IsNullOrWhiteSpace(providerTaskId);

    private static bool IsPollableStatus(string? status)
        => status is RenderJobStatuses.Queued
            or RenderJobStatuses.Preparing
            or RenderJobStatuses.Rendering
            or RenderJobStatuses.PostProcessing
            or RenderJobStatuses.PendingReconciliation
            or RenderJobStatuses.Failed;

    private static RVideoProviderPollDecision Blocked(string reason, string? providerTaskId)
        => new(false, reason, null, null, providerTaskId);
}
