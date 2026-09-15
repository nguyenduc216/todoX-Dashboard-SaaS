using Xunit;

namespace TodoX.Web.Tests;

public sealed class RDanceCharacterReferenceSeparationRegressionTests
{
    [Fact]
    public void QueueingRenderDoesNotOverwriteOriginalCharacterImage()
    {
        var repository = ReadRepoFile("Services", "DanceSell", "DanceSellRepository.cs");
        var queueMethod = repository[repository.IndexOf("public async Task QueueForRenderAsync", StringComparison.Ordinal)..];

        Assert.DoesNotContain("character_image_url=@preparedReferenceUrl", queueMethod);
        Assert.Contains("motion_video_url=@motionVideoUrl", queueMethod);
        Assert.Contains("motion_provider_code=@motionProviderCode", queueMethod);
        Assert.Contains("motion_provider_model=@motionProviderModel", queueMethod);
    }

    [Fact]
    public void MotionQueueStillPassesPreparedReferenceToTheProvider()
    {
        var service = ReadRepoFile("Services", "DanceSell", "DanceSellPhase2Services.cs");

        Assert.Contains("References = new { referenceUrl = job.PreparedReferenceUrl!, motionVideoUrl = job.MotionVideoUrl", service);
        Assert.Contains("await _repo.QueueForRenderAsync(job.Id, renderJob.Id, logicalRequestId, job.PreparedReferenceUrl!, job.MotionVideoUrl", service);
        Assert.Contains("RequestJson = DanceSellRepository.ToJson(new { job.Id, job.PreparedReferenceUrl, job.MotionVideoUrl", service);
    }

    [Fact]
    public void CharacterUploadStillWritesTheOriginalCharacterUrl()
    {
        var repository = ReadRepoFile("Services", "DanceSell", "DanceSellRepository.cs");

        Assert.Contains("UpdateCharacterAsync(Guid id, Guid mediaId, string objectKey, string publicUrl", repository);
        Assert.Contains("character_image_url", repository);
        Assert.Contains("=> await UpdateMediaAsync(id, \"character_media_id\", mediaId, \"character_object_key\", objectKey, \"character_image_url\", publicUrl, ct);", repository);
    }

    private static string ReadRepoFile(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { AppContext.BaseDirectory, "..", "..", "..", ".." }.Concat(parts).ToArray()));
}
