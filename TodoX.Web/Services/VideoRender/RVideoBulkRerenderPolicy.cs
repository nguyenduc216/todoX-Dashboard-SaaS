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

    private static bool IsPendingReconciliation(SceneVideoUiState state)
        => string.Equals(state.LatestJob?.Status, PendingReconciliation, StringComparison.OrdinalIgnoreCase)
            || string.Equals(state.LatestJobVersion?.Status, PendingReconciliation, StringComparison.OrdinalIgnoreCase);
}
