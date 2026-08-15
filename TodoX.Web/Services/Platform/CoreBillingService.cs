using System.Data;
using System.Text.Json;
using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Models.Catalog;
using TodoX.Web.Services.Render;

namespace TodoX.Web.Services.Platform;

public sealed record CoreBillingEstimate(
    decimal EstimatedPoints,
    bool ChargeRequired,
    string QualityTier,
    int ImageCount,
    int SceneCount,
    int? DurationSeconds,
    string? Message);

public sealed record CoreBillingReservation(
    bool Success,
    string PointStatus,
    decimal ReservedPoints,
    string? ErrorMessage);

public sealed record CoreBillingCompletion(
    bool Success,
    string PointStatus,
    decimal ChargedPoints,
    string? ErrorMessage);

public sealed record CoreBillingState(
    Guid JobId,
    decimal EstimatedPoints,
    decimal ChargedPoints,
    string PointStatus);

public interface ICoreBillingService
{
    Task<CoreBillingEstimate> EstimateAsync(
        CoreRequestContext context,
        CoreServiceView service,
        JsonElement input,
        CancellationToken ct = default);

    Task<CoreBillingReservation> ReserveAsync(
        Guid jobId,
        CoreRequestContext context,
        CoreBillingEstimate estimate,
        CancellationToken ct = default);

    Task<CoreBillingCompletion> CompleteAsync(Guid jobId, CancellationToken ct = default);

    Task<CoreBillingCompletion> RefundOrReleaseAsync(
        Guid jobId,
        string reason,
        bool markCancelled,
        CancellationToken ct = default);

    Task<CoreBillingState?> GetBillingStateAsync(Guid jobId, CancellationToken ct = default);
}

/// <summary>
/// Provider-neutral billing lifecycle for canonical Core jobs. The canonical render job row owns
/// billing state; wallet balance changes and job point_status transitions occur in one transaction.
/// </summary>
public sealed class CoreBillingService : ICoreBillingService
{
    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;
    private readonly IServiceSellPriceResolver _prices;
    private readonly WalletService _wallets;

    public CoreBillingService(
        TodoXConnectionFactory factory,
        TenantContext tenant,
        IServiceSellPriceResolver prices,
        WalletService wallets)
    {
        _factory = factory;
        _tenant = tenant;
        _prices = prices;
        _wallets = wallets;
    }

    public async Task<CoreBillingEstimate> EstimateAsync(
        CoreRequestContext context,
        CoreServiceView service,
        JsonElement input,
        CancellationToken ct = default)
    {
        var activePrices = await _prices.GetActivePricesAsync(service.Id, ct);
        if (activePrices.Count == 0)
        {
            return new CoreBillingEstimate(0, false, ServiceSellPriceQualityTiers.Standard, 0, 0, null, "Free service.");
        }

        var qualityTier = ResolveQualityTier(input);
        var imageCount = ReadInt(input, "imageCount", "image_count") ?? 0;
        var sceneCount = ReadInt(input, "sceneCount", "scene_count") ?? 0;
        var durationSeconds = ReadInt(input, "durationSeconds", "duration_seconds", "sceneDurationSeconds", "scene_duration_seconds");

        if (imageCount <= 0 && sceneCount <= 0)
        {
            throw new InvalidOperationException(
                "Billable service input must include imageCount/image_count or sceneCount/scene_count.");
        }

        var estimate = await _prices.EstimateAsync(new ServiceSellPriceEstimateRequest(
            service.Id,
            qualityTier,
            durationSeconds,
            sceneCount,
            imageCount), ct);

        if (!estimate.Success)
        {
            throw new InvalidOperationException(estimate.Message ?? "TodoX service price could not be estimated.");
        }

        var trustedNoCharge = context.IsTrustedInternal
            && context.NormalizedChannel == CoreChannelCodes.System;
        return new CoreBillingEstimate(
            estimate.TotalPoints,
            ChargeRequired: !trustedNoCharge && context.CustomerId is not null && estimate.TotalPoints > 0,
            qualityTier,
            imageCount,
            sceneCount,
            durationSeconds,
            trustedNoCharge ? "Trusted internal job is billing-exempt." : null);
    }

    public async Task<CoreBillingReservation> ReserveAsync(
        Guid jobId,
        CoreRequestContext context,
        CoreBillingEstimate estimate,
        CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);

