using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Services.AiProviders;

namespace TodoX.Web.Services.VideoRender;

public interface IRVideoTrustedPayerContextService
{
    Task<AiBillingTrustedPayerContext> BuildRVideoTrustedPayerContextAsync(long projectId, long sceneId, CancellationToken ct = default);
    Task<AiBillingTrustedPayerContext> ValidateAndBuildRVideoTrustedPayerContextAsync(long projectId, long sceneId, Guid? inputCustomerId, Guid? inputUserId, AiBillingTrustedPayerContext? inputContext, CancellationToken ct = default);
}

public sealed class RVideoTrustedPayerContextService : IRVideoTrustedPayerContextService
{
    private const string MismatchCode = "rvideo_video_payer_context_mismatch";

    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;

    public RVideoTrustedPayerContextService(TodoXConnectionFactory factory, TenantContext tenant)
    {
        _factory = factory;
        _tenant = tenant;
    }

    public async Task<AiBillingTrustedPayerContext> BuildRVideoTrustedPayerContextAsync(long projectId, long sceneId, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        var ownership = await conn.QuerySingleOrDefaultAsync<OwnershipRow>(
            """
            SELECT p.id AS ProjectId,
                   p.customer_id AS ProjectCustomerId,
                   p.user_id AS ProjectUserId,
                   p.core_job_id AS CoreJobId,
                   j.customer_id AS CoreJobCustomerId,
                   j.user_id AS CoreJobUserId,
                   j.operation_type AS OperationType
              FROM video_render.video_projects p
              JOIN video_render.video_project_scenes s
                ON s.id=@sceneId AND s.project_id=p.id AND s.tenant_id=p.tenant_id
              JOIN render.render_jobs j
                ON j.id=p.core_job_id AND j.tenant_id=p.tenant_id
             WHERE p.id=@projectId AND p.tenant_id=@tenant;
            """,
            new { projectId, sceneId, tenant = _tenant.TenantId });

        if (ownership is null
            || ownership.CoreJobId == Guid.Empty
            || !string.Equals(ownership.OperationType, "RVIDEO", StringComparison.OrdinalIgnoreCase)
            || ownership.ProjectCustomerId is null
            || ownership.CoreJobCustomerId is null
            || ownership.ProjectCustomerId != ownership.CoreJobCustomerId
            || (ownership.ProjectUserId is not null && ownership.CoreJobUserId is not null && ownership.ProjectUserId != ownership.CoreJobUserId))
        {
            throw new InvalidOperationException($"{MismatchCode}: persisted RVIDEO project/core-job ownership is invalid.");
        }

        return new AiBillingTrustedPayerContext(
            AiBillingPayerTypes.Customer,
            ownership.ProjectCustomerId,
            ownership.ProjectUserId ?? ownership.CoreJobUserId,
            SystemWalletCode: null,
            Source: "background_job");
    }

    public async Task<AiBillingTrustedPayerContext> ValidateAndBuildRVideoTrustedPayerContextAsync(
        long projectId,
        long sceneId,
        Guid? inputCustomerId,
        Guid? inputUserId,
        AiBillingTrustedPayerContext? inputContext,
        CancellationToken ct = default)
    {
        if (inputContext is null
            || !string.Equals(inputContext.PayerType, AiBillingPayerTypes.Customer, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{MismatchCode}: trusted customer payer context is required.");
        }

        var canonical = await BuildRVideoTrustedPayerContextAsync(projectId, sceneId, ct);
        if (inputCustomerId is null
            || inputCustomerId != canonical.PayerCustomerId
            || inputContext.PayerCustomerId != canonical.PayerCustomerId
            || (inputUserId is not null && inputUserId != canonical.UserId)
            || (inputContext.UserId is not null && inputContext.UserId != canonical.UserId))
        {
            throw new InvalidOperationException($"{MismatchCode}: job payer context does not match persisted RVIDEO ownership.");
        }

        return canonical;
    }

    private sealed class OwnershipRow
    {
        public long ProjectId { get; init; }
        public Guid? ProjectCustomerId { get; init; }
        public Guid? ProjectUserId { get; init; }
        public Guid CoreJobId { get; init; }
        public Guid? CoreJobCustomerId { get; init; }
        public Guid? CoreJobUserId { get; init; }
        public string? OperationType { get; init; }
    }
}
