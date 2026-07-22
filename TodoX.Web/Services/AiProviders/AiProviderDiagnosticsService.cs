using Dapper;
using TodoX.Web.Data;

namespace TodoX.Web.Services.AiProviders;

public sealed class AiProviderAccountDiagnosticsItem
{
    public Guid ProviderAccountId { get; init; }
    public string ProviderCode { get; init; } = string.Empty;
    public string AccountCode { get; init; } = string.Empty;
    public string AccountName { get; init; } = string.Empty;
    public string Environment { get; init; } = "default";
    public bool Enabled { get; init; }
    public bool IsDefault { get; init; }
    public int Priority { get; init; }
    public int Weight { get; init; }
    public int MaxConcurrency { get; init; }
    public int ActiveLeases { get; init; }
    public string HealthStatus { get; init; } = "unknown";
    public DateTimeOffset? CooldownUntil { get; init; }
    public DateTimeOffset? LastSelectedAt { get; init; }
    public DateTimeOffset? LastSuccessAt { get; init; }
    public DateTimeOffset? LastFailureAt { get; init; }
    public decimal? LastKnownBalance { get; init; }
    public decimal? MinimumBalanceThreshold { get; init; }
    public string? BalanceUnit { get; init; }
    public string? CredentialReference { get; init; }
    public decimal UsageQuantity { get; init; }
    public decimal? ProviderCost { get; init; }
    public string? ProviderCurrency { get; init; }
}

public interface IAiProviderDiagnosticsService
{
    Task<IReadOnlyList<AiProviderAccountDiagnosticsItem>> ListAccountsAsync(string? providerCode = null, CancellationToken ct = default);
    Task<bool> ExpireLeaseAsync(Guid leaseId, string workerKey, CancellationToken ct = default);
    Task<ResolvedAiProviderCredential> TestCredentialReferenceAsync(Guid providerAccountId, CancellationToken ct = default);
}

public sealed class AiProviderDiagnosticsService : IAiProviderDiagnosticsService
{
    private readonly TodoXConnectionFactory _factory;
    private readonly IAiProviderAccountRepository _accounts;
    private readonly IAiProviderCredentialResolver _credentials;

    public AiProviderDiagnosticsService(
        TodoXConnectionFactory factory,
        IAiProviderAccountRepository accounts,
        IAiProviderCredentialResolver credentials)
    {
        _factory = factory;
        _accounts = accounts;
        _credentials = credentials;
    }

    public async Task<IReadOnlyList<AiProviderAccountDiagnosticsItem>> ListAccountsAsync(string? providerCode = null, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<AiProviderAccountDiagnosticsItem>(
            """
            SELECT a.id AS ProviderAccountId,
                   a.provider_code AS ProviderCode,
                   a.account_code AS AccountCode,
                   a.account_name AS AccountName,
                   COALESCE(a.environment, a.config_json->>'environment', 'default') AS Environment,
                   a.enabled AS Enabled,
                   a.is_default AS IsDefault,
                   a.priority AS Priority,
                   a.weight AS Weight,
                   a.max_concurrency AS MaxConcurrency,
                   COALESCE(l.active_leases, 0)::int AS ActiveLeases,
                   a.health_status AS HealthStatus,
                   a.cooldown_until AS CooldownUntil,
                   a.last_selected_at AS LastSelectedAt,
                   a.last_success_at AS LastSuccessAt,
                   a.last_failure_at AS LastFailureAt,
                   a.last_known_balance AS LastKnownBalance,
                   a.minimum_balance_threshold AS MinimumBalanceThreshold,
                   COALESCE(a.balance_unit, a.config_json->>'balanceUnit') AS BalanceUnit,
                   COALESCE(c.credential_key, c.credential_config_name) AS CredentialReference,
                   COALESCE(u.usage_quantity, 0) AS UsageQuantity,
                   u.provider_cost AS ProviderCost,
                   u.provider_currency AS ProviderCurrency
              FROM public.todox_ai_provider_account a
              LEFT JOIN LATERAL (
                    SELECT count(*) AS active_leases
                      FROM public.todox_ai_provider_account_lease l
                     WHERE l.provider_account_id = a.id
                       AND l.lease_status='active'
                       AND l.lease_until > now()
              ) l ON true
              LEFT JOIN LATERAL (
                    SELECT credential_key, credential_config_name
                      FROM public.todox_ai_provider_account_credential c
                     WHERE c.provider_account_id=a.id
                       AND c.enabled
                     ORDER BY c.priority, c.credential_role
                     LIMIT 1
              ) c ON true
              LEFT JOIN LATERAL (
                    SELECT sum(quantity) AS usage_quantity,
                           sum(provider_raw_cost) AS provider_cost,
                           max(provider_cost_currency) AS provider_currency
                      FROM public.todox_ai_provider_usage_log u
                     WHERE u.provider_account_id = a.id
              ) u ON true
             WHERE (@providerCode IS NULL OR a.provider_code=@providerCode)
             ORDER BY a.provider_code, a.priority, a.account_code;
            """,
            new { providerCode = string.IsNullOrWhiteSpace(providerCode) ? null : providerCode.Trim() });
        return rows.ToList();
    }

