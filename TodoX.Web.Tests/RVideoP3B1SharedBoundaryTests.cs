using TodoX.Web.Models;
using TodoX.Web.Services.VideoRender;
using Xunit;
namespace TodoX.Web.Tests;
public sealed class RVideoP3B1SharedBoundaryTests
{
    private static string WebRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..","..","..","..","TodoX.Web"));
    private static string Read(params string[] p) => File.ReadAllText(Path.Combine(new[]{WebRoot}.Concat(p).ToArray()));
    [Fact] public void Validator_NullMetadata_Rejected(){ var v=new RVideoSceneDraftValidator(); var r=v.Validate(null!); Assert.False(r.IsValid); Assert.Equal("SCENE_METADATA_NULL",r.ErrorCode); }
    [Fact] public void Validator_UnresolvedImageSource_Rejected(){ var v=new RVideoSceneDraftValidator(); var m=new ScenePromptMetadata{ImagePrompt="[[PLACEHOLDER]]",EffectiveImagePrompt=null}; var r=v.Validate(m); Assert.False(r.IsValid); Assert.Equal("SCENE_IMAGE_SOURCE_UNRESOLVED",r.ErrorCode); }
    [Fact] public void Validator_ValidAccepted(){ var v=new RVideoSceneDraftValidator(); var m=new ScenePromptMetadata{ImagePrompt="A studio shot, bright",EffectiveImagePrompt="A studio shot, bright",MotionPrompt="slow dolly"}; var r=v.Validate(m); Assert.True(r.IsValid); }
    [Fact] public void Validator_PlaceholderWithFallback_Accepted(){ var v=new RVideoSceneDraftValidator(); var m=new ScenePromptMetadata{ImagePrompt="[[PLACEHOLDER]]",EffectiveImagePrompt="fallback usable prompt"}; var r=v.Validate(m); Assert.True(r.IsValid); }
    [Fact] public void SaveAction_ValidatesAndNormalizes(){ var src=Read("Services","VideoRender","RVideoSceneDraftSaveAction.cs"); Assert.Contains("_validator.Validate",src); Assert.Contains("metadata.Serialize()",src); Assert.Contains("SaveSceneDraftAsync",src); Assert.DoesNotContain("Billing",src); }
    [Fact] public void SaveAction_PreservesExtraViaSerialize(){ var m=new ScenePromptMetadata{ImagePrompt="img",MotionPrompt="motion",EffectiveImagePrompt="img"}; m.Extra["custom_key"]="keep_me"; m.Extra["video_model"]="veo_3_1"; var parsed=ScenePromptMetadata.Parse(m.Serialize()); Assert.Equal("keep_me",parsed.Extra["custom_key"]); Assert.Equal("veo_3_1",parsed.Extra["video_model"]); }
}
