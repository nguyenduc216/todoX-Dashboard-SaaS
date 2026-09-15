using TodoX.Web.Services.AiProviders;

namespace TodoX.Web.Services.VideoRender;

public sealed record VideoProviderRoute(
    long ProviderId,
    long ProviderCapabilityId,
    string ProviderCode,
    string CapabilityCode,
    string? ModelName,
    string? ProviderConfigJson,
    string? CapabilityConfigJson,
    decimal UnitCostPoints);

public interface IVideoProviderRoutingService
{
    Task<VideoProviderRoute> ResolveAsync(
        string capabilityCode,
        long? providerCapabilityId = null,
        bool fromUser = false,
        CancellationToken ct = default);
}

public sealed class VideoProviderRoutingService : IVideoProviderRoutingService
{
    private readonly IAiProviderService _providers;
    private readonly AiProviderRepository _providerRepository;

    public VideoProviderRoutingService(IAiProviderService providers, AiProviderRepository providerRepository)
    {
        _providers = providers;
        _providerRepository = providerRepository;
    }

    public async Task<VideoProviderRoute> ResolveAsync(
        string capabilityCode,
        long? providerCapabilityId = null,
        bool fromUser = false,
        CancellationToken ct = default)
    {
        var option = await _providers.ResolveProviderForCapabilityAsync(capabilityCode, providerCapabilityId, fromUser, ct);
        var provider = await _providerRepository.GetProviderAsync(option.ProviderId, ct)
            ?? throw new InvalidOperationException("Configured provider could not be loaded.");
        var capability = provider.Capabilities.FirstOrDefault(x => x.Id == option.ProviderCapabilityId)
            ?? throw new InvalidOperationException("Configured provider capability could not be loaded.");

        return new VideoProviderRoute(
            option.ProviderId,
            option.ProviderCapabilityId,
            option.ProviderCode,
            option.CapabilityCode,
            option.ModelName,
            provider.ConfigJson,
            capability.ConfigJson,
            option.UnitCostPoints);
    }
}
