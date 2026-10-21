using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Services.Render;

namespace TodoX.Web.Services.VideoRender;

public sealed record SceneVideoSubmitState(string SubmitState, Guid OwnerToken, string? ProviderTaskId, string? ProviderVideoIdBase);

/// <summary>
/// Canonical classification for scene-video submits refused before any provider HTTP request.
/// These codes are definite failures, never ambiguous outcomes.
/// </summary>
public static class SceneVideoSubmitGuard
{
    /// <summary>The scene guard or a pre-existing durable intent refused the new submit.</summary>
    public const string ErrorCodeGuardRejected = "SCENE_VIDEO_SUBMIT_GUARD_REJECTED";

    /// <summary>The same logical request ID was already persisted; this attempt must not resend.</summary>
    public const string ErrorCodeDuplicate = "SCENE_VIDEO_SUBMIT_DUPLICATE_ATTEMPT";

    /// <summary>The durable intent existed but could not be moved to the in-flight state.</summary>
    public const string ErrorCodeIntentTransitionFailed = "SCENE_VIDEO_SUBMIT_INTENT_STATE_REJECTED";

    public const string MessageGuardRejected =
        "Lần gửi video này bị chặn trước khi gọi nhà cung cấp; không có yêu cầu nào được gửi đi.";

    public const string MessageDuplicate =
        "Yêu cầu gửi video này đã được ghi nhận trước đó; không gửi lại để tránh trùng lặp.";

    public const string MessageIntentTransitionFailed =
        "Không thể chuyển ý định gửi video sang trạng thái đang gửi; chưa có yêu cầu nào được gửi tới nhà cung cấp.";
}

/// <summary>
/// Result of a pre-submit claim attempt. A rejected claim proves no provider HTTP
/// request was made, so it must never be treated as an ambiguous submit outcome.
/// </summary>
public enum SceneVideoSubmitClaimOutcome
{
    /// <summary>The durable submit intent was persisted and the caller owns the submit.</summary>
    Claimed,

    /// <summary>A pre-existing durable intent or scene guard blocked the submit before any HTTP call.</summary>
    Blocked,

    /// <summary>The same logical request ID was already persisted by another owner; no HTTP call happened.</summary>
    AlreadyClaimed
}

/// <summary>
/// Which guard clause refused a submit. Recorded so operators can tell a stale
/// in-flight attempt apart from a scene-level manual-review hold without re-deriving SQL.
/// </summary>
public enum SceneVideoSubmitBlockReason
{
    /// <summary>No refusal; the claim succeeded.</summary>
    None,

    /// <summary>
    /// A durable submit intent that could still be the provider's live task for this scene exists for
    /// another job, or this job's own intent is not yet resolved. Historical attempts that already ended
    /// without a provider task never reach this state.
    /// </summary>
    ExistingIntentUnresolved,

    /// <summary>A scene-video render job is still in flight or is held for manual review.</summary>
    SceneJobInFlightOrManualReview,

    /// <summary>A scene-video version is still in flight or is held for reconciliation.</summary>
    SceneVersionInFlightOrReconciliation,

    /// <summary>
    /// A settled historical attempt left a manual-review marker behind, and no live attempt exists.
    /// This never refuses the submit: the marker belongs to a job that already ended, so it cannot
    /// still be the provider's live task. It is reported so the new operation is started explicitly
    /// instead of the marker being silently ignored.
    /// </summary>
    HistoricalManualReviewWithoutLiveAttempt
}

