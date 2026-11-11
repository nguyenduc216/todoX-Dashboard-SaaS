using Dapper;
using Microsoft.Extensions.Options;
using TodoX.Web.Data;
using TodoX.Web.Models;

namespace TodoX.Web.Services.PromptAssistant;

/// <summary>
/// Runs a service's prompt agent the way a real job does (same agent input), grades the script with
/// <see cref="PromptAgentAudit"/> and keeps the latest report on the service
/// (catalog.services.default_options.agent_audit) so admins see it before the service goes live.
/// </summary>
public sealed class PromptAgentAuditService
{
    private readonly IServicePromptAssistantService _assistant;
    private readonly ServicePromptAssistantRepository _repository;
    private readonly IOptions<ServicePromptAssistantOptions> _options;
    private readonly TodoXConnectionFactory _factory;
    private readonly ILogger<PromptAgentAuditService> _logger;

    public PromptAgentAuditService(IServicePromptAssistantService assistant, ServicePromptAssistantRepository repository,
        IOptions<ServicePromptAssistantOptions> options, TodoXConnectionFactory factory, ILogger<PromptAgentAuditService> logger)
    {
        _assistant = assistant;
        _repository = repository;
        _options = options;
        _factory = factory;
        _logger = logger;
    }

    public async Task<PromptAgentAuditReport> RunAsync(Guid serviceId, string? sampleInput, CurrentUserSession? user, CancellationToken ct = default)
    {
        if (!AdminEndpointAuthorization.IsAdmin(user)) throw new UnauthorizedAccessException("Chỉ quản trị viên TodoX được kiểm tra agent.");
        var assistant = await _repository.GetOrCreateAssistantAsync(serviceId, _options.Value, ct);
        var sample = string.IsNullOrWhiteSpace(sampleInput) ? PromptAgentAudit.DefaultSampleInput : sampleInput.Trim();
        // Same wording a customer job sends, so the audit sees what production will see.
        var agentInput = PromptAssistantEndpoints.BuildAgentInput(new PromptAssistantGenerateRequest(serviceId, sample));

        PromptAgentAuditReport report;
        Guid? generationId = null;
        try
        {
            var result = await _assistant.GeneratePromptAsync(serviceId, agentInput, user, ct);
            generationId = result.GenerationId;
            report = result.GeneratedJson is not null
                ? PromptAgentAudit.Grade(result.GeneratedJson)
                : new PromptAgentAuditReport
                {
                    Status = PromptAgentAuditStatuses.Error,
                    Error = result.ErrorCode == "assistant_disabled"
                        ? "Prompt Assistant đang tắt: bật Enabled và lưu cấu hình trước khi kiểm tra."
                        : $"{result.ErrorCode}: {result.ErrorMessage}"
                };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PROMPT_AGENT_AUDIT_FAILED serviceId={ServiceId}", serviceId);
            report = new PromptAgentAuditReport { Status = PromptAgentAuditStatuses.Error, Error = ex.Message };
        }

        report = report with
        {
            CheckedAtUtc = DateTime.UtcNow,
            CheckedBy = string.IsNullOrWhiteSpace(user?.Email) ? user?.DisplayName : user.Email,
            ModelCode = assistant.ModelCode,
            AgentId = assistant.GommoAgentId,
            AgentIdBase = assistant.GommoAgentIdBase,
            GenerationId = generationId
        };
        await SaveAsync(serviceId, report, ct);
        _logger.LogInformation("PROMPT_AGENT_AUDIT serviceId={ServiceId} status={Status} scenes={Scenes}", serviceId, report.Status, report.SceneCount);
        return report;
    }

    public async Task<PromptAgentAuditReport?> GetAsync(Guid serviceId, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var json = await conn.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT (default_options->'agent_audit')::text FROM catalog.services WHERE id=@serviceId;", new { serviceId }, cancellationToken: ct));
        return PromptAgentAuditReport.FromJson(json);
    }

    private async Task SaveAsync(Guid serviceId, PromptAgentAuditReport report, CancellationToken ct)
    {
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE catalog.services
               SET default_options = jsonb_set(COALESCE(default_options, '{}'::jsonb), '{agent_audit}', CAST(@report AS jsonb), true)
             WHERE id=@serviceId;
            """, new { serviceId, report = report.ToJson() }, cancellationToken: ct));
    }
}
