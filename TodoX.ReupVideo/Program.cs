using System.Globalization;
using System.Windows.Forms;
using TodoX.ReupVideo;
using TodoX.ReupVideo.App;
using TodoX.ReupVideo.Core;

// After a self-update: wait for the old copy to close (it holds the single-instance lock), remove the old file.
AppUpdater.FinishUpdate(args);

// One copy per Windows user: a second start (e.g. double-click while it sits in the tray) just shows a message.
// The lock is per data folder, so a copy with its own TODOX_REUPVIDEO_HOME (testing) can run beside the real one.
var paths = AppPaths.Default();
var lockName = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
    System.Text.Encoding.UTF8.GetBytes(paths.Root.ToLowerInvariant())))[..16];
using var single = new Mutex(initiallyOwned: true, $@"Local\TodoX.ReupVideo.{Environment.UserName}.{lockName}", out var first);
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
// Keep "Khởi động cùng Windows" pointing at this exe (its file name changes with each update).
try
{
    if (TodoX.ReupVideo.Storage.AppSettings.Load(paths).StartWithWindows)
    {
        StartupRegistration.Set(true);
    }
}
catch (Exception)
{
    // registry not writable: the checkbox in Cài đặt shows the real state
}

var startMinimized = args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));
Application.Run(new MainForm(paths, startMinimized));
