using TodoX.Web.Components.Pages.RVideo;
using TodoX.Web.Models;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class RVideoV2VoiceLibraryTests
{
    private static string Info => File.ReadAllText(Path.Combine(
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..")),
        "TodoX.Web", "Components", "Pages", "RVideo", "RVideoV2Info.razor"));

    [Fact]
    public void A_failed_or_empty_voice_load_is_retried_and_logged()
    {
        var info = Info;
        Assert.DoesNotContain("_catalogsLoaded", info);
        Assert.Contains("_voicesLoaded=_voices.Count>0", info);
        Assert.Contains("RVIDEO_V2_INFO_VOICES_UNAVAILABLE", info);
        Assert.DoesNotContain("try{ _voices.AddRange(await Catalog.ListVoicesAsync(activeOnly:true)); }catch{}", info);
        // Opening the picker or choosing "Giọng từ thư viện" loads the list again when it is still empty.
        Assert.Contains("OnOpen=\"@(async ()=>{ await EnsureVoicesLoadedAsync();", info);
        Assert.Contains("await EnsureVoicesLoadedAsync();\n      if(string.IsNullOrWhiteSpace(_advVoiceCode)) _advVoiceCode=DefaultLibraryVoiceCode(_voices);", info.Replace("\r\n", "\n"));
    }

    [Fact]
    public void Choosing_the_library_mode_picks_the_default_voice()
    {
        var voices = new List<AiStudioVoiceDto>
        {
            new() { Code = "a", Name = "A" },
            new() { Code = "b", Name = "B", IsDefault = true }
        };
        Assert.Equal("b", RVideoV2Info.DefaultLibraryVoiceCode(voices));
        Assert.Equal("a", RVideoV2Info.DefaultLibraryVoiceCode(voices.Take(1).ToList()));
        Assert.Null(RVideoV2Info.DefaultLibraryVoiceCode([]));
    }
}
