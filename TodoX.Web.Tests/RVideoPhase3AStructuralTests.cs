using Xunit;
namespace TodoX.Web.Tests;
public sealed class RVideoPhase3AStructuralTests
{
    private static string Rp(params string[] p)=>File.ReadAllText(Path.Combine(new[]{Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","..","..","..","TodoX.Web"))}.Concat(p).ToArray()));
    private static string V2Info=>Rp("Components","Pages","RVideo","RVideoV2Info.razor");
    private static string Legacy=>Rp("Components","Pages","RenderVideoJobs.razor");
    private static string Speech=>Rp("Services","PromptAssistant","PromptSpeechHandler.cs");
    private static string Merger=>Rp("Services","PromptAssistant","QuickPromptTranscriptMerger.cs");
    private static string Quick=>Rp("Components","Dialogs","QuickPromptDialog.razor");
    private static string Js=>Rp("wwwroot","js","todox-speech-input.js");
    private static string Action=>Rp("Services","VideoRender","RVideoPromptGenerationAction.cs");
    [Fact] public void Adv_DefaultsFalse(){Assert.Contains("_adv=false",V2Info);}
    [Fact] public void WorkingInput_Guards(){Assert.Contains("_lastWorkingProjectId",V2Info);Assert.Contains("_lastWorkingActiveId",V2Info);Assert.Contains("RefreshWorkingInputFromActiveAsync",V2Info);Assert.Contains("LooksLikeJson",V2Info);Assert.Contains("GetProjectGenerationAsync",V2Info);Assert.Contains("GetProjectGenerationsAsync",V2Info);}
    [Fact] public void WorkingInput_ActiveWins(){Assert.Contains("UserInput",V2Info);Assert.Contains("OriginalPrompt",V2Info);}
    [Fact] public void History_Chain(){Assert.Contains("SetActiveProjectGenerationAsync",V2Info);Assert.Contains("OnReloadRequested",V2Info);Assert.Contains("HandleHistoryAsync",V2Info);}
    [Fact] public void ExactlyOnce_Guard(){Assert.Contains("_generating",V2Info);Assert.Contains("string.IsNullOrWhiteSpace(_workingInput)",V2Info);Assert.Contains("confirm",V2Info);Assert.Contains("PromptAction.GenerateAsync",V2Info);}
    [Fact] public void SharedAction_V2UsesAction(){Assert.Contains("IRVideoPromptGenerationAction",V2Info);Assert.Contains("PromptAction.GenerateAsync",V2Info);}
    [Fact] public void SharedAction_V2NotDirectAssistantForGenerate(){var seg=V2Info.Substring(V2Info.IndexOf("HandleGenerateAsync",StringComparison.Ordinal));Assert.DoesNotContain("PromptAssistant.GeneratePromptAsync",seg);}
    [Fact] public void SharedAction_LegacyUsesAction(){Assert.Contains("IRVideoPromptGenerationAction",Legacy);Assert.Contains("RVideoPromptAction.GenerateAsync",Legacy);}
    [Fact] public void Speech_ViVn(){Assert.Contains("\"vi-VN\"",Speech);Assert.Contains("todoXSpeechInput",Speech);}
    [Fact] public void Speech_FinalAppend(){Assert.Contains("PromptSpeechHandler",V2Info);Assert.Contains("QuickPromptTranscriptMerger.AppendTranscript",V2Info);Assert.Contains("QuickPromptTranscriptMerger.AppendTranscript",Quick);}
    [Fact] public void Speech_NoAutoGenerate(){Assert.Contains("OnGenerate",Quick);Assert.Contains("OnApplyInput",Quick);Assert.DoesNotContain("PromptAction.GenerateAsync",Quick);}
    [Fact] public void Speech_DisposeAsync(){Assert.Contains("DisposeAsync",Speech);Assert.Contains("DisposeAsync",V2Info);Assert.Contains("DisposeAsync",Quick);}
    [Fact] public void Speech_JsModule(){Assert.Contains("todoXSpeechInput",Js);Assert.Contains("isSupported",Js);Assert.Contains("invokeMethodAsync",Js);}
    [Fact] public void QuickPrompt_Contracts(){Assert.Contains("CanonicalInput",Quick);Assert.Contains("CanonicalInput",V2Info);Assert.Contains("HandleGenerateAsync",V2Info);}
    [Fact] public void Advanced_PreservedViaHelper(){Assert.Contains("RVideoJobUpdateHelper.BuildSafeUpdate",V2Info);Assert.Contains("HandleAdvancedTotalSecondsChangedAsync",V2Info);}
    [Fact] public void References_ReadOnly(){Assert.Contains("FilteredRefs",V2Info);Assert.Contains("UploadedCharacterUrl",V2Info);Assert.Contains("SourceImageUrl",V2Info);Assert.Contains("StaticImageUrl",V2Info);Assert.DoesNotContain("HandleUpload",V2Info);Assert.DoesNotContain("DeleteRef",V2Info);}
    [Fact] public void Action_WrapsAssistant(){Assert.Contains("IServicePromptAssistantService",Action);Assert.Contains("GeneratePromptAsync",Action);Assert.Contains("RVideoPromptGenerationRequest",Action);}
}
