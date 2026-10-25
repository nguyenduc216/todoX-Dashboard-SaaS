using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor;
using TodoX.Web.Components.Pages.RVideo;
using TodoX.Web.Components.Pages;
using TodoX.Web.Models;
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
        component.OnQueued = new Microsoft.AspNetCore.Components.EventCallback(null, (Action)(() => reloads++));
        await component.RunAsync();
        var snackbar = (SnackbarProxy)(object)typeof(RVideoSupersedeButton)
            .GetProperty("Snackbar", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component)!;
        Assert.Contains(action.JobId.ToString(), snackbar.Message);
        Assert.Equal(1, reloads);
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
            Assert.Equal(expected,component.GetType().GetMethod(method,flags)!.Invoke(component,[scene]));
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
            if(method!.Name=="ShowMessageBoxAsync") { Message=args![1]?.ToString()??""; return Answer!.Task; }
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
        public SceneVideoSupersedeAcknowledgement? Acknowledgement;
        public Task<SceneVideoSupersedeAcknowledgement> GetSupersedeAcknowledgementAsync(long projectId,long sceneId,CurrentUserSession user,Guid key,CancellationToken ct=default)
        { Acknowledgements++; return Task.FromResult(SceneVideoOperationDraftTests.Ack() with {SceneId=sceneId,IdempotencyKey=key,RiskAcknowledged=false}); }
        public Task<RVideoSceneVideoRenderEstimate> EstimateAsync(RVideoSceneVideoRenderRequest request,CancellationToken ct=default)
            =>Task.FromResult(new RVideoSceneVideoRenderEstimate(1,8,100,92,0,CanStart));
        public Task<RVideoSceneVideoRenderResult> SupersedeAsync(RVideoSceneVideoRenderRequest request,SceneVideoSupersedeAcknowledgement acknowledgement,CurrentUserSession user,CancellationToken ct=default)
        { Submits++; Acknowledgement=acknowledgement; if(ThrowAfterSubmit) throw new IOException("lost response"); return Task.FromResult(RVideoSceneVideoRenderResult.Ok(JobId)); }
        public Task<RVideoSceneVideoRenderResult> EnqueueRerenderAsync(RVideoSceneVideoRenderRequest request,CurrentUserSession user,CancellationToken ct=default)
            =>throw new InvalidOperationException("Explicit operation must not use legacy retry.");
    }
}
