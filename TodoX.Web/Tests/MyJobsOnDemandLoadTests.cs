using System.Text;
using Xunit;

namespace TodoX.Web.Tests;

// My Jobs list: loads once after the interactive render, a page at a time, without blocking on per-job point lookups.
public sealed class MyJobsOnDemandLoadTests
{
    private static string Page() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
        "Components", "Pages", "MyJobs.razor"), Encoding.UTF8);

    [Fact]
    public void LoadsAfterFirstInteractiveRenderNotDuringPrerender()
    {
        var page = Page();
        Assert.Contains("if (firstRender) await ReloadForCurrentUserAsync();", page);
        Assert.DoesNotContain("OnInitializedAsync", page);
    }

    [Fact]
    public void EachSourceFetchesOnlyTheCurrentWindowPlusOne()
    {
        var page = Page();
        Assert.Contains("DanceJobs.ListAsync(currentUser, _take + 1)", page);
        Assert.Contains("ListCoreJobsAsync(currentUser, _take + 1)", page);
        Assert.DoesNotContain("DanceJobs.ListAsync(currentUser, 100)", page);
        Assert.DoesNotContain("new(1, 100)", page);
        Assert.Contains("_take += PageSize;", page);
        Assert.Contains("Tải thêm", page);
        // Rows older than a truncated source's last kept row are withheld so "Tải thêm" never inserts rows mid-list.
        Assert.Contains(".Where(x => frontier is null || x.CreatedAt >= frontier)", page);
    }

    [Fact]
    public void DancePointsResolveAfterTheListRendersAndProfileEditsDoNotReload()
    {
        var page = Page();
        var load = page[page.IndexOf("private async Task LoadJobsAsync()", StringComparison.Ordinal)..
                        page.IndexOf("private async Task<IReadOnlyList<TodoX.Web.Services.Platform.CoreJobView>> ListCoreJobsAsync", StringComparison.Ordinal)];
        Assert.DoesNotContain("ResolveDancePointsLabelAsync", load);
        Assert.True(load.IndexOf("_loading = false;", StringComparison.Ordinal) < load.IndexOf("await FillDancePointsAsync(version);", StringComparison.Ordinal));
        Assert.Contains("if (_loaded && key == _userKey) return;", page);
        Assert.Contains("_dancePoints.GetValueOrDefault(job.Id, \"…\")", page);
    }
}
