using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using TodoX.Web.Components.Pages;
using TodoX.Web.Models;
using TodoX.Web.Services;
using TodoX.Web.Services.VideoRender;
using Xunit;
namespace TodoX.Web.Tests;
public sealed class RVideoBootstrapBehaviorTests
{
 const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
 [Theory]
 [InlineData(false)]
 [InlineData(true)]
 public async Task SlowRequestRemainsLoadingThenFailureExitsToRetry(bool timeout)
 {
  var auth=new AuthStateService(null!,null!,NullLogger<AuthStateService>.Instance);
  typeof(AuthStateService).GetProperty("IsInitialized")!.SetValue(auth,true);
  typeof(AuthStateService).GetProperty("CurrentUser")!.SetValue(auth,new CurrentUserSession {UserId=Guid.NewGuid(),Role=TodoXUserRole.CustomerOwner});
  var action=DispatchProxy.Create<IRVideoJobService,Probe>();
  var probe=(Probe)(object)action;
  using var page=new RVideoJobDetail {JobId=Guid.NewGuid()};
  void Inject(string name,object value)=>typeof(RVideoJobDetail).GetProperty(name,Private)!.SetValue(page,value);
  Inject("AuthState",auth);Inject("RVideoJobs",action);Inject("Logger",NullLogger<RVideoJobDetail>.Instance);
  var task=(Task)typeof(RVideoJobDetail).GetMethod("LoadJobAsync",Private)!.Invoke(page,[])!;
  Assert.Equal("LoadingJob",typeof(RVideoJobDetail).GetField("_bootstrapState",Private)!.GetValue(page)!.ToString());
  Assert.False(task.IsCompleted);
  probe.Response.SetException(timeout ? new TimeoutException() : new InvalidOperationException("local failure"));
  await task;
  Assert.Equal("Error",typeof(RVideoJobDetail).GetField("_bootstrapState",Private)!.GetValue(page)!.ToString());
  Assert.False((bool)typeof(RVideoJobDetail).GetField("_loadInProgress",Private)!.GetValue(page)!);
  Assert.NotNull(typeof(RVideoJobDetail).GetField("_loadWarning",Private)!.GetValue(page));
 }
 public class Probe:DispatchProxy
 {
  public TaskCompletionSource<RVideoJobView?> Response=new(TaskCreationOptions.RunContinuationsAsynchronously);
  protected override object? Invoke(MethodInfo? method,object?[]? args)
  {Assert.Equal("GetByJobIdAsync",method!.Name);return Response.Task;}
 }
}
