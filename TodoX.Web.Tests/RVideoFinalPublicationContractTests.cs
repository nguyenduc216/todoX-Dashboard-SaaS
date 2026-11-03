using TodoX.Web.Services.VideoRender;
using Xunit;

namespace TodoX.Web.Tests;

// Persistence contract checks complement executable request/media tests. These do not claim
// to exercise PostgreSQL transactions; the operator staging checklist covers that boundary.
public sealed class RVideoFinalPublicationContractTests
{
    private static string Read(string path)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "TodoX.Web", "Services"))) directory = directory.Parent;
        return File.ReadAllText(Path.Combine(directory!.FullName, "TodoX.Web", path));
    }

    [Fact]
    public void HandlerCannotSelectFinalBeforePostMusicValidation()
    {
        var source = Read("Services/VideoRender/VideoRenderMergeHandler.cs");
        var validate = source.IndexOf("var validationError = await FinalMergeMedia.ValidateAsync", StringComparison.Ordinal);
        var reject = source.IndexOf("if (validationError is not null)", StringComparison.Ordinal);
        var publish = source.IndexOf("await _versions.CompleteFinalVideoVersionAsync", StringComparison.Ordinal);
        Assert.True(validate > source.IndexOf("File.Copy(mixedPath", StringComparison.Ordinal));
        Assert.True(reject > validate && publish > reject);
    }

    [Fact]
    public void CompletingFinalSelectsVersionAndProjectPointerInSameTransaction()
    {
        var source = Read("Services/VideoRender/SceneMediaVersioningService.cs");
        var start = source.IndexOf("public async Task CompleteFinalVideoVersionAsync", StringComparison.Ordinal);
        var end = source.IndexOf("public async Task FailFinalVideoVersionAsync", start, StringComparison.Ordinal);
        var body = source[start..end];
        Assert.Contains("BeginTransaction", body);
        Assert.Contains("is_selected=false", body);
        Assert.Contains("is_selected=true", body);
        Assert.Contains("selected_final_video_version_id=@versionId", body);
        Assert.Contains("tx.Commit()", body);
    }

    [Fact]
    public void MergeFailuresNeverOverwriteOrDeleteCurrentFinalMedia()
    {
        var source = Read("Services/VideoRender/VideoRenderMergeHandler.cs");
        var start = source.IndexOf("catch (Exception ex)", StringComparison.Ordinal);
        var end = source.IndexOf("private static void ValidateMergeInputs", start, StringComparison.Ordinal);
        var failure = source[start..end];
        Assert.Contains("FailFinalVideoVersionAsync", failure);
        Assert.DoesNotContain("CompleteFinalVideoVersionAsync", failure);
        Assert.DoesNotContain("File.Delete", source);
        Assert.DoesNotContain("finalVideoUrl:", failure);
        var repository = Read("Services/VideoRender/VideoRenderRepository.cs");
        Assert.Contains("final_video_url=COALESCE(@url, final_video_url)", repository);
        Assert.Contains("final_video_path=COALESCE(@path, final_video_path)", repository);
    }

    [Fact]
    public void PostPublicationEventFailureCannotMarkSelectedFinalFailed()
    {
        var source = Read("Services/VideoRender/VideoRenderMergeHandler.cs");
        var start = source.IndexOf("catch (Exception ex)", StringComparison.Ordinal);
        var guard = source.IndexOf("if (finalPublished)", start, StringComparison.Ordinal);
        var failure = source.IndexOf("FailFinalVideoVersionAsync", start, StringComparison.Ordinal);
        Assert.True(guard > start && guard < failure);
        Assert.Contains("return;", source[guard..failure]);
    }

    [Fact]
    public void PersistentDuplicateGuardLocksBeforeLookingForActiveJobs()
    {
        var source = Read("Services/Render/RenderJobService.cs");
        var start = source.IndexOf("public async Task<(RenderJobDto Job, bool AlreadyActive)> EnqueueForLogCodeIfNoneActiveAsync", StringComparison.Ordinal);
        var end = source.IndexOf("public async Task<(RenderJobDto Job, bool AlreadyActive)> EnqueueForSceneIfNoneActiveAsync", start, StringComparison.Ordinal);
        var body = source[start..end];
        Assert.True(body.IndexOf("pg_advisory_xact_lock", StringComparison.Ordinal) < body.IndexOf("var active =", StringComparison.Ordinal));
        Assert.Contains("pending_reconciliation", body);
        Assert.Contains("return (active, true)", body);
    }
}
