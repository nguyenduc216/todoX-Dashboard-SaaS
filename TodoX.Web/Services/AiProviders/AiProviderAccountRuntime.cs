using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using TodoX.Web.Data;

namespace TodoX.Web.Services.AiProviders;

public sealed record AiProviderAccountSelectionRequest(
    Guid RenderJobId,
    string ProviderCode,
    string CapabilityCode,
    string OperationType,
    string? ModelName,
    string WorkerKey,
    TimeSpan LeaseFor,
    decimal? MinimumKnownBalance = null);

public sealed record AiProviderAccountSelectionResult(
    bool Claimed,
    Guid? LeaseId,
    Guid? ProviderAccountId,
    long? ProviderId,
    long? ProviderCapabilityId,
    string? ProviderCode,
    string? AccountCode,
    string? CredentialConfigName,
    string? Reason);

public sealed class AiProviderAccountDto
{
    public Guid Id { get; set; }
    public long ProviderId { get; set; }
    public string ProviderCode { get; set; } = string.Empty;
    public string AccountCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public bool IsDefault { get; set; }
    public int Priority { get; set; }
    public int Weight { get; set; }
    public int MaxConcurrency { get; set; }
    public decimal? LastKnownBalance { get; set; }
    public decimal? MinimumBalanceThreshold { get; set; }
    public string HealthStatus { get; set; } = "unknown";
    public DateTimeOffset? CooldownUntil { get; set; }
}

public sealed class AiProviderAccountCredentialDto
{
    public Guid Id { get; set; }
    public Guid ProviderAccountId { get; set; }
    public Guid? CredentialId { get; set; }
    public string? CredentialKey { get; set; }
    public string? CredentialConfigName { get; set; }
    public string CredentialRole { get; set; } = "api_key";
    public bool Enabled { get; set; }
    public int Priority { get; set; }
}

public sealed record ResolvedAiProviderCredential(
    Guid ProviderAccountId,
    string CredentialRole,
    string ReferenceName,
    string SecretValue);

public interface IAiProviderAccountRepository
{
    Task<IReadOnlyList<AiProviderAccountDto>> ListAccountsAsync(string providerCode, CancellationToken ct = default);
    Task<IReadOnlyList<AiProviderAccountCredentialDto>> ListCredentialsAsync(Guid providerAccountId, CancellationToken ct = default);
    Task<AiProviderAccountSelectionResult> ClaimAccountAsync(AiProviderAccountSelectionRequest request, CancellationToken ct = default);
    Task<bool> HeartbeatLeaseAsync(Guid leaseId, string workerKey, TimeSpan extendFor, CancellationToken ct = default);
    Task<bool> ReleaseLeaseAsync(Guid leaseId, string workerKey, string reason, CancellationToken ct = default);
    Task<int> ExpireLeasesAsync(DateTimeOffset nowUtc, CancellationToken ct = default);
    Task MarkAccountSuccessAsync(Guid providerAccountId, CancellationToken ct = default);
    Task MarkAccountFailureAsync(Guid providerAccountId, TimeSpan? cooldown, CancellationToken ct = default);
    Task UpdateBalanceSnapshotAsync(Guid providerAccountId, decimal? balance, string source, CancellationToken ct = default);
}

