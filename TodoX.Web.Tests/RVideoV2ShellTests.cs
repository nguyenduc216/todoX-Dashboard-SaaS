using System.Text.RegularExpressions;
using Xunit;

namespace TodoX.Web.Tests;

public class RVideoV2ShellTests
{
    private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    private static string WebRoot => Path.Combine(RepoRoot, "TodoX.Web");
    private static string ShellPath => Path.Combine(WebRoot, "Components", "Pages", "RVideo", "RVideoV2Shell.razor");
    private static string InfoPath => Path.Combine(WebRoot, "Components", "Pages", "RVideo", "RVideoV2Info.razor");
    private static string PreviewPath => Path.Combine(WebRoot, "Components", "Pages", "RVideo", "RVideoV2Preview.razor");
    private static string ResultPath => Path.Combine(WebRoot, "Components", "Pages", "RVideo", "RVideoV2Result.razor");
    private static string LegacyPath => Path.Combine(WebRoot, "Components", "Pages", "RenderVideoJobs.razor");

    [Fact] public void V2_Route_Exists(){ var t=File.ReadAllText(ShellPath); Assert.Contains("@page \"/r-video\"",t); Assert.Contains("@page \"/r-video/{JobId:guid}\"",t); }
    [Fact] public void Legacy_HasDemoLink(){ var t=File.ReadAllText(LegacyPath); Assert.Contains("Xem demo giao diện mới",t); Assert.Contains("/r-video",t); }
    [Fact] public void V2_Has3Tabs(){ var t=File.ReadAllText(ShellPath); Assert.Equal(3, Regex.Matches(t, "MudTabPanel").Count); Assert.Contains("THÔNG TIN",t); Assert.Contains("XEM TRƯỚC",t); Assert.Contains("KẾT QUẢ",t); }
    [Fact] public void V2_NoDuplicateTopJobHeading(){ var t=File.ReadAllText(ShellPath);
        // RVID-UI-V2-PHASE3A.1: the redundant top job-heading block (title + edit/menu + id/service/status) is removed.
        Assert.DoesNotContain("rv2-title-block",t);
        Assert.DoesNotContain("rv2-title-row",t);
        Assert.DoesNotContain("rv2-meta-row",t);
        Assert.DoesNotContain("rv2-header-top",t);
        Assert.DoesNotContain("Typo.h6",t);
        Assert.DoesNotContain("MoreVert",t);
        // Navigation + back affordance survive.
        Assert.Contains("rv2-nav-row",t);
        Assert.Contains("GoBackLegacy",t);
    }
    [Fact] public void V2_ShellCss_NoDeadHeadingClasses(){ var css=File.ReadAllText(Path.ChangeExtension(ShellPath,".razor.css"));
        Assert.DoesNotContain(".rv2-header-top",css);
        Assert.DoesNotContain(".rv2-title-block",css);
        Assert.DoesNotContain(".rv2-meta-row",css);
        Assert.Contains(".rv2-nav-row",css);
    }
    [Fact] public void Info_Advanced_CollapsedDefault(){ var t=File.ReadAllText(InfoPath); Assert.Contains("_adv=false",t); }
    [Fact] public void Preview_HasDesktopAndMobile(){ var t=File.ReadAllText(PreviewPath); Assert.Contains("rv2-preview-desktop",t); Assert.Contains("rv2-preview-mobile",t); Assert.Contains("rv2-scene-nav",t); }
    [Fact] public void Preview_StatusColors(){ var t=File.ReadAllText(PreviewPath); Assert.Contains("VideoSceneStatuses.VideoReady",t); }
    [Fact] public void Result_NoSceneList_HasSummary(){ var t=File.ReadAllText(ResultPath); Assert.Contains("Tổng điểm",t); Assert.Contains("Kích thước file",t); Assert.Contains("Định dạng",t); Assert.Contains("Thời gian hoàn thành",t); Assert.DoesNotContain("scene-list",t.ToLower()); }
    [Fact] public void Result_Score_NotHardcoded(){ var t=File.ReadAllText(ResultPath); Assert.Contains("\"—\"",t); Assert.DoesNotContain("92/100",t); }
    [Fact] public void Css_UsesRv2Namespace(){ foreach(var p in new[]{ShellPath,InfoPath,PreviewPath,ResultPath}){ var css=Path.ChangeExtension(p,".razor.css"); Assert.True(File.Exists(css)); Assert.Contains(".rv2-",File.ReadAllText(css)); } }
    [Fact] public void Legacy_StillAtRenderJob(){ var t=File.ReadAllText(LegacyPath); Assert.Contains("@page \"/render-job\"",t); }

    private static string InfoText=>File.ReadAllText(InfoPath);
    private static string PreviewText=>File.ReadAllText(PreviewPath);
    private static string MediaPreviewPath=>Path.Combine(WebRoot,"Components","Dialogs","RVideoV2MediaPreviewDialog.razor");

    [Fact] public void Info_HasCharacterReferenceBlock(){ var t=InfoText; Assert.Contains("Hình ảnh tham chiếu",t); Assert.Contains("UploadedCharacterUrl",t); Assert.Contains("SourceImageUrl",t); }
    [Fact] public void Info_Advanced_HasProcessingVoiceMusicModes(){ var t=InfoText; Assert.Contains("RVideoExecutionModes.Auto",t); Assert.Contains("RVideoVoiceModes.Library",t); Assert.Contains("ListMusicAsync",t); Assert.Contains("ListVoicesAsync",t); Assert.Contains("MusicVolume",t); Assert.Contains("DefaultTtsRate",t); }
    [Fact] public void Info_Advanced_DefaultCollapsed(){ var t=InfoText; Assert.DoesNotContain("_adv=true",t); }
    [Fact] public void Preview_HasSceneContentEditor(){ var t=PreviewText; Assert.Contains("Nội dung",t); Assert.Contains("HandleContentChangedAsync",t); Assert.Contains("ScenePromptMetadata.FromScene",t); }
    [Fact] public void Preview_VideoPlayOpensDialog(){ var t=PreviewText; Assert.Contains("OpenVideoAsync",t); Assert.Contains("RVideoV2MediaPreviewDialog",t); Assert.Contains("PlayArrow",t); }
    [Fact] public void Preview_SceneMenu_HasWorkingActions(){ var t=PreviewText; Assert.Contains("MudMenu",t); Assert.Contains("HandleMoveAsync",t); Assert.Contains("HandleDeleteAsync",t); Assert.Contains("HandleEditAsync",t); }
    [Fact] public void MediaPreviewDialog_Exists_WithVideoElement(){ var t=File.ReadAllText(MediaPreviewPath); Assert.Contains("<video",t); Assert.Contains("controls",t); Assert.Contains("VideoUrl",t); }
}
