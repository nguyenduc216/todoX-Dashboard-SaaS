using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using TodoX.Web.Components.Pages;
using TodoX.Web.Models;
using TodoX.Web.Services;
using TodoX.Web.Services.VideoRender;
using Xunit;
namespace TodoX.Web.Tests;
public sealed class RVideoTwoStageLoadingTests
{
 const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
 [Fact]
 public async Task ParentProjectIsReusedAndChildKeepsSkeletonUntilCriticalMediaWithoutDuplicateBootstrap()
 {
  var auth=new AuthStateService(null!,null!,NullLogger<AuthStateService>.Instance);
  typeof(AuthStateService).GetProperty("CurrentUser")!.SetValue(auth,SceneVideoOperationDraftTests.Actor());
  var versions=DispatchProxy.Create<ISceneMediaVersioningService,Versions>();
  var probe=(Versions)(object)versions;
  await using var page=new RenderVideoJobs {Embedded=true,ProjectId=7,JobId=Guid.NewGuid(),
    InitialProject=new VideoProjectDto {Id=7,Title="existing parent project",Scenes=[new() {Id=42,ProjectId=7}]} };
  void Inject(string name,object value)=>typeof(RenderVideoJobs).GetProperty(name,Flags)!.SetValue(page,value);
  Inject("AuthState",auth);Inject("SceneVersions",versions);Inject("PromptParser",new TodoXVideoPromptParser());
  Inject("Logger",NullLogger<RenderVideoJobs>.Instance);
  Task Load()=>(Task)typeof(RenderVideoJobs).GetMethod("OnParametersSetAsync",Flags)!.Invoke(page,[])!;
  var first=Load();
  Assert.True((bool)typeof(RenderVideoJobs).GetField("_projectDialogOpen",Flags)!.GetValue(page)!);
  Assert.True((bool)typeof(RenderVideoJobs).GetField("_initialLoading",Flags)!.GetValue(page)!);
  Assert.False(first.IsCompleted);
  await Load();Assert.Equal(1,probe.Calls);
  probe.Result.SetResult(new(null,null,null,SceneVideoSubmitBlockReason.None,false));
  await first;
  Assert.False((bool)typeof(RenderVideoJobs).GetField("_initialLoading",Flags)!.GetValue(page)!);
  Assert.True((bool)typeof(RenderVideoJobs).GetField("_supplementalPending",Flags)!.GetValue(page)!);
  Assert.False((bool)typeof(RenderVideoJobs).GetField("_supplementalReady",Flags)!.GetValue(page)!);
  Assert.Same(page.InitialProject,typeof(RenderVideoJobs).GetField("_project",Flags)!.GetValue(page));
  await Load();Assert.Equal(1,probe.Calls);
 }
 public class Versions:DispatchProxy
 {
  public int Calls;public TaskCompletionSource<SceneVideoUiState> Result=new(TaskCreationOptions.RunContinuationsAsynchronously);
  protected override object? Invoke(MethodInfo? method,object?[]? args)
  {Assert.Equal("GetSceneVideoUiStateAsync",method!.Name);Calls++;return Result.Task;}
 }
}
