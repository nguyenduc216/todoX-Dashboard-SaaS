namespace TodoX.Web.Services.AiProviders;

public sealed class AiProviderTaskSubmitRequest
{
    public string ProviderCode { get; set; } = string.Empty;
    public string CapabilityCode { get; set; } = string.Empty;
    public string? ModelName { get; set; }
    public Guid? RenderJobId { get; set; }
    public Guid? ProviderAccountId { get; set; }
    public string CredentialReference { get; set; } = string.Empty;
    public string SecretValue { get; set; } = string.Empty;
    public object Payload { get; set; } = new { };
}

public sealed class AiProviderTaskStatusRequest
{
    public string ProviderCode { get; set; } = string.Empty;
    public string ProviderTaskId { get; set; } = string.Empty;
    public Guid? ProviderAccountId { get; set; }
    public string CredentialReference { get; set; } = string.Empty;
    public string SecretValue { get; set; } = string.Empty;
}

public sealed class AiProviderTaskResult
{
    public bool Success { get; set; }
    public bool Terminal { get; set; }
    public string? ProviderTaskId { get; set; }
    public string? ProviderStatus { get; set; }
    public IReadOnlyList<string> OutputUrls { get; set; } = Array.Empty<string>();
    public decimal? UsageQuantity { get; set; }
    public string? UsageUnit { get; set; }
    public decimal? ProviderActualCost { get; set; }
    public string? ProviderCurrency { get; set; }
    public string? RawUsageJson { get; set; }
    public string? RawResponseJson { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
}

public interface IAiProviderTaskClient
{
    string ProviderCode { get; }
    Task<AiProviderTaskResult> SubmitAsync(AiProviderTaskSubmitRequest request, CancellationToken ct = default);
    Task<AiProviderTaskResult> GetStatusAsync(AiProviderTaskStatusRequest request, CancellationToken ct = default);
    Task<AiProviderTaskResult> CancelAsync(AiProviderTaskStatusRequest request, CancellationToken ct = default);
}

public sealed class UnsupportedAiProviderTaskClient : IAiProviderTaskClient
{
    public UnsupportedAiProviderTaskClient(string providerCode)
    {
        ProviderCode = providerCode;
    }

    public string ProviderCode { get; }

    public Task<AiProviderTaskResult> SubmitAsync(AiProviderTaskSubmitRequest request, CancellationToken ct = default)
        => Task.FromResult(Failed("AI_PROVIDER_TASK_CLIENT_NOT_IMPLEMENTED"));

    public Task<AiProviderTaskResult> GetStatusAsync(AiProviderTaskStatusRequest request, CancellationToken ct = default)
        => Task.FromResult(Failed("AI_PROVIDER_TASK_CLIENT_NOT_IMPLEMENTED"));

    public Task<AiProviderTaskResult> CancelAsync(AiProviderTaskStatusRequest request, CancellationToken ct = default)
        => Task.FromResult(Failed("AI_PROVIDER_CANCEL_NOT_SUPPORTED"));

    private static AiProviderTaskResult Failed(string code)
        => new()
        {
            Success = false,
            Terminal = true,
            ErrorCode = code,
            ErrorMessage = code
        };
}
