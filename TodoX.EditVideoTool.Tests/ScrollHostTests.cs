using System.Drawing;
using System.Windows.Forms;
using TodoX.EditVideoTool.App;

namespace TodoX.EditVideoTool.Tests;

public sealed class ScrollHostTests
{
    [Fact]
    public void Scrolls_only_within_the_page_and_hides_the_bar_when_everything_fits()
    {
        using var content = new Panel { Size = new Size(600, 1000) };
        using var host = new ScrollHost(content, Color.Black) { Size = new Size(620, 300) };
        host.PerformLayout();

        Assert.Equal(700, host.Overflow);
        host.ScrollTo(10_000);
        Assert.Equal(-700, content.Top);
        host.ScrollTo(-50);
        Assert.Equal(0, content.Top);

        content.Height = 200; // page now fits: no scrolling, no bar
        Assert.Equal(0, host.Overflow);
        Assert.Equal(0, content.Top);
        Assert.DoesNotContain(host.Controls.Cast<Control>(), c => c != content && c.Visible);
    }
}