public sealed class AiProviderAccountRepository : IAiProviderAccountRepository
{
    public const string ClaimAccountSql =
        """
        WITH candidate AS (
            SELECT a.id,
                   a.provider_id,
                   a.provider_code,
                   a.account_code,
                   a.max_concurrency,
                   c.id AS provider_capability_id,
                   COUNT(l.id) FILTER (WHERE l.lease_status = 'active' AND l.lease_until > now()) AS active_lease_count
              FROM public.todox_ai_provider_account a
              JOIN public.todox_ai_provider p ON p.id = a.provider_id AND p.enabled = true
              JOIN public.todox_ai_provider_capability c ON c.provider_id = p.id
             WHERE a.provider_code = @ProviderCode
               AND a.enabled = true
               AND c.enabled = true
               AND c.capability_code = @CapabilityCode
               AND (@ModelName IS NULL OR c.model_name = @ModelName)
               AND (@OperationType IS NULL OR c.operation_type IS NULL OR c.operation_type = @OperationType)
               AND (a.cooldown_until IS NULL OR a.cooldown_until <= now())
               AND a.health_status NOT IN ('disabled','exhausted')
               AND (@MinimumKnownBalance IS NULL
                    OR a.last_known_balance IS NULL
                    OR a.last_known_balance >= GREATEST(COALESCE(a.minimum_balance_threshold, 0), @MinimumKnownBalance))
             GROUP BY a.id, a.provider_id, a.provider_code, a.account_code, a.priority, a.weight,
                      a.max_concurrency, a.last_selected_at, a.health_status, c.id
            HAVING COUNT(l.id) FILTER (WHERE l.lease_status = 'active' AND l.lease_until > now()) < a.max_concurrency
             ORDER BY a.priority ASC,
                      CASE a.health_status WHEN 'healthy' THEN 0 WHEN 'unknown' THEN 1 WHEN 'degraded' THEN 2 ELSE 3 END,
                      (COUNT(l.id) FILTER (WHERE l.lease_status = 'active' AND l.lease_until > now())::numeric / a.max_concurrency) ASC,
                      a.last_selected_at NULLS FIRST,
                      a.weight DESC,
                      a.account_code ASC
             FOR UPDATE OF a SKIP LOCKED
             LIMIT 1
        ),
        inserted AS (
            INSERT INTO public.todox_ai_provider_account_lease
                (id, provider_account_id, render_job_id, worker_key, lease_status, lease_until, heartbeat_at, metadata_json, created_at, updated_at)
            SELECT gen_random_uuid(), id, @RenderJobId, @WorkerKey, 'active',
                   now() + (@LeaseSeconds || ' seconds')::interval, now(),
                   jsonb_build_object('capabilityCode', @CapabilityCode, 'operationType', @OperationType, 'modelName', @ModelName),
                   now(), now()
              FROM candidate
            ON CONFLICT DO NOTHING
            RETURNING id, provider_account_id
        )
        UPDATE public.todox_ai_provider_account a
           SET last_selected_at = now(),
               updated_at = now()
          FROM candidate c
          JOIN inserted i ON i.provider_account_id = c.id
         WHERE a.id = c.id
         RETURNING i.id AS LeaseId,
                   a.id AS ProviderAccountId,
                   a.provider_id AS ProviderId,
                   c.provider_capability_id AS ProviderCapabilityId,
                   a.provider_code AS ProviderCode,
                   a.account_code AS AccountCode,
                   COALESCE((a.config_json->>'credential_config_name'), '') AS CredentialConfigName;
        """;

    private readonly TodoXConnectionFactory _factory;

