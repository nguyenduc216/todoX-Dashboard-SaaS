using TodoX.Web.Services.ImageRender;
using TodoX.Web.Services.Media;

namespace TodoX.Web.Tests;

internal sealed class FakeMediaService : IMediaFileService
{
    public Dictionary<Guid, MediaFileDto> MediaById { get; } = new();

    public Task<MediaFileDto?> GetAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(MediaById.GetValueOrDefault(id));

    public Task<MediaFileDto> SaveAsync(byte[] content, string originalFileName, string mimeType, string fileCategory, Guid? userId, Guid? customerId, Guid tenantId, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<MediaFileDto> SaveAtObjectKeyAsync(byte[] content, string objectKey, string originalFileName, string mimeType, string fileCategory, Guid? userId, Guid? customerId, Guid tenantId, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<MediaFileDto?> GetByObjectKeyAsync(string objectKey, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<MediaFileDto?> GetByObjectKeyAsync(Guid tenantId, string objectKey, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<MediaFileDto?> GetByPublicUrlAsync(string publicUrl, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<byte[]?> ReadBytesAsync(Guid id, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<Stream?> OpenReadAsync(Guid id, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<MediaFileDto> ReplaceContentAsync(Guid mediaId, byte[] content, string mimeType, Guid userId, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<bool> IsOwnedByAsync(Guid mediaId, Guid userId, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<ReferenceImage?> BuildReferenceImageAsync(Guid mediaId, string role, Guid userId, bool enforceOwnership = true, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<MediaFileDto> DownloadAndSaveImageAsync(string imageUrl, string fileCategory, Guid? userId, Guid? customerId, Guid tenantId, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<MediaFileDto> DownloadAndSaveImageAtObjectKeyAsync(string imageUrl, string objectKey, string fileCategory, Guid? userId, Guid? customerId, Guid tenantId, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<MediaFileDto> SaveBinaryAtObjectKeyAsync(byte[] content, string objectKey, string originalFileName, string mimeType, string fileCategory, Guid? userId, Guid? customerId, Guid tenantId, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<MediaFileDto> DownloadAndSaveBinaryAtObjectKeyAsync(string fileUrl, string objectKey, string fileCategory, string expectedMimeType, Guid? userId, Guid? customerId, Guid tenantId, CancellationToken ct = default)
        => throw new NotImplementedException();
}