/// <summary>
/// One durable submit-intent fact the pre-HTTP guard inspects for a scene, reduced to the only
/// properties that decide whether the intent still represents a live submit for that scene.
/// Keeping this a value type lets the refusal decision be proven in tests without a database.
/// </summary>
public sealed record SceneVideoSubmitIntentFact(
    string SubmitState,
    bool HasProviderTask,
    bool IsNewJob,
    bool IsPriorJobTerminal,
    bool IsPriorJobCompleted)
{
    /// <summary>
    /// Whether an intent row must still refuse an unrelated new submit for the same scene.
    /// <para>
    /// An intent that belongs to the incoming job always protects that operation, so a double-click,
    /// an automatic retry and an in-flight or ambiguous submit can never be silently resent.
    /// </para>
    /// <para>
    /// An intent left behind by a different (historical) attempt blocks only while it can still be the
    /// provider's live task for the scene: while the provider needs reconciling, or while it has not
    /// reached the provider yet AND its own job has not terminated. Once such an attempt is definitely
    /// settled (a recorded pre-HTTP refusal, or a recorded provider task whose job already ended) it
    /// must not lock the scene forever and prevent the user from starting a new render operation.
    /// </para>
    /// </summary>
    public static bool BlocksSceneSubmit(SceneVideoSubmitIntentFact intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        if (intent.IsNewJob)
        {
            // This job's own intent is always authoritative, whatever state it is in.
            return true;
        }

        return Normalize(intent.SubmitState) switch
        {
            // A submit may be in flight or its outcome unknown; the provider task must never be duplicated.
            "SUBMIT_IN_FLIGHT" or "AMBIGUOUS_SUBMIT" or "MANUAL_REVIEW_REQUIRED" => true,
            // An intent was persisted but never sent. It blocks only while its own job is still live.
            "SUBMIT_INTENT_PERSISTED" => !intent.IsPriorJobTerminal,
            // A recorded provider task only settles the attempt once its job actually completed and the
            // result was persisted. A task whose job failed may still be live at the provider, so it keeps
            // blocking until an operator reconciles it.
            "CONFIRMED_PROVIDER_TASK" => !intent.IsPriorJobCompleted,
            // A recorded pre-HTTP refusal proves no request left the server and the attempt is settled.
            "CONFIRMED_NO_TASK" => false,
            _ => true
        };
    }

    private static string Normalize(string? submitState)
        => string.IsNullOrWhiteSpace(submitState) ? string.Empty : submitState.Trim().ToUpperInvariant();
}

/// <summary>
/// One scene-video render job fact Clause 2 of the pre-HTTP guard inspects, reduced to the only
/// properties that decide whether the job still holds the scene. Keeping this a value type lets the
/// refusal decision be proven in tests with the exact production scene-814 rows, without a database.
/// </summary>
public sealed record SceneVideoSubmitJobFact(
    string Status,
    string? ErrorCode,
    bool HasManualReviewMarker,
    bool HasProviderIdentity)
{
    /// <summary>The manual-review marker as written by the ambiguous-submit watchdog.</summary>
    public const string ManualReviewErrorCode = "SCENE_VIDEO_MANUAL_REVIEW_REQUIRED";

    private const string PendingReconciliation = "pending_reconciliation";

    /// <summary>
    /// Whether this job is still open, i.e. it may still be progressing or awaiting reconciliation.
    /// A completed, failed or cancelled job has ended and can never become live again.
    /// </summary>
    public bool IsOpen
        => Normalize(Status) is not ("completed" or "failed" or "cancelled");

    /// <summary>Whether this job carries the manual-review marker in either recorded form.</summary>
    public bool CarriesManualReviewMarker
        => HasManualReviewMarker
           || string.Equals(Normalize(ErrorCode), ManualReviewErrorCode.ToLowerInvariant(), StringComparison.Ordinal);

    /// <summary>
    /// Whether this job must still refuse a submit for the scene (Clause 2).
    /// <para>
    /// A job that is awaiting reconciliation, or that is still open while holding a provider identity,
    /// may have a live provider task and therefore always holds the scene.
    /// </para>
    /// <para>
    /// The manual-review marker holds the scene only while its own job is still open, which is how every
    /// other manual-review gate in the application reads it (enqueue guard, active-job lookup, marker
    /// writer). A marker left on an already terminated job is history: it proves the earlier attempt
    /// ended, not that a provider task is still running, so it must never lock the scene against a
    /// brand-new user-initiated operation.
    /// </para>
    /// </summary>
    public bool BlocksSceneSubmit
        => Normalize(Status) == PendingReconciliation
           || (IsOpen && (CarriesManualReviewMarker || HasProviderIdentity));

    /// <summary>
    /// Whether this job is a settled historical attempt that left a manual-review marker behind and can
    /// no longer hold the scene. Reported so a new operation is started transparently, not silently.
    /// </summary>
    public bool IsSettledManualReviewHistory
        => !BlocksSceneSubmit && CarriesManualReviewMarker;

    private static string Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
}

