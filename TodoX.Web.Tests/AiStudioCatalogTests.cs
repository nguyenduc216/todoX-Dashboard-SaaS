using System.Text;
using TodoX.Web.Models;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class AiStudioCatalogTests
{
    [Fact]
    public void VoiceRules_ValidateRequiredFieldsRateRangeAndCompatibilityAliases()
    {
        AiStudioCatalogRules.ValidateVoice(new AiStudioVoiceDto
        {
            Name = "Ngọc Huyền",
            Code = "vbee_ngochuyen",
            ProviderCode = "vbee",
            ProviderVoiceId = "provider-id-from-config",
            DefaultRate = 1.0m,
            MinRate = 0.8m,
            MaxRate = 1.2m
        });

        Assert.Throws<InvalidOperationException>(() => AiStudioCatalogRules.ValidateVoice(new AiStudioVoiceDto()));
        Assert.Throws<InvalidOperationException>(() => AiStudioCatalogRules.ValidateVoice(new AiStudioVoiceDto { Name = "Voice", Code = "v", ProviderCode = "vbee", DefaultRate = 0 }));
        Assert.Throws<InvalidOperationException>(() => AiStudioCatalogRules.ValidateVoice(new AiStudioVoiceDto { Name = "Voice", Code = "v", ProviderCode = "vbee", ProviderVoiceId = "id", DefaultRate = 1.3m, MinRate = 0.8m, MaxRate = 1.2m }));

        Assert.Equal("vbee_phuthang", AiStudioCatalogRules.RVideoCompatibilityVoiceCodes["a1"]);
        Assert.Equal("vbee_ngochuyen", AiStudioCatalogRules.RVideoCompatibilityVoiceCodes["a2"]);
        Assert.Equal("vbee_minhduc", AiStudioCatalogRules.RVideoCompatibilityVoiceCodes["a3"]);
        Assert.Equal("custom", AiStudioCatalogRules.RVideoCompatibilityVoiceCodes["a4"]);
    }

    [Fact]
    public void MusicRules_ValidateVolumeAndRequireLocalMp3ForActivation()
    {
        AiStudioCatalogRules.ValidateMusic(new AiStudioMusicDto
        {
            Name = "Corporate",
            Code = "corporate_energy_01",
            Category = "corporate",
            DefaultVolume = 0.8m
        });

        AiStudioCatalogRules.ValidateMusicMp3Upload("preview.mp3", "audio/mpeg", 128);
        AiStudioCatalogRules.ValidateAudioUpload("voice.wav", "audio/wav", 128);
        AiStudioCatalogRules.ValidateAudioUpload("voice.m4a", "audio/mp4", 128);
        Assert.Throws<InvalidOperationException>(() => AiStudioCatalogRules.ValidateMusic(new AiStudioMusicDto { Name = "Bad", Code = "bad", DefaultVolume = 1.1m }));
        Assert.Throws<InvalidOperationException>(() => AiStudioCatalogRules.ValidateMusicMp3Upload("track.wav", "audio/wav", 128));
        Assert.Throws<InvalidOperationException>(() => AiStudioCatalogRules.ValidateMusicMp3Upload("track.m4a", "audio/mp4", 128));
        Assert.Throws<InvalidOperationException>(() => AiStudioCatalogRules.ValidateMusicMp3Upload("track.mp3", "text/plain", 128));
        Assert.Throws<InvalidOperationException>(() => AiStudioCatalogRules.ValidateMusicMp3Upload("empty.mp3", "audio/mpeg", 0));
        Assert.Throws<InvalidOperationException>(() => AiStudioCatalogRules.EnsureMusicCanBeActive(new AiStudioMusicDto
        {
            Name = "Missing file",
            Code = "missing_file",
            IsActive = true
        }));

        var localMusic = new AiStudioMusicDto
        {
            Name = "Local MP3",
            Code = "local_mp3",
            FileName = "local.mp3",
            StorageKey = "ai-studio/music/local/local.mp3",
            FileUrl = "/uploads/ai-studio/music/local/local.mp3",
            MimeType = "audio/mpeg",
            IsActive = true
        };
        AiStudioCatalogRules.EnsureMusicCanBeActive(localMusic);
        Assert.True(AiStudioCatalogRules.HasLocalMusicFile(localMusic));
    }

    [Fact]
    public void SourceContracts_AddSharedCatalogWithoutChangingVideoRuntimes()
    {
        var program = ReadSource("TodoX.Web", "Program.cs");
        var endpoints = ReadSource("TodoX.Web", "Services", "AiStudioCatalogEndpoints.cs");
        var service = ReadSource("TodoX.Web", "Services", "AiStudioCatalogService.cs");
        var media = ReadSource("TodoX.Web", "Services", "Media", "MediaFileService.cs");
        var menu = ReadSource("TodoX.Web", "Components", "Layout", "MainLayout.razor");
        var voicePage = ReadSource("TodoX.Web", "Components", "Pages", "AiStudioVoices.razor");
        var musicPage = ReadSource("TodoX.Web", "Components", "Pages", "AiStudioMusic.razor");
        var voiceDialog = ReadSource("TodoX.Web", "Components", "Dialogs", "AiStudioVoiceDialog.razor");
        var musicDialog = ReadSource("TodoX.Web", "Components", "Dialogs", "AiStudioMusicDialog.razor");
        var migration = ReadSource("database", "migrations", "20260817_ai_studio_voice_music_catalog.sql");

        Assert.Contains("AddScoped<IAiStudioCatalogService, AiStudioCatalogService>", program, StringComparison.Ordinal);
        Assert.Contains("app.MapAiStudioCatalogEndpoints()", program, StringComparison.Ordinal);
        Assert.Contains("MapGroup(\"/api/admin/ai-studio\")", endpoints, StringComparison.Ordinal);
        Assert.Contains("MapGet(\"/voices\"", endpoints, StringComparison.Ordinal);
        Assert.Contains("MapGet(\"/music\"", endpoints, StringComparison.Ordinal);
        Assert.Contains("MapPost(\"/music/{id:guid}/import-url\"", endpoints, StringComparison.Ordinal);
        Assert.Contains("MapGroup(\"/api/ai-studio\")", endpoints, StringComparison.Ordinal);
        Assert.Contains("activeOnly: true", endpoints, StringComparison.Ordinal);
        Assert.Contains("ProviderVoiceId", service, StringComparison.Ordinal);
        Assert.Contains("UPDATE public.ai_studio_voices SET is_default=false", service, StringComparison.Ordinal);
        Assert.Contains("UPDATE public.ai_studio_music SET is_default=false", service, StringComparison.Ordinal);
        Assert.Contains("ai-studio/voices/{voice.Code}/preview-", service, StringComparison.Ordinal);
        Assert.Contains("ai-studio/music/{code}/", service, StringComparison.Ordinal);
        Assert.Contains("ImportMusicFromUrlAsync", service, StringComparison.Ordinal);
        Assert.Contains("ValidatePublicMusicUriAsync", service, StringComparison.Ordinal);
        Assert.Contains("MUSIC_URL_PRIVATE_ADDRESS", service, StringComparison.Ordinal);
        Assert.Contains("AllowAutoRedirect = false", program, StringComparison.Ordinal);
        Assert.Contains("\"audio/mpeg\"", media, StringComparison.Ordinal);
        Assert.Contains("GetMaxAudioBytes", media, StringComparison.Ordinal);
        Assert.Contains("BuildAiStudioVoicesItem", menu, StringComparison.Ordinal);
        Assert.Contains("BuildAiStudioMusicItem", menu, StringComparison.Ordinal);
        Assert.Contains("@page \"/admin/ai-studio/voices\"", voicePage, StringComparison.Ordinal);
        Assert.Contains("@page \"/admin/ai-studio/music\"", musicPage, StringComparison.Ordinal);
        Assert.Contains("<audio controls", voicePage, StringComparison.Ordinal);
        Assert.Contains("<audio controls", musicPage, StringComparison.Ordinal);
        Assert.Contains("ShowAsync<Components.Dialogs.AiStudioVoiceDialog>", voicePage, StringComparison.Ordinal);
        Assert.Contains("ShowAsync<Components.Dialogs.AiStudioMusicDialog>", musicPage, StringComparison.Ordinal);
        Assert.DoesNotContain("lg=\"4\"", voicePage, StringComparison.Ordinal);
        Assert.DoesNotContain("lg=\"4\"", musicPage, StringComparison.Ordinal);
        Assert.Contains("<MudDialog Class=\"ai-studio-asset-dialog\">", voiceDialog, StringComparison.Ordinal);
        Assert.Contains("<MudDialog Class=\"ai-studio-asset-dialog ai-studio-music-dialog\">", musicDialog, StringComparison.Ordinal);
        Assert.Contains("accept=\"audio/mpeg,.mp3\"", musicDialog, StringComparison.Ordinal);
        Assert.Contains("ImportMusicFromUrlAsync", musicDialog, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS public.ai_studio_voices", migration, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS public.ai_studio_music", migration, StringComparison.Ordinal);
        Assert.Contains("ux_ai_studio_voices_code", migration, StringComparison.Ordinal);
        Assert.Contains("ux_ai_studio_music_code", migration, StringComparison.Ordinal);
        Assert.Contains("ux_ai_studio_voices_active_default", migration, StringComparison.Ordinal);
        Assert.Contains("ux_ai_studio_music_active_default", migration, StringComparison.Ordinal);
        Assert.Contains("intentionally not guessed", migration, StringComparison.Ordinal);

        Assert.DoesNotContain("Services\\VideoRender", service, StringComparison.Ordinal);
        Assert.DoesNotContain("Services\\DanceSell", service, StringComparison.Ordinal);
        Assert.DoesNotContain("Services\\Timelapse", service, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] parts)
        => ReadStrictUtf8(Path.Combine(new[] { FindRepoRoot() }.Concat(parts).ToArray()));

    private static string ReadStrictUtf8(string file)
        => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
            .GetString(File.ReadAllBytes(file));

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "TodoX.Dashboard.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
