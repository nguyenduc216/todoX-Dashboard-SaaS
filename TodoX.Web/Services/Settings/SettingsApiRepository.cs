using Dapper;
using TodoX.Web.Data;

namespace TodoX.Web.Services.Settings;

public sealed class ApiEndpointDto
{
    public Guid Id { get; set; }
    public string EndpointCode { get; set; } = string.Empty;
    public string EndpointName { get; set; } = string.Empty;
    public Guid? ProviderId { get; set; }
    public Guid? DefaultModelId { get; set; }
    public string HttpMethod { get; set; } = "POST";
    public string Path { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool Enabled { get; set; }
}

public sealed class ApiProviderDto
{
    public Guid Id { get; set; }
    public string ProviderCode { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;
    public string? ProviderGroup { get; set; }
    public string? ProviderType { get; set; }
    public string? BaseUrl { get; set; }
    public string? AuthType { get; set; }
    public string? CredentialKey { get; set; }
    public string? Sdk { get; set; }
    public bool Enabled { get; set; }
}

public sealed class ApiProviderModelDto
{
    public Guid Id { get; set; }
    public Guid ProviderId { get; set; }
    public string ModelCode { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public string? ModelType { get; set; }
    public bool Enabled { get; set; }
}

public sealed class ApiEndpointLogDto
{
    public DateTime CreatedAt { get; set; }
    public string EndpointCode { get; set; } = string.Empty;
    public string? ProviderCode { get; set; }
    public string? ModelCode { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public int? LatencyMs { get; set; }
}

/// <summary>Read access to settings.api_* and write access to settings.api_endpoint_logs.</summary>
public sealed class SettingsApiRepository
{
    private readonly TodoXConnectionFactory _factory;

    public SettingsApiRepository(TodoXConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<ApiEndpointDto?> GetEndpointAsync(string endpointCode)
    {
        using var conn = await _factory.OpenAsync();
        return await conn.QuerySingleOrDefaultAsync<ApiEndpointDto>(
            """
            SELECT id AS Id, endpoint_code AS EndpointCode, endpoint_name AS EndpointName,
                   provider_id AS ProviderId, default_model_id AS DefaultModelId,
                   http_method AS HttpMethod, path AS Path, description AS Description, enabled AS Enabled
              FROM settings.api_endpoints WHERE endpoint_code=@code;
            """, new { code = endpointCode });
    }

    public async Task<ApiProviderDto?> GetProviderAsync(Guid id)
    {
        using var conn = await _factory.OpenAsync();
        return await conn.QuerySingleOrDefaultAsync<ApiProviderDto>(
            """
            SELECT id AS Id, provider_code AS ProviderCode, provider_name AS ProviderName,
                   provider_group AS ProviderGroup, provider_type AS ProviderType, base_url AS BaseUrl,
                   auth_type AS AuthType, credential_key AS CredentialKey, sdk AS Sdk, enabled AS Enabled
              FROM settings.api_providers WHERE id=@id;
            """, new { id });
    }

    public async Task<ApiProviderModelDto?> GetModelAsync(Guid id)
    {
        using var conn = await _factory.OpenAsync();
        return await conn.QuerySingleOrDefaultAsync<ApiProviderModelDto>(
            """
            SELECT id AS Id, provider_id AS ProviderId, model_code AS ModelCode, model_name AS ModelName,
                   model_type AS ModelType, enabled AS Enabled
              FROM settings.api_provider_models WHERE id=@id;
            """, new { id });
    }

    public async Task<IReadOnlyList<ApiProviderDto>> GetProvidersAsync()
    {
        using var conn = await _factory.OpenAsync();
        var rows = await conn.QueryAsync<ApiProviderDto>(
            """
            SELECT id AS Id, provider_code AS ProviderCode, provider_name AS ProviderName,
                   provider_group AS ProviderGroup, provider_type AS ProviderType, base_url AS BaseUrl,
                   auth_type AS AuthType, credential_key AS CredentialKey, sdk AS Sdk, enabled AS Enabled
              FROM settings.api_providers ORDER BY provider_name;
            """);
        return rows.ToList();
    }

    public async Task<IReadOnlyList<ApiProviderModelDto>> GetModelsAsync()
    {
        using var conn = await _factory.OpenAsync();
        var rows = await conn.QueryAsync<ApiProviderModelDto>(
            """
            SELECT id AS Id, provider_id AS ProviderId, model_code AS ModelCode, model_name AS ModelName,
                   model_type AS ModelType, enabled AS Enabled
              FROM settings.api_provider_models ORDER BY model_name;
            """);
        return rows.ToList();
    }

    public async Task<IReadOnlyList<ApiEndpointDto>> GetEndpointsAsync()
    {
        using var conn = await _factory.OpenAsync();
        var rows = await conn.QueryAsync<ApiEndpointDto>(
            """
            SELECT id AS Id, endpoint_code AS EndpointCode, endpoint_name AS EndpointName,
                   provider_id AS ProviderId, default_model_id AS DefaultModelId,
                   http_method AS HttpMethod, path AS Path, description AS Description, enabled AS Enabled
              FROM settings.api_endpoints ORDER BY endpoint_name;
            """);
        return rows.ToList();
    }

    public async Task<IReadOnlyList<ApiEndpointLogDto>> GetRecentLogsAsync(int limit = 100)
    {
        using var conn = await _factory.OpenAsync();
        var rows = await conn.QueryAsync<ApiEndpointLogDto>(
            """
            SELECT created_at AS CreatedAt, endpoint_code AS EndpointCode, provider_code AS ProviderCode,
                   model_code AS ModelCode, status AS Status, error_message AS ErrorMessage, latency_ms AS LatencyMs
              FROM settings.api_endpoint_logs ORDER BY created_at DESC LIMIT @limit;
            """, new { limit });
        return rows.ToList();
    }

    public async Task LogCallAsync(Guid? tenantId, Guid? userId, string endpointCode, string? providerCode,
        string? modelCode, Guid requestId, string status, string? errorMessage, int latencyMs)
    {
        using var conn = await _factory.OpenAsync();
        await conn.ExecuteAsync(
            """
            INSERT INTO settings.api_endpoint_logs
                (id, tenant_id, user_id, endpoint_code, provider_code, model_code, request_id, status, error_message, latency_ms, created_at)
            VALUES
                (gen_random_uuid(), @tenant, @user, @endpoint, @provider, @model, @req, @status, @err, @latency, now());
            """,
            new { tenant = tenantId, user = userId, endpoint = endpointCode, provider = providerCode,
                  model = modelCode, req = requestId, status, err = errorMessage, latency = latencyMs });
    }
}