/// <summary>
/// Executable form of the three-clause pre-HTTP guard. Every clause is a pure function of the durable
/// facts, so the refusal that blocked scene 814 can be reproduced and proven in tests.
/// </summary>
public static class SceneSubmitGuard
{
    /// <summary>
    /// Evaluates all three clauses in the same precedence order as <c>ClaimSubmitAsync</c> so a
    /// refusal stays attributable to exactly one cause.
    /// </summary>
    public static SceneVideoSubmitBlockReason Evaluate(
        IEnumerable<SceneVideoSubmitIntentFact>? intents,
        IEnumerable<SceneVideoSubmitJobFact>? jobs,
        IEnumerable<SceneVideoSubmitJobFact>? versions)
    {
        // Clause 1 takes precedence: a live or not-yet-settled submit intent is the strongest hold and
        // must map to the same reported reason it does in SQL.
        if (intents is not null && intents.Any(i => SceneVideoSubmitIntentFact.BlocksSceneSubmit(i)))
            return SceneVideoSubmitBlockReason.ExistingIntentUnresolved;
        if (jobs is not null && jobs.Any(j => j.BlocksSceneSubmit))
            return SceneVideoSubmitBlockReason.SceneJobInFlightOrManualReview;
        if (versions is not null && versions.Any(v => v.BlocksSceneSubmit))
            return SceneVideoSubmitBlockReason.SceneVersionInFlightOrReconciliation;
        if (jobs is not null && jobs.Any(j => j.IsSettledManualReviewHistory))
            return SceneVideoSubmitBlockReason.HistoricalManualReviewWithoutLiveAttempt;
        return SceneVideoSubmitBlockReason.None;
    }

    /// <summary>
    /// Whether the outcome permits the submit to proceed to the provider adapter as a new operation.
    /// </summary>
    public static bool PermitsSubmit(SceneVideoSubmitBlockReason reason)
        => reason is SceneVideoSubmitBlockReason.None
           or SceneVideoSubmitBlockReason.HistoricalManualReviewWithoutLiveAttempt;
}

public sealed record SceneVideoSubmitClaim(
    SceneVideoSubmitClaimOutcome Outcome,
    Guid? OwnerToken,
    SceneVideoSubmitBlockReason BlockReason = SceneVideoSubmitBlockReason.None)
{
    public static readonly SceneVideoSubmitClaim Blocked = new(SceneVideoSubmitClaimOutcome.Blocked, null);
    public static readonly SceneVideoSubmitClaim AlreadyClaimed = new(SceneVideoSubmitClaimOutcome.AlreadyClaimed, null);

    public static SceneVideoSubmitClaim BlockedBecause(SceneVideoSubmitBlockReason reason)
        => new(SceneVideoSubmitClaimOutcome.Blocked, null, reason);

    /// <summary>
    /// A claim that is permitted while a settled historical manual-review marker exists. The marker is
    /// surfaced to the caller so the new operation is started explicitly and never silently.
    /// </summary>
    public static SceneVideoSubmitClaim ClaimedWithHistoricalManualReview(Guid ownerToken)
        => new(SceneVideoSubmitClaimOutcome.Claimed, ownerToken,
            SceneVideoSubmitBlockReason.HistoricalManualReviewWithoutLiveAttempt);
}

public sealed class SceneVideoSubmitCoordinator
{
    /// <summary>
    /// Snapshot of every state fact the pre-HTTP guard inspects for one scene. Being a plain
    /// value type keeps the refusal decision executable in tests without a database, so the
    /// classification that caused the scene-814 mislabel can be verified behaviourally.
    /// </summary>
    public sealed record SceneSubmitGuardFacts(
        bool HasUnresolvedIntent,
        bool HasInFlightOrManualReviewJob,
        bool HasInFlightOrReconciliationVersion,
        bool HasSettledManualReviewHistory = false);

    /// <summary>
    /// Pure guard decision, evaluated in the same precedence order as the SQL guard so a refusal
    /// stays attributable to exactly one cause.
    /// </summary>
    public static SceneVideoSubmitBlockReason ClassifyBlock(SceneSubmitGuardFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (facts.HasUnresolvedIntent) return SceneVideoSubmitBlockReason.ExistingIntentUnresolved;
        if (facts.HasInFlightOrManualReviewJob) return SceneVideoSubmitBlockReason.SceneJobInFlightOrManualReview;
        if (facts.HasInFlightOrReconciliationVersion) return SceneVideoSubmitBlockReason.SceneVersionInFlightOrReconciliation;
        if (facts.HasSettledManualReviewHistory) return SceneVideoSubmitBlockReason.HistoricalManualReviewWithoutLiveAttempt;
        return SceneVideoSubmitBlockReason.None;
    }

    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;

