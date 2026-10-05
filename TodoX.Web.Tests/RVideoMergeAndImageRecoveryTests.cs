using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TodoX.Web.Models;
using TodoX.Web.Services.AiProviders;
using TodoX.Web.Services.Media;
using TodoX.Web.Services.Render;
using TodoX.Web.Services.ImageRender;
using TodoX.Web.Services.VideoRender;
using Xunit;

namespace TodoX.Web.Tests;

// ============================================================
// Bug A — RVideo scene image existing-provider-task recovery
// ============================================================
public sealed class SceneImageExistingProviderTaskRecoveryTests
{
    private static SceneImageRenderService CreateService(ThrowingMediaService media)
        => new(null!, new StubProviderService(), new ThrowingImageRouter(), media, CreateTenant(), NullLogger<SceneImageRenderService>.Instance);

    private static TodoX.Web.Services.TenantContext CreateTenant()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["TodoX:TenantId"] = Guid.NewGuid().ToString() })
            .Build();
        return new TodoX.Web.Services.TenantContext(null!, config);
    }

    private static SceneImageRenderContext CreateContext(string? providerTaskId, Guid referenceMediaId)
        => new()
        {
            ProjectId = 1,
            SceneId = 2,
            SceneIndex = 0,
            Prompt = "scene",
            UserId = Guid.NewGuid(),
            CapabilityCode = SceneImageRenderContext.RVideoCapabilityCode,
            CharacterReferenceMediaId = referenceMediaId,
            ProviderTaskId = providerTaskId
        };

    [Fact]
    public async Task ExistingProviderTaskBypassesReferenceRevalidationBeforeRouterCall()
    {
        // Any reference resolution attempt will throw (ThrowingMediaService); the reference block
        // must be skipped for an existing provider task so the call reaches the router instead.
        var media = new ThrowingMediaService();
        var service = CreateService(media);

        var outcome = await service.RerenderSceneImageWithOpenRouterAsync(CreateContext("existing-79ai-task", Guid.NewGuid()));

        Assert.False(outcome.Success);
        Assert.Equal(0, media.AccessCount);
        Assert.Contains("TEST_ROUTER", outcome.Error);
    }

    [Fact]
    public async Task InitialSubmitStillRequiresReferenceWhenConfigured()
    {
        var media = new ThrowingMediaService();
        var service = CreateService(media);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RerenderSceneImageWithOpenRouterAsync(CreateContext(null, Guid.NewGuid())));

        Assert.Equal("RVIDEO_REFERENCE_IMAGE_UNAVAILABLE", ex.Message);
        Assert.True(media.AccessCount > 0);
    }

    private sealed class ThrowingMediaService : IMediaFileService
    {
        public int AccessCount { get; private set; }

        public Task<MediaFileDto?> GetAsync(Guid id, CancellationToken ct = default)
        {
            AccessCount++;
            return Task.FromResult<MediaFileDto?>(null);
        }

        public Task<byte[]?> ReadBytesAsync(Guid id, CancellationToken ct = default)
        {
            AccessCount++;
            return Task.FromResult<byte[]?>(null);
        }

        public Task<MediaFileDto> SaveAsync(byte[] content, string originalFileName, string mimeType, string fileCategory, Guid? userId, Guid? customerId, Guid tenantId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<MediaFileDto> SaveAtObjectKeyAsync(byte[] content, string objectKey, string originalFileName, string mimeType, string fileCategory, Guid? userId, Guid? customerId, Guid tenantId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<MediaFileDto?> GetByObjectKeyAsync(string objectKey, CancellationToken ct = default) { AccessCount++; return Task.FromResult<MediaFileDto?>(null); }
        public Task<MediaFileDto?> GetByObjectKeyAsync(Guid tenantId, string objectKey, CancellationToken ct = default) { AccessCount++; return Task.FromResult<MediaFileDto?>(null); }
        public Task<MediaFileDto?> GetByPublicUrlAsync(string publicUrl, CancellationToken ct = default) { AccessCount++; return Task.FromResult<MediaFileDto?>(null); }
        public Task<Stream?> OpenReadAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<MediaFileDto> ReplaceContentAsync(Guid mediaId, byte[] content, string mimeType, Guid userId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> IsOwnedByAsync(Guid mediaId, Guid userId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ReferenceImage?> BuildReferenceImageAsync(Guid mediaId, string role, Guid userId, bool enforceOwnership = true, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<MediaFileDto> DownloadAndSaveImageAsync(string imageUrl, string fileCategory, Guid? userId, Guid? customerId, Guid tenantId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<MediaFileDto> DownloadAndSaveImageAtObjectKeyAsync(string imageUrl, string objectKey, string fileCategory, Guid? userId, Guid? customerId, Guid tenantId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<MediaFileDto> SaveBinaryAtObjectKeyAsync(byte[] content, string objectKey, string originalFileName, string mimeType, string fileCategory, Guid? userId, Guid? customerId, Guid tenantId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<MediaFileDto> DownloadAndSaveBinaryAtObjectKeyAsync(string fileUrl, string objectKey, string fileCategory, string expectedMimeType, Guid? userId, Guid? customerId, Guid tenantId, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class ThrowingImageRouter : IAiImageRenderRouter
    {
        public Task<AiImageRenderResult> RenderImageAsync(AiImageRenderRequest request, CancellationToken ct = default)
            => Task.FromResult(new AiImageRenderResult
            {
                Success = false,
                ErrorMessage = "TEST_ROUTER_REACHED"
            });
    }

    private sealed class StubProviderService : IAiProviderService
    {
        public Task<IReadOnlyList<AiProviderListItemDto>> GetProvidersAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<AiProviderDetailDto?> GetProviderAsync(long id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<AiProviderDetailDto?> GetProviderByCodeAsync(string providerCode, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<AiProviderDetailDto> UpdateProviderAsync(long id, UpdateAiProviderRequest request, CurrentUserSession user, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<AiProviderCapabilityDto>> GetCapabilitiesAsync(long? providerId, string? capabilityCode, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<AiProviderCapabilityDto> UpdateCapabilityAsync(long id, UpdateAiProviderCapabilityRequest request, CurrentUserSession user, CancellationToken ct = default) => throw new NotImplementedException();
        public Task SetDefaultCapabilityAsync(long capabilityId, CurrentUserSession user, CancellationToken ct = default) => throw new NotImplementedException();
        public Task SetDefaultCapabilitiesAsync(IReadOnlyList<long> capabilityIds, CurrentUserSession user, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<ProviderOptionDto>> GetSelectableProvidersAsync(string capabilityCode, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ProviderOptionDto?> GetDefaultProviderAsync(string capabilityCode, CancellationToken ct = default) => throw new NotImplementedException();

        public Task<ProviderOptionDto> ResolveProviderForCapabilityAsync(string capabilityCode, long? providerCapabilityId, bool fromUser, CancellationToken ct = default)
            => Task.FromResult(new ProviderOptionDto
            {
                ProviderCapabilityId = 1,
                ProviderId = 1,
                ProviderCode = "79ai",
                ProviderName = "79AI",
                CapabilityCode = capabilityCode,
                DisplayName = "79AI scene image",
                ModelName = "seedream",
                Enabled = true
            });

        public Task LogUsageAsync(AiProviderUsageLog log, CancellationToken ct = default) => throw new NotImplementedException();
    }
}
