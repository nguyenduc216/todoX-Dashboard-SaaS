using System.Text.Json.Nodes;
using Dapper;
using Microsoft.Extensions.Options;
using TodoX.Web.Data;

namespace TodoX.Web.Services.EditVideoTool;

/// <summary>
/// One project on 79AI (Gommo) per customer project, named "&lt;customer code&gt; - &lt;project name&gt;", so every AI image
/// and video of a project is grouped there. 79AI does not cap points per project: TodoX keeps the customer's points
/// itself. If 79AI cannot create the project, work goes to the shared project (EditVideoTool:Ai:ProjectId) instead.
/// </summary>
public sealed class EditVideoAiProjectService
{
    private readonly TodoXConnectionFactory _factory;
    private readonly IEditVideoGommoClient _gommo;
    private readonly IOptionsMonitor<EditVideoAiOptions> _options;
    private readonly ILogger<EditVideoAiProjectService> _logger;

    public EditVideoAiProjectService(TodoXConnectionFactory factory, IEditVideoGommoClient gommo,
        IOptionsMonitor<EditVideoAiOptions> options, ILogger<EditVideoAiProjectService> logger)
    {
        _factory = factory;
        _gommo = gommo;
        _options = options;
        _logger = logger;
    }

    private string Shared => _options.CurrentValue.ProjectId;

    /// <summary>The 79AI project id for this customer project (created on first use), or the shared one.</summary>
    public async Task<string> ResolveAsync(EditVideoLicenseRow license, string? projectName, CancellationToken ct = default)
    {
        var name = (projectName ?? string.Empty).Trim();
        var key = EditVideoStorageService.ProjectKey(name);
        if (key.Length == 0)
        {
            return Shared;
        }

        using var conn = await _factory.OpenAsync(ct);
        var existing = await conn.ExecuteScalarAsync<string?>(
            "SELECT provider_project_id FROM editvideo.ai_projects WHERE tenant_id = @tenantId AND customer_id = @customerId AND project_key = @key;",
            new { tenantId = license.TenantId, customerId = license.CustomerId, key });
        if (existing is not null)
        {
            return existing;
        }

        var code = await conn.ExecuteScalarAsync<string?>("SELECT customer_code FROM crm.customers WHERE id = @id;", new { id = license.CustomerId });
        var providerName = ProviderName(code, name);
        string? providerId;
        try
        {
            var created = await _gommo.PostJsonAsync("/api/v2/ai-projects", new Dictionary<string, object?>
            {
                ["action"] = "create",
                ["name"] = providerName,
                ["description"] = $"TodoX EditVideo · {license.CustomerName}",
                ["language"] = "vi"
            }, ct);
            providerId = ReadId(created);
            if (providerId is null)
            {
                _logger.LogWarning("EDITVIDEO_AI_PROJECT_NO_ID customer={CustomerId} project={Project} response={Response}",
                    license.CustomerId, key, Shorten(created.ToJsonString()));
                return Shared;
            }

            // No cap on 79AI: TodoX checks and charges the customer's own points.
            try
            {
                await _gommo.PostJsonAsync("/api/v2/ai-projects", new Dictionary<string, object?> { ["action"] = "set_budget", ["id_base"] = providerId, ["budget"] = 0 }, ct);
            }
            catch (Exception ex) when (ex is EditVideoGommoException or HttpRequestException or TaskCanceledException or NotSupportedException)
            {
                _logger.LogInformation(ex, "EDITVIDEO_AI_PROJECT_BUDGET_SKIPPED project={ProviderId}", providerId);
            }
        }
        catch (Exception ex) when (ex is EditVideoGommoException or HttpRequestException or TaskCanceledException or NotSupportedException)
        {
            _logger.LogWarning(ex, "EDITVIDEO_AI_PROJECT_CREATE_FAILED customer={CustomerId} project={Project}", license.CustomerId, key);
            return Shared;
        }

        await conn.ExecuteAsync(
            """
            INSERT INTO editvideo.ai_projects (tenant_id, customer_id, project_key, project_name, provider_project_id, provider_project_name)
            VALUES (@tenantId, @customerId, @key, @name, @providerId, @providerName)
            ON CONFLICT (tenant_id, customer_id, project_key) DO NOTHING;
            """,
            new { tenantId = license.TenantId, customerId = license.CustomerId, key, name, providerId, providerName });
        return await conn.ExecuteScalarAsync<string?>(
            "SELECT provider_project_id FROM editvideo.ai_projects WHERE tenant_id = @tenantId AND customer_id = @customerId AND project_key = @key;",
            new { tenantId = license.TenantId, customerId = license.CustomerId, key }) ?? providerId;
    }

    /// <summary>"KH0012 - Nhà phố Quận 7"; customers without a code get "KH".</summary>
    internal static string ProviderName(string? customerCode, string projectName)
    {
        var code = string.IsNullOrWhiteSpace(customerCode) ? "KH" : customerCode.Trim();
        var name = $"{code} - {projectName.Trim()}";
        return name.Length > 120 ? name[..120] : name;
    }

    /// <summary>The new project's id_base wherever Gommo puts it (data.id_base, data.project.id_base, id_base, id).</summary>
    internal static string? ReadId(JsonNode? node)
    {
        foreach (var candidate in new[] { node?["data"]?["id_base"], node?["data"]?["project"]?["id_base"], node?["project"]?["id_base"], node?["id_base"], node?["data"]?["id"] })
        {
            if (candidate is JsonValue value && value.ToString() is { Length: > 0 } text)
            {
                return text;
            }
        }

        return null;
    }

    private static string Shorten(string text) => text.Length > 400 ? text[..400] : text;
}
