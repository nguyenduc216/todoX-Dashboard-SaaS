namespace TodoX.Web.Services.JobRepair;

public enum JobRepairAction
{
    None,
    RecheckProvider,
    NewOperation
}

public sealed class JobRepairSceneSnapshot
{
    public long ProjectId { get; set; }
    public long SceneId { get; set; }
    public int SceneIndex { get; set; }
    public string? SceneStatus { get; set; }
    public Guid? ServiceId { get; set; }
    public long? Generation { get; set; }
    public Guid? HeadJobId { get; set; }
    public string? HeadJobStatus { get; set; }
    public string? HeadErrorCode { get; set; }
    public bool HeadHasProviderTask { get; set; }
    public bool HeadHasProviderVideoId { get; set; }
    public bool HeadLockActive { get; set; }
    public int HeadIdleMinutes { get; set; }
    public string? HeadVersionStatus { get; set; }
    public string? SubmitStates { get; set; }
    public bool AckFound { get; set; }
    public int PreparedCount { get; set; }
    public int OrphanJobCount { get; set; }
    public int CompetingJobCount { get; set; }
    public int CompetingVersionCount { get; set; }
}

/// <summary>A recognised scene state, what the button may do about it and what that does to points.</summary>
public sealed record JobRepairFinding(
    string CaseCode,
    string Summary,
    JobRepairAction Action,
    string? ActionLabel,
    string? PointNote,
    string? RiskNote);

/// <summary>
/// The repair button's memory: every stuck-job case fixed by hand is added here with its signature, repair and test.
/// A state matching no case is reported, never repaired.
/// </summary>
public static class JobRepairPlaybook
{
    public const string Healthy = "HEALTHY";
    public const string Running = "RUNNING";
    public const string NotStarted = "NOT_STARTED";
    public const string ManualReview = "MANUAL_REVIEW";
    public const string ManualReviewUnconfirmedSubmit = "MANUAL_REVIEW_UNCONFIRMED_SUBMIT";
    public const string BlockedByOtherJob = "BLOCKED_BY_OTHER_JOB";
    public const string KnownTaskTimedOut = "KNOWN_TASK_POLL_TIMEOUT";
    public const string AmbiguousSubmit = "AMBIGUOUS_SUBMIT";
    public const string FailedOperation = "FAILED_OPERATION";
    public const string Unknown = "UNKNOWN";

    // A send that has not finished after this long is treated as stuck rather than still running.
    public const int StaleMinutes = 60;

    private const string NewOperationPointNote =
        "Không trừ điểm của khách: lượt này do quản trị viên tạo nên video thành công chỉ được ghi nhận sử dụng. Chi phí provider của lượt mới do TodoX chịu.";

    public static JobRepairFinding Classify(JobRepairSceneSnapshot s)
    {
        if (s.HeadJobId is null)
            return Info(NotStarted, "Scene chưa có lượt tạo video nào.");
        if (s.HeadJobStatus == "completed")
            return Info(Healthy, "Video của scene đã hoàn tất.");
        if (s.CompetingJobCount + s.CompetingVersionCount > 0)
            return Info(BlockedByOtherJob, $"Còn {s.CompetingJobCount + s.CompetingVersionCount} lượt khác chưa kết thúc của scene này. Chưa thể tạo lượt mới an toàn.");
        if (!s.AckFound || s.PreparedCount > 0)
            return Info(Unknown, "Trạng thái này chưa có trong quy trình sửa lỗi.");

        if (s.HeadErrorCode == "SCENE_VIDEO_MANUAL_REVIEW_REQUIRED")
            return s.HeadHasProviderTask || s.HeadHasProviderVideoId || !string.IsNullOrEmpty(s.SubmitStates)
                ? Info(ManualReview, "Lượt gửi đang chờ kiểm tra thủ công. Nút Sửa lỗi không can thiệp.")
                : new(ManualReviewUnconfirmedSubmit,
                    "Lượt gửi cũ chờ kiểm tra thủ công: không có mã tác vụ và không có bản ghi gửi nào. Có thể tạo lượt mới.",
                    JobRepairAction.NewOperation, "Tạo lượt video mới", NewOperationPointNote,
                    "Hệ thống từng đánh dấu không rõ kết quả gửi. Rất khó xảy ra, nhưng nếu provider đã nhận lượt cũ, TodoX có thể mất phí provider hai lần.");

        if (s.HeadJobStatus == "pending_reconciliation" && s.HeadHasProviderTask && s.HeadHasProviderVideoId
            && s.HeadVersionStatus == "pending_reconciliation" && s.SubmitStates == "CONFIRMED_PROVIDER_TASK")
            return new(KnownTaskTimedOut,
                "Provider đã nhận tác vụ nhưng hệ thống hết thời gian chờ. Video có thể đã xong phía provider.",
                JobRepairAction.RecheckProvider, "Kiểm tra lại provider",
                "Việc kiểm tra không tốn điểm. Nếu video đã xong, khách bị trừ đúng 1 lần phí của lượt này (chưa từng thu), không phát sinh thêm.",
                "Không gửi tác vụ mới lên provider.");

        if (s.HeadJobStatus == "failed")
            return new(FailedOperation,
                s.OrphanJobCount > 0
                    ? "Lượt video đã thất bại và có job mồ côi (đã không còn chặn). Có thể tạo lượt mới."
                    : "Lượt video đã thất bại. Có thể tạo lượt mới.",
                JobRepairAction.NewOperation, "Tạo lượt video mới", NewOperationPointNote, null);

        if (s.HeadJobStatus is "queued" or "preparing" or "rendering" or "processing" or "post_processing")
        {
            if (s.HeadLockActive || s.HeadIdleMinutes < StaleMinutes)
                return Info(Running, "Lượt video đang chạy.");
            if (!s.HeadHasProviderTask && !s.HeadHasProviderVideoId && s.SubmitStates == "SUBMIT_IN_FLIGHT")
                return new(AmbiguousSubmit,
                    $"Lượt gửi bị treo {s.HeadIdleMinutes / 60} giờ, không có mã tác vụ. Không rõ provider đã nhận hay chưa.",
                    JobRepairAction.NewOperation, "Vẫn tạo lượt video mới", NewOperationPointNote,
                    "Nếu provider đã nhận lượt cũ, TodoX có thể mất phí provider hai lần.");
        }

        return Info(Unknown, "Trạng thái này chưa có trong quy trình sửa lỗi.");
    }

    private static JobRepairFinding Info(string code, string summary) => new(code, summary, JobRepairAction.None, null, null, null);
}
