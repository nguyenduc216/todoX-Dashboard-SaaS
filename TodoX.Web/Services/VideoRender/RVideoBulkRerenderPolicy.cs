using TodoX.Web.Models;

namespace TodoX.Web.Services.VideoRender;

public enum RVideoBulkRerenderMode
{
    AllScenes = 1,
    IncompleteScenes = 2
}

public enum RVideoBulkRerenderSceneDecision
{
    Render,
    SkipCompleted,
    SkipInFlight,
    SkipProviderPending,
    SkipMissingImage,
    SkipStateUnknown
}

public sealed record RVideoBulkRerenderQuoteLine(long SceneId, int SceneIndex, decimal Points, bool OutcomeUnknown);

public sealed record RVideoBulkRerenderSkippedScene(int SceneIndex, string Reason);

public sealed record RVideoBulkRerenderQuote(
    RVideoBulkRerenderMode Mode,
    IReadOnlyList<RVideoBulkRerenderQuoteLine> Scenes,
    IReadOnlyList<RVideoBulkRerenderSkippedScene> Skipped,
    decimal AvailablePoints,
    bool HasExternalVoice)
{
    public decimal TotalPoints => Scenes.Sum(x => x.Points);
    public decimal MissingPoints => Math.Max(0m, TotalPoints - AvailablePoints);
    public int UnknownOutcomeCount => Scenes.Count(x => x.OutcomeUnknown);
    public bool CanConfirm => Scenes.Count > 0 && MissingPoints == 0m;
}

/// <summary>
/// Decides which scenes a bulk video re-render may touch. A running attempt is never replaced, so the
/// customer cannot pay twice for it; an attempt waiting for reconciliation is not complete and may be replaced.
/// </summary>
public static class RVideoBulkRerenderPolicy
{
    private const string PendingReconciliation = "pending_reconciliation";

    public static RVideoBulkRerenderSceneDecision Decide(
        RVideoBulkRerenderMode mode,
        SceneVideoUiState? state,
        bool hasCompletedVideo,
        bool hasImage)
    {
        if (state is null) return RVideoBulkRerenderSceneDecision.SkipStateUnknown;
        var waitingForReconciliation = IsPendingReconciliation(state);
        if (state.HasActiveJob && !waitingForReconciliation) return RVideoBulkRerenderSceneDecision.SkipInFlight;
        // The provider holds this task and the video may already exist: check it first instead of paying twice.
        if (waitingForReconciliation && state.HasProviderIdentity) return RVideoBulkRerenderSceneDecision.SkipProviderPending;
        if (mode == RVideoBulkRerenderMode.IncompleteScenes && hasCompletedVideo && !waitingForReconciliation)
            return RVideoBulkRerenderSceneDecision.SkipCompleted;
        return hasImage ? RVideoBulkRerenderSceneDecision.Render : RVideoBulkRerenderSceneDecision.SkipMissingImage;
    }

    // The supersede ledger acknowledges an existing attempt, so a scene without one takes the first-render path.
    public static bool UsesNewOperation(SceneVideoUiState state)
        => state.LatestJobVersion is not null || state.SelectedVersion is not null;

    public static bool HasUnknownOutcome(SceneVideoUiState state)
        => IsPendingReconciliation(state) || state.HasProviderIdentity
            || state.SubmitBlockReason != SceneVideoSubmitBlockReason.None;

    // A scene short of points stays in the quote so the popup can show the shortage.
    public static bool CanQuote(RVideoSceneVideoRenderEstimate estimate, bool forSupersede)
        => estimate.EstimatedPoints > 0m && (estimate.CanStart || estimate.MissingPoints > 0m)
            && (!forSupersede || estimate.OperationSettings is not null);

    public static async Task<RVideoSceneVideoRenderResult> SubmitAsync(
        IRVideoSceneVideoRenderAction action,
        RVideoSceneVideoRenderRequest request,
        CurrentUserSession user)
    {
        if (!request.ForSupersede) return await action.EnqueueRerenderAsync(request, user);
        var acknowledgement = await action.GetSupersedeAcknowledgementAsync(request.ProjectId, request.Scene.Id, user, Guid.NewGuid());
        return await action.SupersedeAsync(request, acknowledgement with { RiskAcknowledged = true }, user);
    }

    private static bool IsPendingReconciliation(SceneVideoUiState state)
        => string.Equals(state.LatestJob?.Status, PendingReconciliation, StringComparison.OrdinalIgnoreCase)
            || string.Equals(state.LatestJobVersion?.Status, PendingReconciliation, StringComparison.OrdinalIgnoreCase);
}
