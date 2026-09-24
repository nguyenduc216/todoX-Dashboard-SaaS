using System.Text.Json;
using TodoX.Web.Models;
using TodoX.Web.Services.PromptAssistant;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class PromptAssistantCharacterReferenceSynchronizerTests
{
    private const string ReferenceHeader = "CHARACTER REFERENCE - REQUIRED";

    [Fact]
    public void NoneRemovesStaleMetadataAndReferenceInstruction()
    {
        var result = Synchronize(Prompt("Character A", true), PromptAssistantCharacterReference.None);

        Assert.Equal("none", result.RootElement.GetProperty("character_reference_mode").GetString());
        Assert.Equal(string.Empty, result.RootElement.GetProperty("character_reference_image").GetString());
        Assert.Equal(JsonValueKind.Null, result.RootElement.GetProperty("character_reference").ValueKind);
        Assert.Equal("Character A", ImagePrompt(result));
    }

    [Fact]
    public void LibraryReferenceUsesActualSelectedImageAndMetadata()
    {
        using var result = Synchronize(Prompt("A pencil boy in a park.", true),
            new(RVideoCharacterModes.Library, "https://media.example/pencil-boy.png", "cau be but chi", "LIBRARY"));

        Assert.Equal("image_reference_required", result.RootElement.GetProperty("character_reference_mode").GetString());
        Assert.Equal("https://media.example/pencil-boy.png", result.RootElement.GetProperty("character_reference_image").GetString());
        var metadata = result.RootElement.GetProperty("character_reference");
        Assert.Equal("cau be but chi", metadata.GetProperty("name").GetString());
        Assert.Equal("https://media.example/pencil-boy.png", metadata.GetProperty("image").GetString());
        Assert.Contains(ReferenceHeader, ImagePrompt(result));
    }

    [Fact]
    public void ReferenceInstructionIsOnlyInjectedForExplicitMainCharacterScene()
    {
        const string prompt = """
            {"scenes":[
              {"main_character_present":true,"image_prompt":"Main scene"},
              {"main_character_present":false,"image_prompt":"Landscape"},
              {"image_prompt":"Unspecified"}
            ]}
            """;

        using var result = Synchronize(prompt, Character("A"));
        var scenes = result.RootElement.GetProperty("scenes");

        Assert.Contains(ReferenceHeader, scenes[0].GetProperty("image_prompt").GetString());
        Assert.Equal("Landscape", scenes[1].GetProperty("image_prompt").GetString());
        Assert.Equal("Unspecified", scenes[2].GetProperty("image_prompt").GetString());
    }

    [Fact]
    public void SwitchingCharacterReplacesAWithBWithoutChangingUnrelatedContent()
    {
        const string prompt = """
            {"video_title":"Keep title","objective":"Keep objective","scenes":[{"main_character_present":true,"image_prompt":"Keep scene","motion_prompt":"Keep motion","duration_seconds":8}]}
            """;
        using var withA = Synchronize(prompt, Character("A"));
        using var withB = Synchronize(withA.RootElement.GetRawText(), Character("B"));

        Assert.Equal("https://media.example/B.png", withB.RootElement.GetProperty("character_reference_image").GetString());
        Assert.DoesNotContain("Selected character: A", ImagePrompt(withB), StringComparison.Ordinal);
        Assert.Contains("Selected character: B", ImagePrompt(withB), StringComparison.Ordinal);
        Assert.Equal(1, ImagePrompt(withB).Split(ReferenceHeader, StringSplitOptions.None).Length - 1);
        Assert.Equal("Keep title", withB.RootElement.GetProperty("video_title").GetString());
        Assert.Equal("Keep objective", withB.RootElement.GetProperty("objective").GetString());
        Assert.Equal("Keep motion", withB.RootElement.GetProperty("scenes")[0].GetProperty("motion_prompt").GetString());
        Assert.Equal(8, withB.RootElement.GetProperty("scenes")[0].GetProperty("duration_seconds").GetInt32());
    }

    [Fact]
    public void ExistingNoReferencePromptCanBeSynchronizedToCharacter()
    {
        using var result = Synchronize("""{"character_reference_mode":"none","scenes":[{"main_character_present":true,"image_prompt":"Keep scene"}]}""", Character("A"));

        Assert.Equal("image_reference_required", result.RootElement.GetProperty("character_reference_mode").GetString());
        Assert.Contains("Selected character: A", ImagePrompt(result));
    }

    [Fact]
    public void ExistingCharacterPromptCanBeCleanedToNone()
    {
        using var withCharacter = Synchronize(Prompt("Keep scene", true), Character("A"));
        using var result = Synchronize(withCharacter.RootElement.GetRawText(), PromptAssistantCharacterReference.None);

        Assert.Equal("none", result.RootElement.GetProperty("character_reference_mode").GetString());
        Assert.Equal("Keep scene", ImagePrompt(result));
    }

    [Fact]
    public void NoneRemovesLegacyReferenceWrapperWithoutRemovingSceneContent()
    {
        const string prompt = """
            {"scenes":[{"main_character_present":true,"image_prompt":"Use the provided reference image as the authoritative identity source.\n\nSCENE:\nKeep this unrelated scene composition."}]}
            """;

        using var result = Synchronize(prompt, PromptAssistantCharacterReference.None);

        Assert.Equal("Keep this unrelated scene composition.", ImagePrompt(result));
        Assert.DoesNotContain("reference image", ImagePrompt(result), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SynchronizationPreservesSceneWithoutImagePrompt()
    {
        const string prompt = """{"scenes":[{"main_character_present":true,"motion_prompt":"Keep motion"}]}""";

        using var result = Synchronize(prompt, Character("A"));
        var scene = result.RootElement.GetProperty("scenes")[0];

        Assert.False(scene.TryGetProperty("image_prompt", out _));
        Assert.Equal("Keep motion", scene.GetProperty("motion_prompt").GetString());
    }

    [Fact]
    public void SettingsFallbackUsesLibraryMasterImageInsteadOfCharacterName()
    {
        var reference = PromptAssistantCharacterReferenceSynchronizer.FromSettings(new RVideoJobSettingsDto
        {
            CharacterMode = RVideoCharacterModes.Library,
            SelectedCharacterId = 12,
            CharacterSnapshotJson = """{"characterName":"cau be but chi","masterImageUrl":"https://media.example/library.png"}"""
        });

        Assert.True(reference.IsEnabled);
        Assert.Equal("https://media.example/library.png", reference.Image);
        Assert.Equal("cau be but chi", reference.Name);
    }

    [Fact]
    public void SharedReferenceSettingIsNotModifiedBySettingsConversion()
    {
        var settings = new RVideoJobSettingsDto
        {
            CharacterMode = RVideoCharacterModes.Upload,
            UseReferenceImageForAllScenes = true,
            CharacterSnapshotJson = """{"fileUrl":"https://media.example/shared.png"}"""
        };

        var reference = PromptAssistantCharacterReferenceSynchronizer.FromSettings(settings);

        Assert.True(settings.UseReferenceImageForAllScenes);
        Assert.Equal("https://media.example/shared.png", reference.Image);
    }

    private static PromptAssistantCharacterReference Character(string name)
        => new(RVideoCharacterModes.Library, $"https://media.example/{name}.png", name, "LIBRARY");

    private static string Prompt(string imagePrompt, bool mainCharacterPresent)
        => JsonSerializer.Serialize(new
        {
            character_reference_mode = "image_reference_required",
            character_reference_image = "https://media.example/stale.png",
            character_reference = new { name = "stale", image = "https://media.example/stale.png" },
            scenes = new[]
            {
                new
                {
                    main_character_present = mainCharacterPresent,
                    image_prompt = $"{ReferenceHeader}\n\nSelected character: stale\n\nSCENE:\n{imagePrompt}"
                }
            }
        });

    private static JsonDocument Synchronize(string prompt, PromptAssistantCharacterReference reference)
        => JsonDocument.Parse(PromptAssistantCharacterReferenceSynchronizer.Synchronize(prompt, reference));

    private static string ImagePrompt(JsonDocument document)
        => document.RootElement.GetProperty("scenes")[0].GetProperty("image_prompt").GetString()!;
}