        if (!estimate.ChargeRequired || estimate.EstimatedPoints <= 0)
        {
            using var noChargeConn = await _factory.OpenAsync(ct);
            using var noChargeTx = noChargeConn.BeginTransaction();
            await LockJobAsync(noChargeConn, noChargeTx, _tenant.TenantId, jobId);
            await noChargeConn.ExecuteAsync(
                """
                UPDATE render.render_jobs
                   SET status='queued',
                       current_step='queued',
                       point_status='not_required',
                       updated_at=now()
                 WHERE id=@jobId
                   AND tenant_id=@tenant
                   AND job_type=@jobType
                   AND status='draft';
                """,
                new { jobId, tenant = _tenant.TenantId, jobType = RenderJobTypes.CoreService },
                noChargeTx);
            await AddBillingEventAsync(
                noChargeConn,
                noChargeTx,
                jobId,
                "CORE_BILLING_NOT_REQUIRED",
                "Core job does not require a customer charge.",
                new { estimate.EstimatedPoints, context.NormalizedChannel });
            noChargeTx.Commit();
            return new CoreBillingReservation(true, RenderPointStatuses.NotRequired, 0, null);
        }

        if (context.CustomerId is not Guid customerId)
        {
            return new CoreBillingReservation(false, RenderPointStatuses.Insufficient, 0, "Customer wallet identity is required.");
        }

        var walletId = await _wallets.EnsureWalletAsync(customerId);
        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        await LockJobAsync(conn, tx, _tenant.TenantId, jobId);

        var job = await GetJobForUpdateAsync(conn, tx, _tenant.TenantId, jobId);
        if (job is null || job.CustomerId != customerId)
        {
            tx.Commit();
            return new CoreBillingReservation(false, RenderPointStatuses.Insufficient, 0, "Core job billing scope is invalid.");
        }

        if (job.PointStatus == RenderPointStatuses.Charged
            || (job.PointStatus == RenderPointStatuses.Pending && job.Status == RenderJobStatuses.Queued))
        {
            tx.Commit();
            return new CoreBillingReservation(true, job.PointStatus, job.PointCostEstimate, null);
        }

        if (job.PointStatus == RenderPointStatuses.Insufficient)
        {
            tx.Commit();
            return new CoreBillingReservation(false, job.PointStatus, 0, job.ErrorMessage ?? "Insufficient points.");
        }

        var wallet = await conn.QuerySingleAsync<WalletRow>(
            """
            SELECT id AS Id, balance AS Balance, locked_balance AS LockedBalance
              FROM billing.token_wallets
             WHERE id=@walletId
               AND customer_id=@customerId
               AND tenant_id=@tenant
             FOR UPDATE;
            """,
            new { walletId, customerId, tenant = _tenant.TenantId },
            tx);

        if (wallet.Balance < estimate.EstimatedPoints)
        {
            var message = $"Insufficient TodoX points. Required {estimate.EstimatedPoints:0.####}, available {wallet.Balance:0.####}.";
            await conn.ExecuteAsync(
                """
                UPDATE render.render_jobs
                   SET status='failed',
                       current_step='billing',
                       point_status='insufficient',
                       error_code='insufficient_points',
                       error_message=@message,
                       completed_at=now(),
                       updated_at=now()
                 WHERE id=@jobId
                   AND tenant_id=@tenant;
                """,
                new { jobId, tenant = _tenant.TenantId, message },
                tx);
            await AddBillingEventAsync(conn, tx, jobId, "CORE_BILLING_INSUFFICIENT", message,
                new { required = estimate.EstimatedPoints, available = wallet.Balance });
            tx.Commit();
            return new CoreBillingReservation(false, RenderPointStatuses.Insufficient, 0, message);
        }