    public AiProviderAccountRepository(TodoXConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<IReadOnlyList<AiProviderAccountDto>> ListAccountsAsync(string providerCode, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<AiProviderAccountDto>(
            """
            SELECT id AS Id, provider_id AS ProviderId, provider_code AS ProviderCode, account_code AS AccountCode,
                   account_name AS AccountName, enabled AS Enabled, is_default AS IsDefault, priority AS Priority,
                   weight AS Weight, max_concurrency AS MaxConcurrency, last_known_balance AS LastKnownBalance,
                   minimum_balance_threshold AS MinimumBalanceThreshold, health_status AS HealthStatus,
                   cooldown_until AS CooldownUntil
              FROM public.todox_ai_provider_account
             WHERE provider_code = @providerCode
             ORDER BY priority, account_code;
            """,
            new { providerCode });
        return rows.ToList();
    }

    public async Task<IReadOnlyList<AiProviderAccountCredentialDto>> ListCredentialsAsync(Guid providerAccountId, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<AiProviderAccountCredentialDto>(
            """
            SELECT id AS Id, provider_account_id AS ProviderAccountId, credential_id AS CredentialId,
                   credential_key AS CredentialKey, credential_config_name AS CredentialConfigName,
                   credential_role AS CredentialRole, enabled AS Enabled, priority AS Priority
              FROM public.todox_ai_provider_account_credential
             WHERE provider_account_id = @providerAccountId
             ORDER BY priority, credential_role;
            """,
            new { providerAccountId });
        return rows.ToList();
    }

    public async Task<AiProviderAccountSelectionResult> ClaimAccountAsync(AiProviderAccountSelectionRequest request, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        var row = await conn.QuerySingleOrDefaultAsync<ClaimRow>(
            ClaimAccountSql,
            new
            {
                request.RenderJobId,
                request.ProviderCode,
                request.CapabilityCode,
                OperationType = request.OperationType,
                request.ModelName,
                request.WorkerKey,
                request.MinimumKnownBalance,
                LeaseSeconds = Math.Max(1, (int)request.LeaseFor.TotalSeconds)
            },
            tx);
        tx.Commit();
        return row is null
            ? new AiProviderAccountSelectionResult(false, null, null, null, null, null, null, null, "NO_ELIGIBLE_PROVIDER_ACCOUNT")
            : new AiProviderAccountSelectionResult(true, row.LeaseId, row.ProviderAccountId, row.ProviderId, row.ProviderCapabilityId, row.ProviderCode, row.AccountCode, row.CredentialConfigName, null);
    }

    public async Task<bool> HeartbeatLeaseAsync(Guid leaseId, string workerKey, TimeSpan extendFor, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var changed = await conn.ExecuteAsync(
            """
            UPDATE public.todox_ai_provider_account_lease
               SET heartbeat_at = now(),
                   lease_until = now() + (@seconds || ' seconds')::interval,
                   updated_at = now()
             WHERE id = @leaseId
               AND worker_key = @workerKey
               AND lease_status = 'active';
            """,
            new { leaseId, workerKey, seconds = Math.Max(1, (int)extendFor.TotalSeconds) });
        return changed > 0;
    }

    public async Task<bool> ReleaseLeaseAsync(Guid leaseId, string workerKey, string reason, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var changed = await conn.ExecuteAsync(
            """
            UPDATE public.todox_ai_provider_account_lease
               SET lease_status = 'released',
                   released_at = COALESCE(released_at, now()),
                   release_reason = @reason,
                   updated_at = now()
             WHERE id = @leaseId
               AND worker_key = @workerKey
               AND lease_status = 'active';
            """,
            new { leaseId, workerKey, reason });
        return changed > 0;
    }

    public async Task<int> ExpireLeasesAsync(DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        return await conn.ExecuteAsync(
            """
            UPDATE public.todox_ai_provider_account_lease
               SET lease_status = 'expired',
                   release_reason = COALESCE(release_reason, 'watchdog_expired'),
                   released_at = COALESCE(released_at, now()),
                   updated_at = now()
             WHERE lease_status = 'active'
               AND lease_until < @nowUtc;
            """,
            new { nowUtc });
    }

    public async Task MarkAccountSuccessAsync(Guid providerAccountId, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            "UPDATE public.todox_ai_provider_account SET health_status='healthy', consecutive_failures=0, last_success_at=now(), cooldown_until=NULL, updated_at=now() WHERE id=@providerAccountId;",
            new { providerAccountId });
    }

    public async Task MarkAccountFailureAsync(Guid providerAccountId, TimeSpan? cooldown, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            """
            UPDATE public.todox_ai_provider_account
               SET health_status = CASE WHEN @cooldownSeconds IS NULL THEN 'degraded' ELSE 'cooldown' END,
                   consecutive_failures = consecutive_failures + 1,
                   last_failure_at = now(),
                   cooldown_until = CASE WHEN @cooldownSeconds IS NULL THEN cooldown_until ELSE now() + (@cooldownSeconds || ' seconds')::interval END,
                   updated_at = now()
             WHERE id=@providerAccountId;
            """,
            new { providerAccountId, cooldownSeconds = cooldown is null ? (int?)null : Math.Max(1, (int)cooldown.Value.TotalSeconds) });
    }

    public async Task UpdateBalanceSnapshotAsync(Guid providerAccountId, decimal? balance, string source, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            """
            UPDATE public.todox_ai_provider_account
               SET last_known_balance=@balance,
                   last_balance_source=@source,
                   last_balance_synced_at=now(),
                   health_status = CASE
                       WHEN @balance IS NOT NULL AND minimum_balance_threshold IS NOT NULL AND @balance < minimum_balance_threshold THEN 'exhausted'
                       ELSE health_status
                   END,
                   updated_at=now()
             WHERE id=@providerAccountId;
            """,
            new { providerAccountId, balance, source });
    }

    private sealed class ClaimRow
    {
        public Guid LeaseId { get; init; }
        public Guid ProviderAccountId { get; init; }
        public long ProviderId { get; init; }
        public long ProviderCapabilityId { get; init; }
        public string ProviderCode { get; init; } = string.Empty;
        public string AccountCode { get; init; } = string.Empty;
        public string? CredentialConfigName { get; init; }
    }
}

