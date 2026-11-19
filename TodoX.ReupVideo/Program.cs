using System.Globalization;
using System.Windows.Forms;
using TodoX.ReupVideo;
using TodoX.ReupVideo.App;
using TodoX.ReupVideo.Core;

// One copy per Windows user: a second start (e.g. double-click while it sits in the tray) just shows a message.
using var single = new Mutex(initiallyOwned: true, $@"Local\TodoX.ReupVideo.{Environment.UserName}", out var first);
if (!first)
{
    MessageBox.Show("todoX ReupVideo đang chạy (xem biểu tượng todoX ở khay hệ thống, góc phải thanh Taskbar).",
        "todoX ReupVideo", MessageBoxButtons.OK, MessageBoxIcon.Information);
    return;
}

// Vietnamese formats everywhere (dd/MM/yyyy).
var vi = CultureInfo.GetCultureInfo("vi-VN");
CultureInfo.DefaultThreadCurrentCulture = vi;
CultureInfo.CurrentCulture = vi;

var paths = AppPaths.Default();
var crashLog = new AppLog(paths);

// An unexpected error is written to the log file and shown in Vietnamese; the app keeps running.
Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
Application.ThreadException += (_, e) =>
{
    crashLog.Report("LỖI KHÔNG MONG MUỐN: " + e.Exception);
    MessageBox.Show("Có lỗi không mong muốn: " + e.Exception.Message + "\n\nChi tiết đã được ghi vào nhật ký (Cài đặt → Hệ thống → Mở thư mục nhật ký).",
        "todoX ReupVideo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
};
AppDomain.CurrentDomain.UnhandledException += (_, e) => crashLog.Report("LỖI NGHIÊM TRỌNG: " + e.ExceptionObject);

ApplicationConfiguration.Initialize();
var startMinimized = args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));
Application.Run(new MainForm(paths, startMinimized));