    public SceneVideoSubmitCoordinator(TodoXConnectionFactory factory, TenantContext tenant)
    {
        _factory = factory;
        _tenant = tenant;
    }

    public async Task<SceneVideoSubmitClaim> ClaimSubmitAsync(long projectId, long sceneId, string logicalRequestId, Guid jobId, CancellationToken ct = default)
    {
        if (projectId <= 0 || sceneId <= 0 || jobId == Guid.Empty || string.IsNullOrWhiteSpace(logicalRequestId))
            throw new ArgumentException("Invalid scene-video submit identity.");
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        await conn.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtextextended(@key, 0));",
            new { key = RenderJobService.BuildSceneJobLockName(RenderJobTypes.RenderSceneVideo, sceneId) }, tx);
        var owned = await conn.ExecuteScalarAsync<bool>(
            """
            SELECT EXISTS (SELECT 1 FROM video_render.video_project_scenes s
                JOIN video_render.video_projects p ON p.id=s.project_id AND p.tenant_id=s.tenant_id
                JOIN render.render_jobs j ON j.id=@jobId AND j.tenant_id=s.tenant_id
                    AND j.job_type='render_scene_video'
                WHERE s.id=@sceneId AND s.project_id=@projectId AND s.tenant_id=@tenant
                    AND COALESCE(j.input_json->>'sceneId', j.input_json->>'scene_id')=s.id::text
                    AND COALESCE(j.input_json->>'projectId', j.input_json->>'project_id')=p.id::text);
            """, new { projectId, sceneId, jobId, tenant = _tenant.TenantId }, tx);
        if (!owned) throw new InvalidOperationException("Scene-video submit tenant or job ownership mismatch.");

        // The reason is evaluated in the same precedence order as the previous combined EXISTS so that
        // refusals stay behaviourally identical while still being attributable for operators.
        var blockReason = (SceneVideoSubmitBlockReason)await conn.ExecuteScalarAsync<int>(
            """
            SELECT CASE
                WHEN EXISTS (SELECT 1 FROM render.scene_video_submit_intents i
                    JOIN render.render_jobs prior ON prior.id=i.render_job_id AND prior.tenant_id=i.tenant_id
                    WHERE i.tenant_id=@tenant AND i.scene_id=@sceneId
                      AND (
                            -- This job's own intent is always authoritative: it protects the double-click,
                            -- the automatic retry and any in-flight/ambiguous submit from being resent.
                            i.render_job_id=@jobId
                            -- Intent that reached the provider but has no recorded task yet: the provider task
                            -- may be live, so a new operation must not silently duplicate it.
                            OR i.submit_state IN ('SUBMIT_IN_FLIGHT', 'AMBIGUOUS_SUBMIT', 'MANUAL_REVIEW_REQUIRED')
                            -- Intent that never reached the provider: it blocks only while its own job is live.
                            OR (i.submit_state='SUBMIT_INTENT_PERSISTED' AND prior.status NOT IN ('completed', 'failed'))
                            -- A recorded provider task settles the attempt only once its job completed.
                            OR (i.submit_state='CONFIRMED_PROVIDER_TASK' AND prior.status <> 'completed')
                          ))
                    THEN 1
                WHEN EXISTS (SELECT 1 FROM render.render_jobs j
                    WHERE j.tenant_id=@tenant AND j.job_type='render_scene_video'
                      AND COALESCE(j.input_json->>'sceneId', j.input_json->>'scene_id')=@sceneText
                      -- A manual-review marker only holds the scene while its own job is still open.
                      -- Every other manual-review gate in the application reads the marker this way
                      -- (the enqueue guard and the active-job lookup both require pending_reconciliation,
                      -- and the watchdog only writes the marker onto a pending_reconciliation job).
                      -- A marker left on an already terminated job is history, not a live provider task,
                      -- so it must not lock the scene against a brand-new user-initiated operation.
                      AND (j.status='pending_reconciliation'
                        AND NOT (j.id <> @jobId
                          AND j.error_code='RVIDEO_JOB_VERSION_OWNERSHIP_REQUIRED'
                          AND NULLIF(btrim(j.provider_task_id), '') IS NULL
                          AND NULLIF(btrim(j.provider_video_id_base), '') IS NULL
                          AND (j.lock_until IS NULL OR j.lock_until <= now())
                          AND NOT EXISTS (SELECT 1 FROM video_render.scene_video_versions owned_version
                              WHERE owned_version.tenant_id=j.tenant_id AND owned_version.render_job_id=j.id)
                          AND NOT EXISTS (SELECT 1 FROM render.scene_video_submit_intents owned_intent
                              WHERE owned_intent.tenant_id=j.tenant_id AND owned_intent.render_job_id=j.id))
                        OR (j.status NOT IN ('completed', 'failed', 'cancelled')
                            AND (j.error_code='SCENE_VIDEO_MANUAL_REVIEW_REQUIRED'
                                 OR COALESCE(j.input_json, '{}'::jsonb) ? 'sceneVideoManualReviewAt'))
                        OR (j.status NOT IN ('completed', 'failed') AND
                            (NULLIF(btrim(j.provider_task_id), '') IS NOT NULL
                             OR NULLIF(btrim(j.provider_video_id_base), '') IS NOT NULL))))
                    THEN 2
                WHEN EXISTS (SELECT 1 FROM video_render.scene_video_versions v
                    WHERE v.tenant_id=@tenant AND v.scene_id=@sceneId
                      AND ((v.status NOT IN ('completed', 'failed') AND
                            (NULLIF(btrim(v.provider_task_id), '') IS NOT NULL
                             OR NULLIF(btrim(v.provider_video_id_base), '') IS NOT NULL))
                        OR v.status='pending_reconciliation'))
                    THEN 3
                ELSE 0
            END;
            """, new { tenant = _tenant.TenantId, sceneId, sceneText = sceneId.ToString(), jobId }, tx);
        if (blockReason != SceneVideoSubmitBlockReason.None)
        {
            tx.Commit();
            return SceneVideoSubmitClaim.BlockedBecause(blockReason);
        }
        // The three refusal clauses passed. A manual-review marker on an already settled job is history:
        // it must not refuse this operation, but it is reported so the new submit is started explicitly
        // rather than the marker being silently dropped.
        var historicalManualReview = await conn.ExecuteScalarAsync<bool>(
            """
            SELECT EXISTS (SELECT 1 FROM render.render_jobs j
                WHERE j.tenant_id=@tenant AND j.job_type='render_scene_video'
                  AND COALESCE(j.input_json->>'sceneId', j.input_json->>'scene_id')=@sceneText
                  AND j.status NOT IN ('pending_reconciliation', 'queued', 'preparing', 'rendering',
                                       'post_processing', 'processing')
                  AND (j.error_code='SCENE_VIDEO_MANUAL_REVIEW_REQUIRED'
                       OR COALESCE(j.input_json, '{}'::jsonb) ? 'sceneVideoManualReviewAt'));
            """, new { tenant = _tenant.TenantId, sceneText = sceneId.ToString() }, tx);
        var token = Guid.NewGuid();
        var inserted = await conn.ExecuteAsync(
            """
            INSERT INTO render.scene_video_submit_intents
                (tenant_id, scene_id, logical_request_id, render_job_id, owner_token)
            VALUES (@tenant, @sceneId, @logicalRequestId, @jobId, @token)
            ON CONFLICT (tenant_id, scene_id, logical_request_id) DO NOTHING;
            """, new { tenant = _tenant.TenantId, sceneId, logicalRequestId, jobId, token }, tx);
        tx.Commit();
        if (inserted != 1) return SceneVideoSubmitClaim.AlreadyClaimed;
        return historicalManualReview
            ? SceneVideoSubmitClaim.ClaimedWithHistoricalManualReview(token)
            : new SceneVideoSubmitClaim(SceneVideoSubmitClaimOutcome.Claimed, token);
    }

    public async Task<bool> BeginSubmitAsync(Guid token, CancellationToken ct = default)
        => await UpdateAsync(token, "SUBMIT_IN_FLIGHT", null, null, "SUBMIT_INTENT_PERSISTED", ct);

    public async Task<bool> ConfirmSubmittedAsync(Guid token, string taskId, string? videoIdBase, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(taskId)) throw new ArgumentException("Provider task ID required.", nameof(taskId));
        return await UpdateAsync(token, "CONFIRMED_PROVIDER_TASK", taskId, videoIdBase, "SUBMIT_IN_FLIGHT", ct);
    }

    public Task<bool> ConfirmNoTaskAsync(Guid token, CancellationToken ct = default)
        => UpdateAsync(token, "CONFIRMED_NO_TASK", null, null, "SUBMIT_IN_FLIGHT", ct);

    /// <summary>
    /// Records that a refused attempt definitely never reached the provider.
    /// Only a persisted intent that is still unsubmitted is closed; an intent that already
    /// carries a provider task, or that is already ambiguous/confirmed, is left untouched so a
    /// real in-flight submit is never downgraded or silently converted into a failure.
    /// </summary>
    public async Task<SceneVideoSubmitState?> TryRecordPreHttpFailureAsync(long sceneId, string logicalRequestId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(logicalRequestId)) return null;
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            """
            UPDATE render.scene_video_submit_intents
               SET submit_state='CONFIRMED_NO_TASK', reconciled_at=COALESCE(reconciled_at, now()), updated_at=now()
             WHERE tenant_id=@tenant AND scene_id=@sceneId AND logical_request_id=@logicalRequestId
               AND submit_state='SUBMIT_INTENT_PERSISTED'
               AND COALESCE(NULLIF(btrim(provider_task_id), ''), NULL) IS NULL;
            """, new { tenant = _tenant.TenantId, sceneId, logicalRequestId });
        return await GetSubmitStateAsync(sceneId, logicalRequestId, ct);
    }

    /// <summary>
    /// Whether an intent that already exists for this logical request proves a provider submit
    /// happened. Callers must re-link to the stored task instead of sending a duplicate request.
    /// </summary>
    public static bool HasProviderTask(SceneVideoSubmitState? state)
        => !string.IsNullOrWhiteSpace(state?.ProviderTaskId)
           || string.Equals(state?.SubmitState, "CONFIRMED_PROVIDER_TASK", StringComparison.Ordinal)
           || string.Equals(state?.SubmitState, "SUBMIT_IN_FLIGHT", StringComparison.Ordinal)
           || string.Equals(state?.SubmitState, "AMBIGUOUS_SUBMIT", StringComparison.Ordinal);

    public Task<bool> MarkAmbiguousAsync(Guid token, CancellationToken ct = default)
        => UpdateAsync(token, "AMBIGUOUS_SUBMIT", null, null, "SUBMIT_IN_FLIGHT", ct);

    public async Task<SceneVideoSubmitState?> GetSubmitStateAsync(long sceneId, string logicalRequestId, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<SceneVideoSubmitState>(
            """
            SELECT submit_state AS SubmitState, owner_token AS OwnerToken,
                   provider_task_id AS ProviderTaskId, provider_video_id_base AS ProviderVideoIdBase
              FROM render.scene_video_submit_intents
             WHERE tenant_id=@tenant AND scene_id=@sceneId AND logical_request_id=@logicalRequestId;
            """, new { tenant = _tenant.TenantId, sceneId, logicalRequestId });
    }

    private async Task<bool> UpdateAsync(Guid token, string next, string? taskId, string? videoIdBase, string expected, CancellationToken ct)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        return await conn.ExecuteAsync(
            """
            UPDATE render.scene_video_submit_intents
               SET submit_state=@next, provider_task_id=COALESCE(@taskId, provider_task_id),
                   provider_video_id_base=COALESCE(@videoIdBase, provider_video_id_base),
                   reconciled_at=CASE WHEN @next IN ('CONFIRMED_PROVIDER_TASK', 'CONFIRMED_NO_TASK') THEN now() ELSE reconciled_at END,
                   updated_at=now()
             WHERE tenant_id=@tenant AND owner_token=@token AND submit_state=@expected;
            """, new { tenant = _tenant.TenantId, token, next, expected, taskId, videoIdBase }) == 1;
    }
}