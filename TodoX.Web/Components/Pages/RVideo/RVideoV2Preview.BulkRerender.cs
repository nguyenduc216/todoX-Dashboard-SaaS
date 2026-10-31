using Microsoft.AspNetCore.Components;
using MudBlazor;
using TodoX.Web.Components.Dialogs;
using TodoX.Web.Models;
using TodoX.Web.Services.VideoRender;

namespace TodoX.Web.Components.Pages.RVideo;

public partial class RVideoV2Preview
{
    [Inject] private IVideoRenderEligibilityService BulkEligibility { get; set; } = default!;

    private readonly Dictionary<long, RVideoSceneVideoRenderRequest> _bulkRerenderRequests = new();

    private async Task OpenBulkRerenderDialogAsync()
    {
        if (JobView is null || _busy) return;
        var parameters = new DialogParameters<RVideoBulkRerenderDialog>
        {
            { x => x.LoadQuote, BuildBulkRerenderQuoteAsync }
        };
        var dialog = await DialogService.ShowAsync<RVideoBulkRerenderDialog>("Render job", parameters,
            new DialogOptions { MaxWidth = MaxWidth.Small, FullWidth = true, CloseButton = true, BackdropClick = false });
        var result = await dialog.Result;
        if (result is { Canceled: false, Data: RVideoBulkRerenderQuote quote }) await RunBulkRerenderAsync(quote);
    }

    private async Task<RVideoBulkRerenderQuote> BuildBulkRerenderQuoteAsync(RVideoBulkRerenderMode mode)
    {
        _bulkRerenderRequests.Clear();
        var view = JobView!;
        var user = AuthState.CurrentUser ?? throw new InvalidOperationException("User session required.");
        var scenes = view.Project.Scenes.OrderBy(x => x.SceneIndex).ToArray();
        var eligibility = await BulkEligibility.GetVideoRenderEligibilityAsync(view.Project.Id, scenes.Select(x => x.Id).ToArray());
        var lines = new List<RVideoBulkRerenderQuoteLine>();
        var skipped = new List<RVideoBulkRerenderSkippedScene>();
        var available = 0m;
        var hasExternalVoice = false;
        foreach (var scene in scenes)
        {
            var state = await LoadBulkRerenderStateAsync(scene, user);
            var check = eligibility.Results.FirstOrDefault(x => x.SceneId == scene.Id)?.Status;
            var hasCompletedVideo = !string.IsNullOrWhiteSpace(scene.SceneVideoUrl)
                || string.Equals(state?.SelectedVersion?.Status, "completed", StringComparison.OrdinalIgnoreCase);
            var decision = RVideoBulkRerenderPolicy.Decide(mode, state, hasCompletedVideo, check != VideoRenderEligibilityStatus.MissingImage);
            if (decision == RVideoBulkRerenderSceneDecision.SkipCompleted) continue;
            if (decision != RVideoBulkRerenderSceneDecision.Render)
            {
                skipped.Add(new(scene.SceneIndex, BulkRerenderSkipReason(decision)));
                continue;
            }
            if (check == VideoRenderEligibilityStatus.InvalidPrompt)
            {
                skipped.Add(new(scene.SceneIndex, "nội dung chưa hợp lệ"));
                continue;
            }

            var forSupersede = RVideoBulkRerenderPolicy.UsesNewOperation(state!);
            if (forSupersede && !await SaveBulkRerenderSettingsAsync(scene, user))
            {
                skipped.Add(new(scene.SceneIndex, "cấu hình chưa hợp lệ"));
                continue;
            }

            // Same immutable snapshot and quote as the per-scene "Tạo lượt video mới" button.
            var snapshot = System.Text.Json.JsonSerializer.Deserialize<VideoProjectSceneDto>(System.Text.Json.JsonSerializer.Serialize(scene))!;
            var request = new RVideoSceneVideoRenderRequest(view.Project.Id, snapshot, WithRenderSettings(ScenePromptMetadata.FromScene(snapshot)), view.CoreJob.ServiceId)
            {
                ForSupersede = forSupersede
            };
            var estimate = await SceneVideoRender.EstimateAsync(request);
            if (!RVideoBulkRerenderPolicy.CanQuote(estimate, forSupersede))
            {
                skipped.Add(new(scene.SceneIndex, "chưa báo giá được"));
                continue;
            }

            _bulkRerenderRequests[scene.Id] = request with { ConfirmedSettings = estimate.OperationSettings };
            lines.Add(new(scene.Id, scene.SceneIndex, estimate.EstimatedPoints, RVideoBulkRerenderPolicy.HasUnknownOutcome(state!)));
            available = estimate.AvailablePoints;
            hasExternalVoice |= view.Settings is { } settings && RVideoRules.RequiresExternalVoice(scene, settings);
        }

        return new(mode, lines, skipped, available, hasExternalVoice);
    }

