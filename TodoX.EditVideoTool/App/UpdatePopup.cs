using TodoX.EditVideoTool.Setup;

namespace TodoX.EditVideoTool.App;

/// <summary>The "new version" popup the open window shows (once per version) when its periodic check finds one.</summary>
public static class UpdatePopup
{
    public static bool ShouldAsk(UpdateInfo info, string? alreadyAsked) =>
        info.UpdateAvailable && info.Latest is not null && !string.Equals(info.Latest, alreadyAsked, StringComparison.Ordinal);

    public static string Text(UpdateInfo info)
    {
        var notes = Updater.CleanNotes(info.Latest, info.Notes);
        var head = info.BelowMinimum
            ? $"Bản {info.Current} đang dùng đã cũ. Cần cập nhật lên TodoX EditVideo {info.Latest} để tiếp tục dùng."
            : $"Đã có TodoX EditVideo {info.Latest} (bạn đang dùng {info.Current}).";
        var what = notes.Length > 0 ? $"\n\nCó gì mới: {notes}" : "";
        var action = info.Staged
            ? "\n\nBản mới đã tải về máy. Mở lại TodoX EditVideo ngay để dùng bản mới?"
            : "\n\nCập nhật ngay? TodoX sẽ tải bản mới rồi tự mở lại cửa sổ.";
        return head + what + action +
               "\n\nNếu Claude/Codex đang dùng TodoX, hãy tắt hẳn rồi mở lại Claude/Codex sau khi cập nhật.";
    }
}
