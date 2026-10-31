using Microsoft.AspNetCore.Components;
using MudBlazor;
using TodoX.Web.Components.Dialogs;
using TodoX.Web.Models;
using TodoX.Web.Services.VideoRender;

namespace TodoX.Web.Components.Pages;

public partial class RenderVideoJobs
{
    [Inject] private IRVideoSceneVideoRenderAction BulkVideoRender { get; set; } = default!;

    private readonly Dictionary<long, RVideoSceneVideoRenderRequest> _bulkRerenderRequests = new();
    private bool _bulkRerendering;

    private async Task OpenBulkRerenderDialogAsync()
    {
        if (_project is null || _busy || _bulkRerendering) return;
        await LoadSceneVideoUiStatesAsync();
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
        var lines = new List<RVideoBulkRerenderQuoteLine>();
        var skipped = new List<RVideoBulkRerenderSkippedScene>();
        var available = 0m;
        var hasExternalVoice = false;
        // Saving a scene replaces the project DTOs, so walk ids and re-read each scene.
        foreach (var sceneId in _project!.Scenes.OrderBy(x => x.SceneIndex).Select(x => x.Id).ToArray())
        {
            if (_project.Scenes.SingleOrDefault(x => x.Id == sceneId) is not { } scene) continue;
            var state = SupersedeState(scene);
            var decision = _pendingVideoOperations.ContainsKey(sceneId)
                ? RVideoBulkRerenderSceneDecision.SkipInFlight
                : RVideoBulkRerenderPolicy.Decide(mode, state, HasCompletedVideoVersion(scene), HasBulkRerenderImage(scene));
            if (decision == RVideoBulkRerenderSceneDecision.SkipCompleted) continue;
            if (decision != RVideoBulkRerenderSceneDecision.Render)
            {
                skipped.Add(new(scene.SceneIndex, BulkRerenderSkipReason(decision)));
                continue;
            }

            var request = await PrepareSceneVideoRequestAsync(scene);
            if (request is null)
            {
                skipped.Add(new(scene.SceneIndex, "cấu hình chưa hợp lệ"));
                continue;
            }

            // Same immutable snapshot and quote as the per-scene "Tạo lượt video mới" button.
            var snapshot = System.Text.Json.JsonSerializer.Deserialize<VideoProjectSceneDto>(System.Text.Json.JsonSerializer.Serialize(request.Scene))!;
            request = request with { Scene = snapshot, Metadata = ScenePromptMetadata.FromScene(snapshot), ForSupersede = RVideoBulkRerenderPolicy.UsesNewOperation(state!) };
            var estimate = await BulkVideoRender.EstimateAsync(request);
            if (!RVideoBulkRerenderPolicy.CanQuote(estimate, request.ForSupersede))
            {
                skipped.Add(new(scene.SceneIndex, "chưa báo giá được"));
                continue;
            }

            _bulkRerenderRequests[sceneId] = request with { ConfirmedSettings = estimate.OperationSettings };
            lines.Add(new(sceneId, scene.SceneIndex, estimate.EstimatedPoints, RVideoBulkRerenderPolicy.HasUnknownOutcome(state!)));
            available = estimate.AvailablePoints;
            hasExternalVoice |= RequiresExternalVoice(scene);
        }

        return new(mode, lines, skipped, available, hasExternalVoice);
    }

    private async Task RunBulkRerenderAsync(RVideoBulkRerenderQuote quote)
    {
        if (AuthState.CurrentUser is not { } user || _bulkRerendering) return;
        _bulkRerendering = true;
        _busy = true;
        var queued = 0;
        var failed = new List<int>();
        try
        {
            foreach (var line in quote.Scenes)
            {
                try
                {
                    var request = _bulkRerenderRequests[line.SceneId];
                    var result = await RVideoBulkRerenderPolicy.SubmitAsync(BulkVideoRender, request, user);
                    if (!result.Success)
                    {
                        Logger.LogWarning("RVIDEO_BULK_RERENDER_REFUSED sceneId={SceneId} errorCode={ErrorCode}", line.SceneId, result.ErrorCode);
                        failed.Add(line.SceneIndex);
                        continue;
                    }
                    if (result.RenderJobId is Guid jobId && request.ForSupersede) ApplySupersedeQueued(line.SceneId, jobId);
                    queued++;
                }
                catch (Exception ex)
                {
                    // The outcome is uncertain: never resend here. The reload below shows any attempt that did start.
                    Logger.LogWarning(ex, "RVIDEO_BULK_RERENDER_FAILED sceneId={SceneId}", line.SceneId);
                    failed.Add(line.SceneIndex);
                }
            }
        }
        finally
        {
            _bulkRerendering = false;
            _busy = false;
            _bulkRerenderRequests.Clear();
        }

        if (queued > 0) Snackbar.Add($"Đang tạo lại video cho {queued} scene.", Severity.Success);
        if (failed.Count > 0)
            Snackbar.Add($"Chưa tạo được lượt mới cho {string.Join(", ", failed.Select(x => $"scene {x:00}"))}. Hãy tải lại trạng thái rồi thử lại.", Severity.Warning);
        await StartPolling();
        await ReloadAsync();
    }

    private bool HasBulkRerenderImage(VideoProjectSceneDto scene)
        => !string.IsNullOrWhiteSpace(ResolveDraftImageUrl(scene, GetSceneDraft(scene)))
           || (_useReferenceImageForAllScenes && HasSharedReferenceImage);

    private static string BulkRerenderSkipReason(RVideoBulkRerenderSceneDecision decision) => decision switch
    {
        RVideoBulkRerenderSceneDecision.SkipInFlight => "đang tạo video",
        RVideoBulkRerenderSceneDecision.SkipMissingImage => "thiếu ảnh",
        _ => "chưa tải được trạng thái"
    };
}
