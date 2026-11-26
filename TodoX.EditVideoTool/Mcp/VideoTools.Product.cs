using TodoX.EditVideoTool.Server;

namespace TodoX.EditVideoTool.Mcp;

// The app's own "Tạo video từ sản phẩm" screen uses the same paid steps as the assistant (session log, project
// history, price window), but sends every AI job first and waits for them together.
public sealed partial class VideoTools
{
    /// <summary>Sends one AI image/video job and returns at once (a task to check with <see cref="PollAiAsync"/>).</summary>
    internal Task<string> SubmitAiAsync(AiCreateInput input, string project, CancellationToken ct) =>
        AiCreateAsync(input, project, TimeSpan.Zero, new Progress<ModelContextProtocol.ProgressNotificationValue>(), ct);

    /// <summary>A task's state; a finished one is saved into the project's 06-ai folder first.</summary>
    internal Task<string> PollAiAsync(string task, string project, CancellationToken ct) => AiStatusAsync(task, project, ct);
}
