using System.Text.Json;
using TodoX.Web.Models;
using TodoX.Web.Services.Platform;
using TodoX.Web.Services.VideoRender;
using Xunit;
namespace TodoX.Web.Tests;
public sealed class RVideoJobUpdateHelperTests
{
    private static RVideoJobView V(string? ia=null,string? ir=null,string? pa=null,string? pr=null,string t="T",string op="plain",int ts=27,int ss=9,bool th=true,string? url=null,RVideoJobSettingsDto? s=null)
    {
        var ij = Build(ia,ir);
        var core=new CoreJobView(Guid.NewGuid(),Guid.NewGuid(),"rvideo",Guid.NewGuid(),Guid.NewGuid(),"draft","dashboard",null,null,null,0,0,0,"not_required",null,ij,JsonDocument.Parse("[]").RootElement.Clone(),null,null,DateTime.UtcNow,null,null);
        var proj=new VideoProjectDto{Id=123,CoreJobId=core.JobId,TenantId=Guid.NewGuid(),Title=t,OriginalPrompt=op,TotalSeconds=ts,SceneSeconds=ss,ThinkScenes=th,SourceImageUrl=url,Status=VideoProjectStatuses.Draft,StorageRoot="/tmp",PublicBase="https://cdn.test",JobFolder="j"};
        if(pa!=null||pr!=null){var o=new System.Text.Json.Nodes.JsonObject(); if(pa!=null) o["aspectRatio"]=pa; if(pr!=null) o["resolution"]=pr; proj.OriginalPrompt=o.ToJsonString();} else proj.OriginalPrompt=op;
        return new RVideoJobView{CoreJob=core,Project=proj,Settings=s??Def()};
    }
    private static JsonElement Build(string? a,string? r){if(a==null&&r==null) return JsonDocument.Parse("{}").RootElement.Clone(); var o=new System.Text.Json.Nodes.JsonObject(); if(a!=null) o["aspectRatio"]=a; if(r!=null) o["resolution"]=r; return JsonDocument.Parse(o.ToJsonString()).RootElement.Clone();}
    private static RVideoJobSettingsDto Def()=>new(){ProjectId=123,ExecutionMode=RVideoExecutionModes.Manual,CurrentStage=RVideoStages.Info,CharacterMode="NONE",VoiceMode=RVideoVoiceModes.None,DefaultTtsRate=1.0m,MusicVolume=0.8m};
    [Fact] public void P1_16_9_1080p(){var v=V(ia:"16:9",ir:"1080p",ts:27,ss:9,th:true,url:"https://cdn.test/a.jpg");var r=RVideoJobUpdateHelper.BuildSafeUpdate(v,titleOverride:"New Title");Assert.Equal("16:9",r.AspectRatio);Assert.Equal("1080p",r.Resolution);Assert.Equal("New Title",r.Title);Assert.Equal(27,r.TotalSeconds);Assert.Equal(9,r.SceneSeconds);Assert.True(r.ThinkScenes);Assert.Equal("https://cdn.test/a.jpg",r.SourceImageUrl);}
    [Fact] public void P2_9_16_720p(){var v=V(ia:"9:16",ir:"720p");var r=RVideoJobUpdateHelper.BuildSafeUpdate(v,titleOverride:"T2");Assert.Equal("9:16",r.AspectRatio);Assert.Equal("720p",r.Resolution);}
    [Fact] public void P3_CoreWins(){var v=V(ia:"16:9",ir:"1080p",pa:"9:16",pr:"720p");var r=RVideoJobUpdateHelper.BuildSafeUpdate(v);Assert.Equal("16:9",r.AspectRatio);Assert.Equal("1080p",r.Resolution);}
    [Fact] public void P4_PromptFallback(){var v=V(pa:"9:16",pr:"720p");var r=RVideoJobUpdateHelper.BuildSafeUpdate(v);Assert.Equal("9:16",r.AspectRatio);Assert.Equal("720p",r.Resolution);}
    [Fact] public void P5_TotalPreserved(){Assert.Equal(42,RVideoJobUpdateHelper.BuildSafeUpdate(V(ts:42)).TotalSeconds);}
    [Fact] public void P6_ScenePreserved(){Assert.Equal(7,RVideoJobUpdateHelper.BuildSafeUpdate(V(ss:7)).SceneSeconds);}
    [Fact] public void P7_ThinkPreserved(){Assert.True(RVideoJobUpdateHelper.BuildSafeUpdate(V(th:true)).ThinkScenes);Assert.False(RVideoJobUpdateHelper.BuildSafeUpdate(V(th:false)).ThinkScenes);}
    [Fact] public void P8_UrlPreserved(){Assert.Equal("https://cdn.test/x.jpg",RVideoJobUpdateHelper.BuildSafeUpdate(V(url:"https://cdn.test/x.jpg")).SourceImageUrl);}
    [Fact] public void P9_SettingsPreserved(){var s=Def();s.ExecutionMode=RVideoExecutionModes.Auto;s.CharacterMode="LIBRARY";s.SelectedCharacterId=99;s.VoiceMode=RVideoVoiceModes.Native;s.VoiceCatalogCode="vc1";var r=RVideoJobUpdateHelper.BuildSafeUpdate(V(s:s));Assert.Equal(RVideoExecutionModes.Auto,r.Settings.ExecutionMode);Assert.Equal("LIBRARY",r.Settings.CharacterMode);Assert.Equal(99,r.Settings.SelectedCharacterId);Assert.Equal(RVideoVoiceModes.Native,r.Settings.VoiceMode);}
    [Fact] public void P10_AdvancedOnlyTotal(){var v=V(ia:"16:9",ir:"1080p",ts:27,ss:9,th:true,s:Def());var r=RVideoJobUpdateHelper.BuildSafeUpdate(v,totalSecondsOverride:40);Assert.Equal(40,r.TotalSeconds);Assert.Equal("16:9",r.AspectRatio);Assert.Equal("1080p",r.Resolution);Assert.Equal(9,r.SceneSeconds);Assert.True(r.ThinkScenes);}
}