        await conn.ExecuteAsync(
            """
            UPDATE billing.token_wallets
               SET balance=balance-@amount,
                   locked_balance=locked_balance+@amount,
                   updated_at=now()
             WHERE id=@walletId;

            UPDATE render.render_jobs
               SET status='queued',
                   current_step='queued',
                   point_status='pending',
                   error_code=NULL,
                   error_message=NULL,
                   updated_at=now()
             WHERE id=@jobId
               AND tenant_id=@tenant;
            """,
            new { amount = estimate.EstimatedPoints, walletId, jobId, tenant = _tenant.TenantId },
            tx);
        await AddBillingEventAsync(conn, tx, jobId, "CORE_BILLING_RESERVED", "Core job points reserved.",
            new { points = estimate.EstimatedPoints });
        tx.Commit();
        return new CoreBillingReservation(true, RenderPointStatuses.Pending, estimate.EstimatedPoints, null);
    }

    public async Task<CoreBillingCompletion> CompleteAsync(Guid jobId, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        await LockJobAsync(conn, tx, _tenant.TenantId, jobId);

        var job = await GetJobForUpdateAsync(conn, tx, _tenant.TenantId, jobId);
        if (job is null)
        {
            tx.Commit();
            return new CoreBillingCompletion(false, "missing", 0, "Core job was not found.");
        }

        if (job.PointStatus == RenderPointStatuses.Charged)
        {
            tx.Commit();
            return new CoreBillingCompletion(true, job.PointStatus, job.PointCostCharged, null);
        }

        if (job.PointStatus == RenderPointStatuses.NotRequired)
        {
            await MarkJobCompletedAsync(
                conn,
                tx,
                _tenant.TenantId,
                jobId,
                chargedPoints: 0,
                RenderPointStatuses.NotRequired);
            tx.Commit();
            return new CoreBillingCompletion(true, RenderPointStatuses.NotRequired, 0, null);
        }

        if (job.PointStatus != RenderPointStatuses.Pending || job.CustomerId is not Guid customerId)
        {
            tx.Commit();
            return new CoreBillingCompletion(false, job.PointStatus, job.PointCostCharged,
                $"Core job cannot complete billing from point status '{job.PointStatus}'.");
        }

        var wallet = await GetWalletForUpdateAsync(conn, tx, customerId);
        var release = Math.Min(wallet.LockedBalance, job.PointCostEstimate);
        await conn.ExecuteAsync(
            "UPDATE billing.token_wallets SET locked_balance=locked_balance-@release, updated_at=now() WHERE id=@walletId;",
            new { release, walletId = wallet.Id },
            tx);

        var transactionId = Guid.NewGuid();
        await InsertWalletTransactionAsync(
            conn,
            tx,
            transactionId,
            wallet,
            "debit",
            job.PointCostEstimate,
            wallet.Balance + job.PointCostEstimate,
            wallet.Balance,
            job.Id,
            job.UserId,
            "Core service job charge.");
        await MarkJobCompletedAsync(
            conn,
            tx,
            _tenant.TenantId,
            jobId,
            job.PointCostEstimate,
            RenderPointStatuses.Charged);
        await AddBillingEventAsync(conn, tx, jobId, "CORE_BILLING_CHARGED", "Core job points charged.",
            new { points = job.PointCostEstimate, transactionId });
        tx.Commit();
        return new CoreBillingCompletion(true, RenderPointStatuses.Charged, job.PointCostEstimate, null);
    }

    public async Task<CoreBillingCompletion> RefundOrReleaseAsync(
        Guid jobId,
        string reason,
        bool markCancelled,
        CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        await LockJobAsync(conn, tx, _tenant.TenantId, jobId);

        var job = await GetJobForUpdateAsync(conn, tx, _tenant.TenantId, jobId);
        if (job is null)
        {
            tx.Commit();
            return new CoreBillingCompletion(false, "missing", 0, "Core job was not found.");
        }

        if (markCancelled && job.Status is RenderJobStatuses.Completed or RenderJobStatuses.Failed or RenderJobStatuses.Cancelled)
        {
            tx.Commit();
            return new CoreBillingCompletion(false, job.PointStatus, job.PointCostCharged,
                $"Terminal job '{job.Status}' cannot be cancelled.");
        }

        if (job.PointStatus is RenderPointStatuses.Cancelled or RenderPointStatuses.Refunded)
        {
            tx.Commit();
            return new CoreBillingCompletion(true, job.PointStatus, job.PointCostCharged, null);
        }

        var nextPointStatus = job.PointStatus;
        if (job.CustomerId is Guid customerId && job.PointCostEstimate > 0)
        {
            var wallet = await GetWalletForUpdateAsync(conn, tx, customerId);
            if (job.PointStatus == RenderPointStatuses.Pending)
            {
                var release = Math.Min(wallet.LockedBalance, job.PointCostEstimate);
                await conn.ExecuteAsync(
                    """
                    UPDATE billing.token_wallets
                       SET balance=balance+@release,
                           locked_balance=locked_balance-@release,
                           updated_at=now()
                     WHERE id=@walletId;
                    """,
                    new { release, walletId = wallet.Id },
                    tx);
                nextPointStatus = RenderPointStatuses.Cancelled;
            }
            else if (job.PointStatus == RenderPointStatuses.Charged)
            {
                var balanceBefore = wallet.Balance;
                var balanceAfter = balanceBefore + job.PointCostCharged;
                await conn.ExecuteAsync(
                    "UPDATE billing.token_wallets SET balance=@balanceAfter, updated_at=now() WHERE id=@walletId;",
                    new { balanceAfter, walletId = wallet.Id },
                    tx);
                await InsertWalletTransactionAsync(
                    conn,
                    tx,
                    Guid.NewGuid(),
                    wallet,
                    "credit",
                    job.PointCostCharged,
                    balanceBefore,
                    balanceAfter,
                    job.Id,
                    job.UserId,
                    "Core service job refund.");
                nextPointStatus = RenderPointStatuses.Refunded;
            }
        }

        var nextStatus = markCancelled ? RenderJobStatuses.Cancelled : job.Status;
        await conn.ExecuteAsync(
            """
            UPDATE render.render_jobs
               SET status=@status,
                   current_step=CASE WHEN @markCancelled THEN 'cancelled' ELSE current_step END,
                   point_status=@pointStatus,
                   cancel_reason=CASE WHEN @markCancelled THEN @reason ELSE cancel_reason END,
                   cancelled_at=CASE WHEN @markCancelled THEN now() ELSE cancelled_at END,
                   completed_at=CASE WHEN @markCancelled THEN COALESCE(completed_at, now()) ELSE completed_at END,
                   updated_at=now()
             WHERE id=@jobId
               AND tenant_id=@tenant;
            """,
            new
            {
                jobId,
                tenant = _tenant.TenantId,
                status = nextStatus,
                pointStatus = nextPointStatus,
                reason,
                markCancelled
            },
            tx);
        await AddBillingEventAsync(
            conn,
            tx,
            jobId,
            nextPointStatus == RenderPointStatuses.Refunded ? "CORE_BILLING_REFUNDED" : "CORE_BILLING_RELEASED",
            reason,
            new { pointStatus = nextPointStatus, markCancelled });
        tx.Commit();
        return new CoreBillingCompletion(true, nextPointStatus, job.PointCostCharged, null);
    }

    public async Task<CoreBillingState?> GetBillingStateAsync(Guid jobId, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<CoreBillingState>(
            """
            SELECT id AS JobId,
                   point_cost_estimate AS EstimatedPoints,
                   point_cost_charged AS ChargedPoints,
                   point_status AS PointStatus
             FROM render.render_jobs
             WHERE id=@jobId
               AND tenant_id=@tenant
               AND job_type=@jobType;
            """,
            new { jobId, tenant = _tenant.TenantId, jobType = RenderJobTypes.CoreService });
    }

    internal static string ResolveQualityTier(JsonElement input)
    {
        var explicitTier = ReadString(input, "qualityTier", "quality_tier");
        if (ServiceSellPriceQualityTiers.IsValid(explicitTier))
        {
            return explicitTier!.Trim().ToLowerInvariant();
        }

        var mode = ReadString(input, "videoMode", "video_mode", "mode");
        return mode?.Trim().ToLowerInvariant() is "professional" or "premium"
            ? ServiceSellPriceQualityTiers.Premium
            : ServiceSellPriceQualityTiers.Standard;
    }

    private async Task<WalletRow> GetWalletForUpdateAsync(IDbConnection conn, IDbTransaction tx, Guid customerId)
        => await conn.QuerySingleAsync<WalletRow>(
            """
            SELECT id AS Id, balance AS Balance, locked_balance AS LockedBalance
              FROM billing.token_wallets
             WHERE tenant_id=@tenant
               AND customer_id=@customerId
             LIMIT 1
             FOR UPDATE;
            """,
            new { tenant = _tenant.TenantId, customerId },
            tx);

    private async Task AddBillingEventAsync(
        IDbConnection conn,
        IDbTransaction tx,
        Guid jobId,
        string eventType,
        string message,
        object data)
        => await conn.ExecuteAsync(
            """
            INSERT INTO render.render_job_events
                (job_id, tenant_id, event_type, level, message, data_json, created_at)
            VALUES
                (@jobId, @tenant, @eventType, 'info', @message, CAST(@data AS jsonb), now());
            """,
            new
            {
                jobId,
                tenant = _tenant.TenantId,
                eventType,
                message,
                data = JsonSerializer.Serialize(data)
            },
            tx);

    private static Task LockJobAsync(
        IDbConnection conn,
        IDbTransaction tx,
        Guid tenantId,
        Guid jobId)
        => conn.ExecuteAsync(
            "SELECT pg_advisory_xact_lock(hashtextextended(@lockName, 0));",
            new { lockName = $"core-billing:{tenantId:N}:{jobId:N}" },
            tx);

    private static Task<CoreBillingJobRow?> GetJobForUpdateAsync(
        IDbConnection conn,
        IDbTransaction tx,
        Guid tenantId,
        Guid jobId)
        => conn.QuerySingleOrDefaultAsync<CoreBillingJobRow>(
            """
            SELECT id AS Id,
                   customer_id AS CustomerId,
                   user_id AS UserId,
                   status AS Status,
                   point_cost_estimate AS PointCostEstimate,
                   point_cost_charged AS PointCostCharged,
                   point_status AS PointStatus,
                   error_message AS ErrorMessage
             FROM render.render_jobs
             WHERE id=@jobId
               AND tenant_id=@tenantId
               AND job_type=@jobType
             FOR UPDATE;
            """,
            new { jobId, tenantId, jobType = RenderJobTypes.CoreService },
            tx);

    private static Task MarkJobCompletedAsync(
        IDbConnection conn,
        IDbTransaction tx,
        Guid tenantId,
        Guid jobId,
        decimal chargedPoints,
        string pointStatus)
        => conn.ExecuteAsync(
            """
            UPDATE render.render_jobs
               SET status='completed',
                   current_step='completed',
                   progress_percent=100,
                   point_cost_charged=@chargedPoints,
                   point_status=@pointStatus,
                   completed_at=COALESCE(completed_at, now()),
                   updated_at=now()
             WHERE id=@jobId
               AND tenant_id=@tenantId;
            """,
            new { jobId, tenantId, chargedPoints, pointStatus },
            tx);

    private async Task InsertWalletTransactionAsync(
        IDbConnection conn,
        IDbTransaction tx,
        Guid transactionId,
        WalletRow wallet,
        string transactionType,
        decimal amount,
        decimal balanceBefore,
        decimal balanceAfter,
        Guid jobId,
        Guid? userId,
        string description)
        => await conn.ExecuteAsync(
            """
            INSERT INTO billing.token_transactions
                (id, tenant_id, wallet_id, transaction_type, amount, balance_before, balance_after,
                 reference_type, reference_id, description, created_at, created_by)
            VALUES
                (@transactionId, @tenant, @walletId, @transactionType, @amount, @balanceBefore, @balanceAfter,
                 'core_service_job', @jobId, @description, now(), @userId);
            """,
            new
            {
                transactionId,
                tenant = _tenant.TenantId,
                walletId = wallet.Id,
                transactionType,
                amount,
                balanceBefore,
                balanceAfter,
                jobId,
                description,
                userId
            },
            tx);

    private static string? ReadString(JsonElement input, params string[] names)
    {
        if (input.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in input.EnumerateObject())
        {
            if (names.Any(name => string.Equals(name, property.Name, StringComparison.OrdinalIgnoreCase))
                && property.Value.ValueKind == JsonValueKind.String)
            {
                return property.Value.GetString();
            }
        }

        return null;
    }

    private static int? ReadInt(JsonElement input, params string[] names)
    {
        if (input.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in input.EnumerateObject())
        {
            if (!names.Any(name => string.Equals(name, property.Name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (property.Value.TryGetInt32(out var number))
            {
                return Math.Max(0, number);
            }

            if (property.Value.ValueKind == JsonValueKind.String
                && int.TryParse(property.Value.GetString(), out number))
            {
                return Math.Max(0, number);
            }
        }

        return null;
    }

    private sealed class WalletRow
    {
        public Guid Id { get; init; }
        public decimal Balance { get; init; }
        public decimal LockedBalance { get; init; }
    }

    private sealed class CoreBillingJobRow
    {
        public Guid Id { get; init; }
        public Guid? CustomerId { get; init; }
        public Guid? UserId { get; init; }
        public string Status { get; init; } = string.Empty;
        public decimal PointCostEstimate { get; init; }
        public decimal PointCostCharged { get; init; }
        public string PointStatus { get; init; } = RenderPointStatuses.NotRequired;
        public string? ErrorMessage { get; init; }
    }
}
