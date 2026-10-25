using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor;
using TodoX.Web.Components.Pages.RVideo;
using TodoX.Web.Components.Pages;
using TodoX.Web.Models;
using TodoX.Web.Models.Catalog;
using TodoX.Web.Services;
using TodoX.Web.Services.Render;
using TodoX.Web.Services.VideoRender;
using Xunit;

namespace TodoX.Web.Tests;

// Executes the real component action, not a browser/provider end-to-end test.
public sealed class SceneVideoSupersedeUiTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitConsentCreatesOneOperationEvenForConcurrentClicks(bool unknown)
    {
        var (component,action,dialog)=Create(unknown);
        dialog.Answer=new TaskCompletionSource<bool?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first=component.RunAsync();
        await component.RunAsync(); // Busy button cannot issue a second command.
        Assert.Equal(0,action.Submits);
        dialog.Answer.SetResult(true);
        await first;
        Assert.Equal(1,action.Submits);
        Assert.True(action.Acknowledgement!.RiskAcknowledged);
        Assert.Equal(42,action.Acknowledgement.SceneId);
        Assert.Contains(unknown ? "chưa xác định kết quả" : "được giữ nguyên",dialog.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public async Task CancelDoesNotCreateOrChangeAnyOperation(bool? answer)
    {
        var (component,action,dialog)=Create(true);
        dialog.Answer=new TaskCompletionSource<bool?>(); dialog.Answer.SetResult(answer);
        await component.RunAsync();
        Assert.Equal(0,action.Submits);
    }

    [Fact]
    public async Task DisabledAndMissingReadinessNeverSubmit()
    {
        var (component,action,_)=Create(true);
        component.Disabled=true; await component.RunAsync(); Assert.Equal(0,action.Acknowledgements);
        component.Disabled=false; action.CanStart=false; await component.RunAsync(); Assert.Equal(0,action.Submits);
    }

    [Fact]
    public async Task FailedDraftSavePreventsAcknowledgementAndSubmission()
    {
        var (component, action, _) = Create(true);
        component.Prepare = () => Task.FromResult(false);
        await component.RunAsync();
        Assert.Equal(0, action.Acknowledgements);
        Assert.Equal(0, action.Submits);
    }

    [Fact]
    public async Task SuccessfulSupersedeReportsNewJobAndReloadsWithoutManualRefresh()
    {
        var (component, action, dialog) = Create(true);
        dialog.Answer = new TaskCompletionSource<bool?>(); dialog.Answer.SetResult(true);
        var reloads = 0;
        Guid? queuedJobId = null;
        component.OnOperationQueued = new Microsoft.AspNetCore.Components.EventCallback<RVideoSceneVideoRenderResult>(
            null, (Action<RVideoSceneVideoRenderResult>)(result => queuedJobId = result.RenderJobId));
        component.OnQueued = new Microsoft.AspNetCore.Components.EventCallback(null, (Action)(() => reloads++));
        await component.RunAsync();
        var snackbar = (SnackbarProxy)(object)typeof(RVideoSupersedeButton)
            .GetProperty("Snackbar", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component)!;
        Assert.Contains(action.JobId.ToString(), snackbar.Message);
        Assert.Equal(1, reloads);
        Assert.Equal(action.JobId, queuedJobId);
    }

    [Fact]
    public async Task LostResponseRetryRetainsTheSameAcknowledgementKey()
    {
        var (component,action,dialog)=Create(true);
        dialog.Answer=new TaskCompletionSource<bool?>(); dialog.Answer.SetResult(true);
        action.ThrowAfterSubmit=true;
        await component.RunAsync();
        var key=action.Acknowledgement!.IdempotencyKey;
        action.ThrowAfterSubmit=false;
        await component.RunAsync();
        Assert.Equal(1,action.Acknowledgements);
        Assert.Equal(key,action.Acknowledgement!.IdempotencyKey);
    }

    [Theory]
    [InlineData(false,false,false)]
    [InlineData(false,true,true)]
    [InlineData(true,false,true)]
    [InlineData(true,true,true)]
    public void V2AndLegacyExposeExplicitOperationWithoutChangingCompletedSelection(bool headed,bool unresolved,bool expected)
    {
        var selected=new SceneVideoVersionDto {Id=Guid.NewGuid(),Status="completed",IsSelected=true};
        var state=new SceneVideoUiState(selected,new RenderJobDto {Id=Guid.NewGuid(),Status=unresolved?"pending_reconciliation":"completed"},null,
            unresolved?SceneVideoSubmitBlockReason.ExistingIntentUnresolved:SceneVideoSubmitBlockReason.None,unresolved,headed?1L:null);
        var scene=new VideoProjectSceneDto {Id=42};
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        foreach(var (component,field,method) in new (object,string,string)[] {
            (new RVideoV2Preview(),"_videoStates","NeedsExplicitOperation"),
            (new RenderVideoJobs(),"_sceneVideoUiStates","NeedsExplicitVideoOperation")})
        {
            var states=(Dictionary<long,SceneVideoUiState>)component.GetType().GetField(field,flags)!.GetValue(component)!;
            states[42]=state;
            Assert.Equal(component is RenderVideoJobs || expected,component.GetType().GetMethod(method,flags)!.Invoke(component,[scene]));
            Assert.Same(selected,states[42].SelectedVersion);
            Assert.True(selected.IsSelected);
        }
    }

    [Fact]
    public void BothUiModelSelectorsRetainSerializedNonDefaultChoice()
    {
        var policy=RVideoVideoModelPolicy.Models.Last();
        var option=new SceneVideoModelOption(RVideoSceneVideoModelOptionsService.BuildKey(policy.ProviderCode,policy.Model,policy.Mode),
            policy.ProviderCode,policy.Model,policy.Mode,"chosen",[8]);
        var first=new SceneVideoModelOption("first|model|mode","first","model","mode","default",[8]);
        var metadata=new ScenePromptMetadata(); metadata.Extra["video_model"]=option.Key;
        var scene=new VideoProjectSceneDto {Id=42,ScenePrompt=metadata.Serialize()};
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var v2=new RVideoV2Preview();
        typeof(RVideoV2Preview).GetField("_opts",flags)!.SetValue(v2,new[]{first,option});
        Assert.Equal(option.Key,typeof(RVideoV2Preview).GetMethod("ResolveModelKey",flags)!.Invoke(v2,[scene]));
        var legacy=new RenderVideoJobs();
        typeof(RenderVideoJobs).GetField("_sceneVideoModelOptions",flags)!.SetValue(legacy,new[]{first,option});
        Assert.Same(option,typeof(RenderVideoJobs).GetMethod("ResolveSceneVideoModelOption",flags)!.Invoke(legacy,[scene]));
    }

    [Fact]
    public async Task ConsentFreezesPromptDurationAndModelWhileParentChanges()
    {
        var (component, action, dialog) = Create(false);
        component.HideTrigger = true;
        component.Scene.VideoPrompt = "confirmed prompt";
        component.Scene.DurationSeconds = 4;
        component.Scene.ScenePrompt = "confirmed metadata";
        dialog.Answer = new TaskCompletionSource<bool?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = component.RunAsync();
        component.Scene.VideoPrompt = "later edit";
        component.Scene.DurationSeconds = 8;
        component.Scene.ScenePrompt = "later metadata";
        dialog.Answer.SetResult(true);
        await run;
        Assert.Equal("confirmed prompt", action.Request!.Scene.VideoPrompt);
        Assert.Equal(4, action.Request.Scene.DurationSeconds);
        Assert.Equal("confirmed metadata", action.Request.Scene.ScenePrompt);
    }

    [Fact]
    public void DropdownEditsRemainDirtyAndPersistThroughMetadata()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var page = new RenderVideoJobs();
        var option = new SceneVideoModelOption("provider|model|flash", "provider", "model", "flash", "model", [4,6]);
        typeof(RenderVideoJobs).GetField("_sceneVideoModelOptions", flags)!.SetValue(page, new[] { option });
        var scene = new VideoProjectSceneDto { Id=42, DurationSeconds=6 };
        typeof(RenderVideoJobs).GetMethod("OnSceneVideoModelChanged", flags)!.Invoke(page, [scene, option.Key]);
        typeof(RenderVideoJobs).GetMethod("OnSceneVideoDurationChanged", flags)!.Invoke(page, [scene, 4]);
        var draft = typeof(RenderVideoJobs).GetMethod("GetSceneDraft", flags)!.Invoke(page, [scene])!;
        Assert.True((bool)draft.GetType().GetProperty("IsDirty")!.GetValue(draft)!);
        var metadata = (ScenePromptMetadata)typeof(RenderVideoJobs).GetMethod("BuildMetadata", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [draft])!;
        Assert.Equal(option.Key, metadata.Extra["video_model"]);
        Assert.Equal("4", metadata.Extra["video_duration"]);
    }

    [Fact]
    public void StrictNewOperationHasOnlyTheConfirmedModelAndMode()
    {
        var policy = RVideoVideoModelPolicy.Models.Last();
        var input = new SceneVideoRenderWorkItemInput { ProviderCode=policy.ProviderCode,
            RequestedModelCode=policy.Model, RequestedMode=policy.Mode, ManualOverride=true, StrictRequestedSettings=true };
        var policies = (IReadOnlyList<RVideoVideoModelPolicyEntry>)typeof(SceneVideoWorkerHandler)
            .GetMethod("ResolveOrderedPolicies", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [input])!;
        var selected = Assert.Single(policies);
        Assert.Equal(policy.Model, selected.Model);
        Assert.Equal(policy.Mode, selected.Mode);
    }

    [Theory]
    [InlineData(4, "720p", 1)]
    [InlineData(5, "720p", 0)]
    [InlineData(4, "1080p", 0)]
    public void StrictNewOperationNeverRoundsDurationOrResolution(int duration, string resolution, int expected)
    {
        var policy = RVideoVideoModelPolicy.Models.First();
        var input = new SceneVideoRenderWorkItemInput { ProviderCode=policy.ProviderCode,
            RequestedModelCode=policy.Model, RequestedMode=policy.Mode, ManualOverride=true,
            StrictRequestedSettings=true, DurationSeconds=duration, Resolution=resolution };
        var catalog = new[] { new AiProviderModelListItemDto {
            ProviderCode=policy.ProviderCode, ProviderModelCode=policy.Model, MediaType="video", Enabled=true,
            SupportedDurations=[4,6], SupportedResolutions=["720p"] } };
        var candidates = (System.Collections.IEnumerable)typeof(SceneVideoWorkerHandler)
            .GetMethod("ResolveFallbackCandidates", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [input,catalog])!;
        Assert.Equal(expected, candidates.Cast<object>().Count());
    }

    [Theory]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(8)]
    public async Task ReloadedSceneIsQuotedAndSubmittedInsteadOfOldComponentDto(int duration)
    {
        var (component, action, dialog) = Create(false);
        component.Scene.DurationSeconds = 4; // stale component parameter survives the parent reload
        var metadata = new ScenePromptMetadata();
        metadata.Extra["video_duration"] = duration.ToString();
        var saved = new VideoProjectSceneDto { Id=42, ProjectId=1, DurationSeconds=duration,
            VideoPrompt="new prompt", ScenePrompt=metadata.Serialize() };
        component.PrepareRequest = () => Task.FromResult<RVideoSceneVideoRenderRequest?>(
            new(1, saved, ScenePromptMetadata.FromScene(saved)));
        dialog.Answer = new TaskCompletionSource<bool?>(); dialog.Answer.SetResult(true);
        await component.RunAsync();
        Assert.Equal(duration, action.EstimateRequest!.Scene.DurationSeconds);
        Assert.Equal(duration, action.Request!.ConfirmedSettings!.DurationSeconds);
        Assert.Equal(duration * action.Rate, action.Request.ConfirmedSettings.EstimatedPoints);
        Assert.Equal(duration, action.Request.ConfirmedSettings.CreateWorkItem().RequestedDurationSeconds);
        Assert.Contains($"Thời lượng: {duration} giây", dialog.Message);
        Assert.Contains("Đơn giá:", dialog.Message);
    }

    [Fact]
    public async Task UncertainResponseKeepsTheOriginalPriceAndSettingsForReplay()
    {
        var (component, action, dialog) = Create(false);
        component.Scene.DurationSeconds=6;
        dialog.Answer = new TaskCompletionSource<bool?>(); dialog.Answer.SetResult(true);
        action.ThrowAfterSubmit=true;
        await component.RunAsync();
        var quote=action.Request!.ConfirmedSettings;
        action.Rate=9m; component.Scene.DurationSeconds=8;
        action.ThrowAfterSubmit=false;
        await component.RunAsync();
        Assert.Same(quote, action.Request!.ConfirmedSettings);
        Assert.Equal(6, action.Request.ConfirmedSettings!.DurationSeconds);
    }

    private static (RVideoSupersedeButton Component, FakeAction Action, DialogProxy Dialog) Create(bool unknown)
    {
        var auth=new AuthStateService(null!,null!,NullLogger<AuthStateService>.Instance);
        typeof(AuthStateService).GetProperty(nameof(AuthStateService.CurrentUser))!.SetValue(auth,SceneVideoOperationDraftTests.Actor());
        var action=new FakeAction();
        var dialog=DispatchProxy.Create<IDialogService,DialogProxy>();
        var component=new RVideoSupersedeButton { ProjectId=1,Scene=new VideoProjectSceneDto {Id=42},Visible=true,OutcomeUnknown=unknown };
        void Inject(string name,object value)=>typeof(RVideoSupersedeButton).GetProperty(name,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(component,value);
        Inject("AuthState",auth); Inject("VideoAction",action); Inject("DialogService",dialog);
        Inject("Snackbar",DispatchProxy.Create<ISnackbar,SnackbarProxy>());
        Inject("Logger",NullLogger<RVideoSupersedeButton>.Instance);
        return(component,action,(DialogProxy)(object)dialog);
    }

    public class DialogProxy:DispatchProxy
    {
        public TaskCompletionSource<bool?>? Answer;
        public string Message="";
        protected override object? Invoke(MethodInfo? method,object?[]? args)
        {
            if(method!.Name=="ShowMessageBoxAsync") { Message=System.Net.WebUtility.HtmlDecode(args![1]?.ToString()??"").Replace("<br />", "\n"); return Answer!.Task; }
            throw new InvalidOperationException(method.Name);
        }
    }
    public class SnackbarProxy:DispatchProxy
    {
        public string Message = "";
        protected override object? Invoke(MethodInfo? method,object?[]? args)
        { if (method?.Name == "Add") Message = args?[0]?.ToString() ?? ""; return null; }
    }
    private sealed class FakeAction:IRVideoSceneVideoRenderAction
    {
        public int Acknowledgements,Submits; public bool CanStart=true,ThrowAfterSubmit;
        public Guid JobId = Guid.NewGuid();
        public RVideoSceneVideoRenderRequest? Request;
        public RVideoSceneVideoRenderRequest? EstimateRequest;
        public decimal Rate = 0.8m;
        public SceneVideoSupersedeAcknowledgement? Acknowledgement;
        public Task<SceneVideoSupersedeAcknowledgement> GetSupersedeAcknowledgementAsync(long projectId,long sceneId,CurrentUserSession user,Guid key,CancellationToken ct=default)
        { Acknowledgements++; return Task.FromResult(SceneVideoOperationDraftTests.Ack() with {SceneId=sceneId,IdempotencyKey=key,RiskAcknowledged=false}); }
        public Task<RVideoSceneVideoRenderEstimate> EstimateAsync(RVideoSceneVideoRenderRequest request,CancellationToken ct=default)
        {
            EstimateRequest = request;
            var duration = request.Metadata.Extra.TryGetValue("video_duration", out var text) ? int.Parse(text)
                : request.Scene.DurationSeconds > 0 ? request.Scene.DurationSeconds : 8;
            var input = new SceneVideoRenderWorkItemInput { ProjectId=request.ProjectId, SceneId=request.Scene.Id,
                ProviderCode="test-provider", ModelName="test-model", RequestedModelCode="test-model", RequestedMode="flash",
                DurationSeconds=duration, RequestedDurationSeconds=duration, VideoPrompt=request.Scene.VideoPrompt ?? "prompt",
                AspectRatio="9:16", Resolution="720p", CustomerPointRate=Rate, EstimatedPoints=duration*Rate };
            var rate = new PointPricingRate("video", "standard", Rate, "per_second", "test");
            var quote = RVideoSceneVideoOperationSettings.Capture(request.Scene, input, "Test model", request.ServiceId, rate);
            return Task.FromResult(new RVideoSceneVideoRenderEstimate(Rate,quote.EstimatedPoints,100,100-quote.EstimatedPoints,0,CanStart)
                { OperationSettings=quote });
        }
        public Task<RVideoSceneVideoRenderResult> SupersedeAsync(RVideoSceneVideoRenderRequest request,SceneVideoSupersedeAcknowledgement acknowledgement,CurrentUserSession user,CancellationToken ct=default)
        { Submits++; Request=request; Acknowledgement=acknowledgement; if(ThrowAfterSubmit) throw new IOException("lost response"); return Task.FromResult(RVideoSceneVideoRenderResult.Ok(JobId)); }
        public Task<RVideoSceneVideoRenderResult> EnqueueRerenderAsync(RVideoSceneVideoRenderRequest request,CurrentUserSession user,CancellationToken ct=default)
            =>throw new InvalidOperationException("Explicit operation must not use legacy retry.");
    }
}