public interface IAiProviderCredentialResolver
{
    Task<ResolvedAiProviderCredential> ResolveAsync(Guid providerAccountId, string role = "api_key", CancellationToken ct = default);
}

public sealed class AiProviderCredentialResolver : IAiProviderCredentialResolver
{
    private readonly IAiProviderAccountRepository _accounts;
    private readonly IConfiguration _configuration;

    public AiProviderCredentialResolver(IAiProviderAccountRepository accounts, IConfiguration configuration)
    {
        _accounts = accounts;
        _configuration = configuration;
    }

    public async Task<ResolvedAiProviderCredential> ResolveAsync(Guid providerAccountId, string role = "api_key", CancellationToken ct = default)
    {
        var credentials = await _accounts.ListCredentialsAsync(providerAccountId, ct);
        var credential = credentials.FirstOrDefault(x => x.Enabled && x.CredentialRole.Equals(role, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("AI_PROVIDER_CREDENTIAL_REFERENCE_MISSING");

        var reference = credential.CredentialKey ?? credential.CredentialConfigName;
        if (string.IsNullOrWhiteSpace(reference))
        {
            throw new InvalidOperationException("AI_PROVIDER_CREDENTIAL_REFERENCE_MISSING");
        }

        var value = _configuration[reference]
                    ?? _configuration[$"AiProviders:{reference}"]
                    ?? Environment.GetEnvironmentVariable(reference);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"AI_PROVIDER_CREDENTIAL_VALUE_MISSING:{reference}");
        }

        return new ResolvedAiProviderCredential(providerAccountId, credential.CredentialRole, reference, value);
    }
}

public static partial class AiSecretRedactor
{
    private static readonly string[] SecretKeys =
    {
        "authorization", "bearer", "api_key", "apiKey", "access_token", "refresh_token",
        "client_secret", "private_key", "token", "secret", "password"
    };

    public static string? Redact(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        try
        {
            using var doc = JsonDocument.Parse(value);
            return JsonSerializer.Serialize(RedactElement(doc.RootElement), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException)
        {
            return SecretPattern().Replace(value, "$1[redacted]");
        }
    }

    private static object? RedactElement(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(
                p => p.Name,
                p => IsSecretKey(p.Name) ? (object?)"[redacted]" : RedactElement(p.Value),
                StringComparer.OrdinalIgnoreCase),
            JsonValueKind.Array => element.EnumerateArray().Select(RedactElement).ToArray(),
            JsonValueKind.String => RedactBearer(element.GetString()),
            JsonValueKind.Number when element.TryGetInt64(out var l) => l,
            JsonValueKind.Number when element.TryGetDecimal(out var d) => d,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };

    private static string? RedactBearer(string? value)
        => string.IsNullOrWhiteSpace(value) ? value : BearerPattern().Replace(value, "Bearer [redacted]");

    private static bool IsSecretKey(string key)
        => SecretKeys.Any(secret => key.Equals(secret, StringComparison.OrdinalIgnoreCase));

    [GeneratedRegex(@"(?i)(authorization\s*[:=]\s*bearer\s+|api[_-]?key\s*[:=]\s*|access[_-]?token\s*[:=]\s*|refresh[_-]?token\s*[:=]\s*|client[_-]?secret\s*[:=]\s*|private[_-]?key\s*[:=]\s*)\S+")]
    private static partial Regex SecretPattern();

    [GeneratedRegex(@"(?i)Bearer\s+\S+")]
    private static partial Regex BearerPattern();
}
