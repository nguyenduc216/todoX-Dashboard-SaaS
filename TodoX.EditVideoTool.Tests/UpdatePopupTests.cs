using TodoX.EditVideoTool.App;
using TodoX.EditVideoTool.Setup;

namespace TodoX.EditVideoTool.Tests;

/// <summary>The open window checks for updates by itself and asks once per new version.</summary>
public sealed class UpdatePopupTests
{
    [Fact]
    public void Asks_once_per_new_version_and_never_when_up_to_date()
    {
        var newer = new UpdateInfo("1.2.3", "1.2.4", "0.4.0", "1.2.4: Sửa lỗi", Staged: false);
        Assert.True(UpdatePopup.ShouldAsk(newer, null));
        Assert.False(UpdatePopup.ShouldAsk(newer, "1.2.4"));
        Assert.True(UpdatePopup.ShouldAsk(newer with { Latest = "1.2.5" }, "1.2.4"));
        Assert.False(UpdatePopup.ShouldAsk(new UpdateInfo("1.2.3", "1.2.3", null, null, false), null));
        Assert.False(UpdatePopup.ShouldAsk(new UpdateInfo("1.2.3", null, null, null, false), null));
    }

    [Fact]
    public void Text_names_the_version_notes_and_what_yes_does()
    {
        var text = UpdatePopup.Text(new UpdateInfo("1.2.3", "1.2.4", "0.4.0", "1.2.4: Sửa lỗi đồng bộ", Staged: false));
        Assert.Contains("1.2.4", text);
        Assert.Contains("Có gì mới: Sửa lỗi đồng bộ", text);
        Assert.Contains("Cập nhật ngay?", text);
        Assert.Contains("Mở lại TodoX EditVideo ngay", UpdatePopup.Text(new UpdateInfo("1.2.3", "1.2.4", null, null, Staged: true)));
        Assert.Contains("đã cũ", UpdatePopup.Text(new UpdateInfo("0.3.0", "1.2.4", "0.4.0", null, false)));
    }

    [Fact]
    public void Window_rechecks_every_half_hour() => Assert.Equal(TimeSpan.FromMinutes(30), MainWindow.UpdateCheckEvery);
}
