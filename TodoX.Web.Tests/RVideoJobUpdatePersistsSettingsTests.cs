using Xunit;

namespace TodoX.Web.Tests;

// V2 saves reference image / mode / voice / music through RVideoJobService.UpdateAsync. The render
// pipeline reads video_render.rvideo_job_settings, so UpdateAsync must write that row as well.
public sealed class RVideoJobUpdatePersistsSettingsTests
{
    [Fact]
    public void UpdateAsyncWritesTheSettingsRowThePipelineReads()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "TodoX.Web");
        var source = File.ReadAllText(Path.Combine(root, "Services", "VideoRender", "RVideoJobService.cs"));
        var body = source[source.IndexOf("public async Task UpdateAsync(", StringComparison.Ordinal)..];
        body = body[..body.IndexOf("public async Task SyncLifecycleAsync(", StringComparison.Ordinal)];
        Assert.Contains("await _settings.SaveAsync(view.Project.Id, request.Settings, ct);", body);
        Assert.Contains("RVideoRules.PreserveValidUploadedCharacterSnapshot(request.Settings, persisted);", body);
    }
}
