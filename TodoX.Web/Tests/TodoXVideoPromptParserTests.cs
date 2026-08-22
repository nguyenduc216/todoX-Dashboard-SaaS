using TodoX.Web.Services.VideoRender;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class TodoXVideoPromptParserTests
{
    [Fact]
    public void ParsesCurrentTodoXSchemaAndIgnoresUnknownFields()
    {
        var result = new TodoXVideoPromptParser().Parse("""
        {
          "_doc": { "version": "2" },
          "meta": {
            "total_duration_seconds": 24,
            "product_name": "TodoX Demo",
            "kieu_kich_ban": "Product showcase",
            "style": "clean",
            "cta": "Try TodoX"
          },
          "scenes": [
            {
              "scene": 1,
              "duration_seconds": 8,
              "image_prompt": "A bright product desk",
              "video_prompt": "Camera pushes in",
              "motion_prompt": "Camera pushes in",
              "voice": "Welcome"
            },
            {
              "scene": 2,
              "duration_seconds": 8,
              "image_prompt": "A user working",
              "video_prompt": "User moves naturally",
              "motion_prompt": "User moves naturally",
              "voice": "Work faster"
            },
            {
              "scene": 3,
              "duration_seconds": 8,
              "image_prompt": "A finished result",
              "video_prompt": "Logo settles",
              "motion_prompt": "Logo settles",
              "voice": "Start today"
            }
          ],
          "qc": { "approved": true },
          "motion_beats": [],
          "image_prompt_fallback": "ignored",
          "tts_rate": 1.0
        }
        """);

        Assert.True(result.IsJsonValid);
        Assert.True(result.IsTodoXPrompt);
        Assert.True(result.IsTodoXSchemaValid);
        Assert.Equal(3, result.Summary.SceneCount);
        Assert.Equal(24, result.Summary.SceneDurationTotal);
        Assert.Equal(24, result.Summary.DeclaredDurationSeconds);
        Assert.Equal("TodoX Demo", result.Summary.VideoTitle);
        Assert.Equal("Camera pushes in", result.Model.Scenes[0].MotionPrompt);
        Assert.Contains("\"qc\"", result.RawText);
    }

    [Fact]
    public void UsesVideoAndVoiceAliasesWithoutMakingJsonInvalid()
    {
        var result = new TodoXVideoPromptParser().Parse("""
        {
          "scenes": [
            {
              "scene": 1,
              "duration_seconds": 8,
              "image_prompt": "real image",
              "video_prompt": "move camera",
              "voice_text": "narration"
            }
          ]
        }
        """);

        Assert.True(result.IsJsonValid);
        Assert.True(result.IsTodoXSchemaValid);
        Assert.Equal("move camera", result.Model.Scenes[0].MotionPrompt);
        Assert.Equal("narration", result.Model.Scenes[0].Voice);
    }

    [Fact]
    public void UsesImageFallbackAndTtsAliases()
    {
        var result = new TodoXVideoPromptParser().Parse("""
        {
          "aspect_ratio": "9:16",
          "resolution": "720p",
          "scenes": [
            {
              "scene": 1,
              "duration_seconds": 8,
              "image_prompt": "PLACEHOLDER",
              "image_prompt_fallback": "A real product image",
              "video_prompt": "slow pan",
              "tts_text": "Fallback narration",
              "ttsRate": 1.1
            }
          ]
        }
        """);

        var scene = result.Model.Scenes[0];
        Assert.True(result.IsJsonValid);
        Assert.True(result.IsTodoXSchemaValid);
        Assert.Equal("A real product image", scene.EffectiveImagePrompt);
        Assert.Equal("slow pan", scene.MotionPrompt);
        Assert.Equal("Fallback narration", scene.Voice);
        Assert.Equal(1.1m, scene.TtsRate);
        Assert.Equal("9:16", result.Model.AspectRatio);
        Assert.Equal("720p", result.Model.Resolution);
    }

    [Theory]
    [InlineData("dialogue", "Narration A")]
    [InlineData("dialogue_text", "Narration B")]
    [InlineData("narration", "Narration C")]
    [InlineData("tts_text", "Narration D")]
    public void ScenePromptMetadata_NormalizesVoiceAliases(string key, string expectedVoice)
    {
        var metadata = ScenePromptMetadata.Parse($$"""
        {
          "{{key}}": "{{expectedVoice}}",
          "voice_instruction": "Soft tone"
        }
        """);

        Assert.Equal(expectedVoice, metadata.Voice);
        Assert.Equal("Soft tone", metadata.VoiceInstruction);
        Assert.Contains("\"voice\":", metadata.Serialize(), StringComparison.Ordinal);
    }

    [Fact]
    public void PreservesEffectiveImagePromptThroughSceneMetadataRoundTrip()
    {
        var parser = new TodoXVideoPromptParser();
        var result = parser.Parse("""
        {
          "scenes": [
            {
              "scene": 1,
              "duration_seconds": 8,
              "image_prompt": "PLACEHOLDER",
              "image_prompt_fallback": "A real product image",
              "motion_prompt": "slow pan"
            }
          ]
        }
        """);

        var scene = result.Model.Scenes[0];
        var metadata = new ScenePromptMetadata
        {
            ScenePurpose = scene.ScenePurpose,
            ImagePrompt = scene.ImagePrompt,
            MotionPrompt = scene.MotionPrompt,
            EffectiveImagePrompt = scene.EffectiveImagePrompt,
            RawSceneJson = scene.RawJson
        }.Serialize();

        var parsed = ScenePromptMetadata.Parse(metadata);

        Assert.Equal("A real product image", parsed.EffectiveImagePrompt);
        Assert.Equal("PLACEHOLDER", parsed.ImagePrompt);
        Assert.Contains("\"effective_image_prompt\"", metadata, StringComparison.Ordinal);
    }

    [Fact]
    public void EditedImagePromptReplacesStaleEffectiveImagePromptThroughRoundTrip()
    {
        var edited = new ScenePromptMetadata
        {
            ImagePrompt = "new prompt",
            EffectiveImagePrompt = ScenePromptMetadata.NormalizeEditedEffectiveImagePrompt("new prompt", "old prompt", "old prompt"),
            MotionPrompt = "slow pan"
        };

        var reloaded = ScenePromptMetadata.Parse(edited.Serialize());

        Assert.Equal("new prompt", reloaded.ImagePrompt);
        Assert.Equal("new prompt", reloaded.EffectiveImagePrompt);
    }

    [Fact]
    public void PlaceholderImagePromptWithoutFallbackIsBlocked()
    {
        var effective = ScenePromptMetadata.NormalizeEffectiveImagePrompt(
            "[[THAY BẰNG ẢNH THỰC TẾ]]",
            null);

        Assert.Null(effective);
        Assert.False(ScenePromptMetadata.IsUsableImagePrompt(effective));
    }

    [Fact]
    public void PlaceholderEditDoesNotReuseStaleEffectiveImagePrompt()
    {
        var effective = ScenePromptMetadata.NormalizeEditedEffectiveImagePrompt(
            "[[THAY BẰNG ẢNH THỰC TẾ]]",
            "old prompt",
            "old prompt");

        Assert.Null(effective);
        Assert.False(ScenePromptMetadata.IsUsableImagePrompt(effective));
    }

    [Fact]
    public void PlaceholderProducesWarningInsteadOfInvalidJson()
    {
        var result = new TodoXVideoPromptParser().Parse("""
        {
          "scenes": [
            {
              "scene": 1,
              "duration_seconds": 8,
              "image_prompt": "[[THAY BẰNG ẢNH THỰC TẾ]]",
              "motion_prompt": "slow pan"
            }
          ]
        }
        """);

        Assert.True(result.IsJsonValid);
        Assert.False(result.IsTodoXSchemaValid);
        Assert.Contains(result.Warnings, warning =>
            warning.Contains("SCENE_IMAGE_SOURCE_UNRESOLVED", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidJsonWithMissingMetadataIsWarningNotSyntaxError()
    {
        var result = new TodoXVideoPromptParser().Parse("""{"scenes": []}""");

        Assert.True(result.IsJsonValid);
        Assert.False(result.IsTodoXSchemaValid);
        Assert.Null(result.ErrorMessage);
        Assert.Contains(result.Warnings, warning => warning.Contains("Metadata thiếu", StringComparison.Ordinal));
    }
    [Fact]
    public void PreservesBomAndUnknownSceneFields()
    {
        var source = "\uFEFF" + """
        {
          "scenes": [
            {
              "scene_index": 2,
              "duration_seconds": 6,
              "image_prompt": "A real scene",
              "motion_prompt": "Slow camera move",
              "tts_rate": "0.85",
              "knowledge_beat": "Keep this field"
            }
          ]
        }
        """;

        var result = new TodoXVideoPromptParser().Parse(source);
        var scene = result.Model.Scenes[0];

        Assert.True(result.IsJsonValid);
        Assert.True(result.IsTodoXSchemaValid);
        Assert.Equal(2, scene.Scene);
        Assert.Equal(0.85m, scene.TtsRate);
        Assert.Contains("knowledge_beat", scene.RawJson, StringComparison.Ordinal);

        var metadata = ScenePromptMetadata.FromScene(new TodoX.Web.Models.VideoProjectSceneDto
        {
            ScenePrompt = new ScenePromptMetadata
            {
                ImagePrompt = scene.ImagePrompt,
                MotionPrompt = scene.MotionPrompt,
                TtsRate = scene.TtsRate,
                RawSceneJson = scene.RawJson
            }.Serialize()
        });

        Assert.Contains("knowledge_beat", metadata.RawSceneJson, StringComparison.Ordinal);
        Assert.Equal(0.85m, metadata.TtsRate);
    }

    [Fact]
    public void PreservesRawMetadataWhenEditingVoiceAndSavingScene()
    {
        var result = new TodoXVideoPromptParser().Parse("""
        {
          "scenes": [
            {
              "scene": 1,
              "duration_seconds": 8,
              "image_prompt": "A real scene",
              "image_prompt_fallback": "Fallback image",
              "motion_prompt": "Slow camera move",
              "voice": "Original voice",
              "voice_instruction": "Soft tone",
              "tts_rate": 0.85,
              "knowledge_beat": "Keep this field",
              "motion_beats": ["beat-a"],
              "crop_factor": 1.2,
              "custom_unknown_field": "custom"
            }
          ]
        }
        """);

        var scene = result.Model.Scenes[0];
        var draft = new ScenePromptMetadata
        {
            ScenePurpose = scene.ScenePurpose,
            ImagePrompt = scene.ImagePrompt,
            MotionPrompt = scene.MotionPrompt,
            Voice = "Updated voice",
            VoiceInstruction = scene.VoiceInstruction,
            TtsRate = scene.TtsRate,
            RawSceneJson = scene.RawJson,
            EffectiveImagePrompt = scene.EffectiveImagePrompt
        };
        foreach (var item in ScenePromptMetadata.Parse(scene.RawJson).Extra)
        {
            draft.Extra[item.Key] = item.Value;
        }

        var serialized = draft.Serialize();
        var parsed = ScenePromptMetadata.Parse(serialized);

        Assert.Equal("Updated voice", parsed.Voice);
        Assert.Equal("A real scene", parsed.ImagePrompt);
        Assert.Equal("A real scene", parsed.EffectiveImagePrompt);
        Assert.Equal(scene.RawJson, parsed.RawSceneJson);
        Assert.Equal("Keep this field", parsed.Extra["knowledge_beat"]);
        Assert.Equal("[\"beat-a\"]", parsed.Extra["motion_beats"]);
        Assert.Equal("1.2", parsed.Extra["crop_factor"]);
        Assert.Equal("custom", parsed.Extra["custom_unknown_field"]);
    }
}
