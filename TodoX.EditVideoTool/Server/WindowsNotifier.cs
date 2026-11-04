using System.Drawing;
using System.Windows.Forms;

namespace TodoX.EditVideoTool.Server;

public interface INotifier
{
    void Show(string title, string text, bool isError = false);
}

/// <summary>
/// Windows notification (toast/balloon from a tray icon). Runs its own STA message loop so it works from the
/// console/MCP process, which has none.
/// </summary>
public sealed class WindowsNotifier : INotifier
{
    private static readonly TimeSpan Visible = TimeSpan.FromSeconds(8);

    public void Show(string title, string text, bool isError = false)
    {
        if (Environment.GetEnvironmentVariable("TODOX_EDITVIDEO_NO_TOAST") == "1")
        {
            return;
        }

        var thread = new Thread(() =>
        {
            try
            {
                using var icon = new NotifyIcon
                {
                    Icon = AppIcon(),
                    Visible = true,
                    Text = "TodoX EditVideo"
                };
                icon.ShowBalloonTip((int)Visible.TotalMilliseconds, title, text, isError ? ToolTipIcon.Error : ToolTipIcon.Info);
                using var timer = new System.Windows.Forms.Timer { Interval = (int)Visible.TotalMilliseconds };
                timer.Tick += (_, _) => Application.ExitThread();
                timer.Start();
                Application.Run();
                icon.Visible = false;
            }
            catch (Exception)
            {
                // A notification must never break the tool (e.g. no interactive desktop).
            }
        })
        {
            IsBackground = true
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    internal static Icon AppIcon()
    {
        try
        {
            return Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application;
        }
        catch (Exception)
        {
            return SystemIcons.Application;
        }
    }
}