    public Task<bool> ExpireLeaseAsync(Guid leaseId, string workerKey, CancellationToken ct = default)
        => _accounts.ReleaseLeaseAsync(leaseId, workerKey, "admin_expired", ct);

    public async Task<ResolvedAiProviderCredential> TestCredentialReferenceAsync(Guid providerAccountId, CancellationToken ct = default)
    {
        var resolved = await _credentials.ResolveAsync(providerAccountId, ct: ct);
        return new ResolvedAiProviderCredential(resolved.ProviderAccountId, resolved.CredentialRole, resolved.ReferenceName, "[redacted]");
    }
}

public sealed class AiProviderBalanceLedgerEntry
{
    public Guid ProviderAccountId { get; init; }
    public string TransactionType { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public decimal? BalanceBefore { get; init; }
    public decimal? BalanceAfter { get; init; }
    public string Unit { get; init; } = "credits";
    public string Source { get; init; } = "manual";
    public string? ReferenceType { get; init; }
    public Guid? ReferenceId { get; init; }
    public string? ProviderTransactionId { get; init; }
    public string IdempotencyKey { get; init; } = string.Empty;
    public string? MetadataJson { get; init; }
    public Guid? CreatedBy { get; init; }
}

public interface IAiProviderBalanceService
{
    Task<Guid> RecordAsync(AiProviderBalanceLedgerEntry entry, CancellationToken ct = default);
}

public sealed class AiProviderBalanceService : IAiProviderBalanceService
{
    private static readonly HashSet<string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "opening_balance", "top_up", "usage_charge", "refund", "manual_adjustment", "provider_sync"
    };

    private readonly TodoXConnectionFactory _factory;

    public AiProviderBalanceService(TodoXConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<Guid> RecordAsync(AiProviderBalanceLedgerEntry entry, CancellationToken ct = default)
    {
        if (!AllowedTypes.Contains(entry.TransactionType))
        {
            throw new InvalidOperationException("AI_PROVIDER_BALANCE_LEDGER_INVALID_TYPE");
        }

        using var conn = await _factory.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<Guid>(
            """
            INSERT INTO public.todox_ai_provider_balance_ledger
                (id, provider_account_id, transaction_type, amount, balance_before, balance_after,
                 unit, source, reference_type, reference_id, provider_transaction_id, idempotency_key,
                 metadata_json, created_by, created_at)
            VALUES
                (gen_random_uuid(), @ProviderAccountId, @TransactionType, @Amount, @BalanceBefore, @BalanceAfter,
                 @Unit, @Source, @ReferenceType, @ReferenceId, @ProviderTransactionId, @IdempotencyKey,
                 COALESCE(CAST(@MetadataJson AS jsonb), '{}'::jsonb), @CreatedBy, now())
            ON CONFLICT (idempotency_key) DO UPDATE
                SET balance_after = COALESCE(EXCLUDED.balance_after, public.todox_ai_provider_balance_ledger.balance_after),
                    metadata_json = public.todox_ai_provider_balance_ledger.metadata_json || EXCLUDED.metadata_json
            RETURNING id;
            """,
            entry);
    }
}
