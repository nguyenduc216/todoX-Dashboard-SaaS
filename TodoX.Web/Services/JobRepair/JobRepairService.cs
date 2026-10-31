using System.Text;
using TodoX.Web.Models;
using TodoX.Web.Services.Render;
using TodoX.Web.Services.VideoRender;

namespace TodoX.Web.Services.JobRepair;

public sealed record JobRepairSceneDiagnosis(JobRepairSceneSnapshot Snapshot, JobRepairFinding Finding);

public sealed record JobRepairDiagnosis(string Kind, Guid JobId, IReadOnlyList<JobRepairSceneDiagnosis> Scenes, string Report);

public sealed record JobRepairResult(bool Success, string Message);

public interface IJobRepairService
{
    Task<JobRepairDiagnosis> DiagnoseAsync(CurrentUserSession user, string kind, Guid jobId, CancellationToken ct = default);
    Task<JobRepairResult> ApplyAsync(CurrentUserSession user, string kind, Guid jobId, long sceneId, string expectedCaseCode, CancellationToken ct = default);
}

public sealed class JobRepairService(
    JobRepairRepository repository,
    VideoRenderRepository projects,
    IRVideoSceneVideoRenderAction videoAction,
    IRenderJobService jobs,
    ILogger<JobRepairService> logger) : IJobRepairService
{
    private static readonly TimeSpan PollDelay = TimeSpan.FromSeconds(5);

    public async Task<JobRepairDiagnosis> DiagnoseAsync(CurrentUserSession user, string kind, Guid jobId, CancellationToken ct = default)
    {
        Demand(user);
        // Only RVideo has repair cases so far; other kinds still get a report instead of a guess.
        var scenes = kind == AdminJobMonitorKinds.RVideo
            ? (await repository.GetSceneSnapshotsAsync(jobId, ct)).Select(s => new JobRepairSceneDiagnosis(s, JobRepairPlaybook.Classify(s))).ToList()
            : [];
        return new(kind, jobId, scenes, BuildReport(kind, jobId, scenes));
    }

    public async Task<JobRepairResult> ApplyAsync(CurrentUserSession user, string kind, Guid jobId, long sceneId, string expectedCaseCode, CancellationToken ct = default)
    {
        Demand(user);
        // Re-read at click time: only the exact state the administrator confirmed may be repaired.
        var diagnosis = await DiagnoseAsync(user, kind, jobId, ct);
        var scene = diagnosis.Scenes.FirstOrDefault(s => s.Snapshot.SceneId == sceneId);
        if (scene is null || scene.Finding.CaseCode != expectedCaseCode)
            return new(false, "Trạng thái scene đã thay đổi. Hãy kiểm tra lại trước khi sửa.");
        try
        {
            var result = scene.Finding.Action switch
            {
                JobRepairAction.RecheckProvider => await RecheckProviderAsync(scene.Snapshot, ct),
                JobRepairAction.NewOperation => await CreateNewOperationAsync(user, scene.Snapshot, ct),
                _ => new JobRepairResult(false, "Trường hợp này không có thao tác sửa tự động.")
            };
            await RecordAsync(user, scene, result, ct);
            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ADMIN_JOB_REPAIR_FAILED jobId={JobId} sceneId={SceneId} case={CaseCode}", jobId, sceneId, expectedCaseCode);
            var failed = new JobRepairResult(false, "Không sửa được scene này. Hãy gửi báo cáo cho bộ phận kỹ thuật.");
            await RecordAsync(user, scene, failed, ct);
            return failed;
        }
    }

    private async Task<JobRepairResult> RecheckProviderAsync(JobRepairSceneSnapshot scene, CancellationToken ct)
    {
        if (!await repository.ResetKnownTaskRecoveryBudgetAsync(scene.ProjectId, scene.SceneId, scene.HeadJobId!.Value, ct))
            return new(false, "Trạng thái job đã thay đổi nên chưa kiểm tra lại được.");
        var scheduled = await jobs.SchedulePersistentProviderPollAsync(scene.HeadJobId.Value, PollDelay,
            "ADMIN_JOB_REPAIR_RECHECK", "Administrator requested another check of the existing provider task.", ct);
        return scheduled
            ? new(true, "Đã xếp lịch kiểm tra lại provider. Hãy bấm Kiểm tra lại sau ít phút để xem kết quả.")
            : new(false, "Job chưa đủ điều kiện để kiểm tra lại provider. Hãy gửi báo cáo cho bộ phận kỹ thuật.");
    }

    private async Task<JobRepairResult> CreateNewOperationAsync(CurrentUserSession user, JobRepairSceneSnapshot snapshot, CancellationToken ct)
    {
        // The new job is billed to the actor's customer. Without one it cannot touch a customer's wallet.
        if (user.CustomerId is not null)
            return new(false, "Tài khoản này gắn với khách hàng nên lượt mới sẽ bị trừ điểm. Hãy dùng tài khoản quản trị.");
        var project = await projects.GetProjectAsync(snapshot.ProjectId, ct);
        var scene = project?.Scenes.SingleOrDefault(s => s.Id == snapshot.SceneId);
        if (scene is null) return new(false, "Không tìm thấy scene.");
        var request = new RVideoSceneVideoRenderRequest(snapshot.ProjectId, scene, ScenePromptMetadata.FromScene(scene), snapshot.ServiceId) { ForSupersede = true };
        var estimate = await videoAction.EstimateAsync(request, ct);
        if (!estimate.CanStart || estimate.OperationSettings is null)
            return new(false, estimate.ErrorMessage ?? "Chưa thể tạo lượt mới. Hãy kiểm tra ảnh, nội dung và bảng giá của scene.");
        request = request with { ConfirmedSettings = estimate.OperationSettings };
        var acknowledgement = await videoAction.GetSupersedeAcknowledgementAsync(snapshot.ProjectId, snapshot.SceneId, user, Guid.NewGuid(), ct);
        var result = await videoAction.SupersedeAsync(request, acknowledgement with { RiskAcknowledged = true }, user, ct);
        return result.Success
            ? new(true, $"Đã tạo lượt video mới. Job: {result.RenderJobId}")
            : new(false, result.ErrorMessage ?? "Chưa thể tạo lượt mới.");
    }

    private async Task RecordAsync(CurrentUserSession user, JobRepairSceneDiagnosis scene, JobRepairResult result, CancellationToken ct)
    {
        try
        {
            await jobs.AddEventAsync(scene.Snapshot.HeadJobId!.Value, "ADMIN_JOB_REPAIR", result.Message,
                new { actorId = user.UserId, sceneId = scene.Snapshot.SceneId, caseCode = scene.Finding.CaseCode, action = scene.Finding.Action.ToString(), result.Success },
                result.Success ? "info" : "warning", ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ADMIN_JOB_REPAIR_AUDIT_FAILED sceneId={SceneId} case={CaseCode} success={Success}", scene.Snapshot.SceneId, scene.Finding.CaseCode, result.Success);
        }
    }

    private static void Demand(CurrentUserSession user)
    {
        if (!AdminEndpointAuthorization.IsAdmin(user))
            throw new UnauthorizedAccessException("Chỉ quản trị viên mới được dùng Sửa lỗi.");
    }

    // Holds states and error codes only: no customer data, prompts or provider identifiers.
    private static string BuildReport(string kind, Guid jobId, IReadOnlyList<JobRepairSceneDiagnosis> scenes)
    {
        var report = new StringBuilder($"Job {jobId} ({kind}), {scenes.Count} scene\n");
        foreach (var (s, finding) in scenes)
        {
            report.Append($"scene {s.SceneIndex} id={s.SceneId} case={finding.CaseCode} sceneStatus={s.SceneStatus} generation={s.Generation} ")
                .Append($"headJob={s.HeadJobId} jobStatus={s.HeadJobStatus} error={s.HeadErrorCode} versionStatus={s.HeadVersionStatus} ")
                .Append($"providerTask={s.HeadHasProviderTask} providerVideoId={s.HeadHasProviderVideoId} submit={s.SubmitStates} ")
                .Append($"lockActive={s.HeadLockActive} idleMinutes={s.HeadIdleMinutes} prepared={s.PreparedCount} orphans={s.OrphanJobCount} competing={s.CompetingJobCount}\n");
        }
        return report.ToString();
    }
}