    private async Task<SceneVideoUiState?> LoadBulkRerenderStateAsync(VideoProjectSceneDto scene, CurrentUserSession user)
    {
        try
        {
            var state = await SceneVersions.GetSceneVideoUiStateAsync(scene.Id, user);
            _videoStates[scene.Id] = state;
            return state;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "RVIDEO_V2_BULK_RERENDER_STATE_UNAVAILABLE sceneId={SceneId}", scene.Id);
            return null;
        }
    }

    // A new operation quotes the saved scene, so the job's ratio and resolution must be saved first; otherwise it falls back to 9:16.
    private async Task<bool> SaveBulkRerenderSettingsAsync(VideoProjectSceneDto scene, CurrentUserSession user)
    {
        var settings = RVideoRules.ResolveRenderSettings(JobView!.Project.OriginalPrompt);
        var metadata = ScenePromptMetadata.FromScene(scene);
        if (metadata.Extra.GetValueOrDefault("aspect_ratio") == settings.AspectRatio
            && metadata.Extra.GetValueOrDefault("resolution") == settings.Resolution) return true;
        WithRenderSettings(metadata);
        var request = new VideoProjectSaveSceneDraftRequest { SceneId = scene.Id, Status = scene.Status, Title = scene.Title, SceneIndex = scene.SceneIndex, ScenePrompt = metadata.Serialize(), ImagePrompt = metadata.ImagePrompt, VideoPrompt = metadata.MotionPrompt, ImageUrl = scene.StaticImageUrl, ImagePath = scene.StaticImagePath, VideoUrl = scene.SceneVideoUrl, VideoPath = scene.SceneVideoPath, DurationSeconds = scene.DurationSeconds, ErrorMessage = scene.ErrorMessage };
        var result = await SceneDraftSave.SaveAsync(request, user.UserId, metadata);
        if (!result.Success)
        {
            Logger.LogWarning("RVIDEO_V2_BULK_RERENDER_SAVE_REFUSED sceneId={SceneId} errorCode={ErrorCode}", scene.Id, result.ErrorCode);
            return false;
        }
        scene.ScenePrompt = request.ScenePrompt;
        scene.ImagePrompt = request.ImagePrompt;
        scene.VideoPrompt = request.VideoPrompt;
        return true;
    }

    private async Task RunBulkRerenderAsync(RVideoBulkRerenderQuote quote)
    {
        if (AuthState.CurrentUser is not { } user || !await _gate.WaitAsync(0)) return;
        _busy = true;
        StateHasChanged();
        var queued = 0;
        var failed = new List<int>();
        try
        {
            foreach (var line in quote.Scenes)
            {
                try
                {
                    var result = await RVideoBulkRerenderPolicy.SubmitAsync(SceneVideoRender, _bulkRerenderRequests[line.SceneId], user);
                    if (!result.Success)
                    {
                        Logger.LogWarning("RVIDEO_V2_BULK_RERENDER_REFUSED sceneId={SceneId} errorCode={ErrorCode}", line.SceneId, result.ErrorCode);
                        failed.Add(line.SceneIndex);
                        continue;
                    }
                    queued++;
                }
                catch (Exception ex)
                {
                    // The outcome is uncertain: never resend here. The reload below shows any attempt that did start.
                    Logger.LogWarning(ex, "RVIDEO_V2_BULK_RERENDER_FAILED sceneId={SceneId}", line.SceneId);
                    failed.Add(line.SceneIndex);
                }
            }
        }
        finally
        {
            _bulkRerenderRequests.Clear();
            _busy = false;
            _gate.Release();
        }

        if (queued > 0) Snackbar.Add($"Đang tạo lại video cho {queued} scene.", Severity.Success);
        if (failed.Count > 0)
            Snackbar.Add($"Chưa tạo được lượt mới cho {string.Join(", ", failed.Select(x => $"scene {x:00}"))}. Hãy tải lại trạng thái rồi thử lại.", Severity.Warning);
        if (OnReloadRequested.HasDelegate) await OnReloadRequested.InvokeAsync();
    }

    private static string BulkRerenderSkipReason(RVideoBulkRerenderSceneDecision decision) => decision switch
    {
        RVideoBulkRerenderSceneDecision.SkipInFlight => "đang tạo video",
        RVideoBulkRerenderSceneDecision.SkipProviderPending => "provider đang xử lý, cần kiểm tra lại kết quả",
        RVideoBulkRerenderSceneDecision.SkipMissingImage => "thiếu ảnh",
        _ => "chưa tải được trạng thái"
    };
}
