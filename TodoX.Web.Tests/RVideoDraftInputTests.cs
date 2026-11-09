using System.Text.Json;
using TodoX.Web.Services.VideoRender;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class RVideoDraftInputTests
{
    [Fact]
    public void ReadsTheSavedDescriptionDraftFromTheJobInput()
    {
        using var doc = JsonDocument.Parse("""{"engine":"RVIDEO","draftInput":"Video về quê ra đồng","draftSavedAt":"2026-11-09T07:30:00.0000000+00:00"}""");
        var (text, savedAt) = RVideoDraftInput.Read(doc.RootElement);
        Assert.Equal("Video về quê ra đồng", text);
        Assert.Equal(new DateTimeOffset(2026, 11, 9, 7, 30, 0, TimeSpan.Zero), savedAt);
    }

    [Fact]
    public void MissingDraftReadsAsNothing()
    {
        using var doc = JsonDocument.Parse("""{"engine":"RVIDEO"}""");
        Assert.Equal((null, null), RVideoDraftInput.Read(doc.RootElement));
    }

    [Fact]
    public void JobUpdateKeepsTheDraftAlreadyInTheDatabase()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "TodoX.Web");
        var source = File.ReadAllText(Path.Combine(root, "Services", "VideoRender", "RVideoJobService.cs"));
        Assert.Contains("'draftInput', input_json->'draftInput', 'draftSavedAt', input_json->'draftSavedAt'", source);
    }
}
