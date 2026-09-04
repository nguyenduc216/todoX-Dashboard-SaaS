using TodoX.Web.Services.DanceSell;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class DanceSellRepositoryTests
{
    [Fact]
    public void CreateDraftAsyncSql_WritesOrientationButNotGeneratedCharacterOrientation()
    {
        var source = ReadRepositorySource();
        var section = GetMethodSection(source, "CreateDraftAsync");

        Assert.DoesNotContain("mode, character_orientation,", section, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("mode, orientation", section, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@logicalRequestId", section, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@orientation", section, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("character_orientation AS CharacterOrientation", section, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateAsyncSql_WritesOrientationButNotGeneratedCharacterOrientation()
    {
        var source = ReadRepositorySource();
        var section = GetMethodSection(source, "CreateAsync");

        Assert.DoesNotContain("mode, character_orientation,", section, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("mode, orientation", section, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@orientation", section, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("character_orientation AS CharacterOrientation", section, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UpdateBusinessAsyncSql_UpdatesOrientationButNotGeneratedCharacterOrientation()
    {
        var source = ReadRepositorySource();
        var section = GetMethodSection(source, "UpdateBusinessAsync");

        Assert.DoesNotContain("character_orientation=@orientation", section, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("orientation=@orientation", section, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("motion_provider_code=COALESCE(@motionProviderCode, motion_provider_code)", section, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("motion_provider_model=COALESCE(@motionProviderModel, motion_provider_model)", section, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MotionStagingSql_PreservesDraftContinuationFields()
    {
        var source = ReadRepositorySource();
        var createDraft = GetMethodSection(source, "CreateDraftAsync");
        var updateMotion = GetMethodSection(source, "UpdateMotionAsync");

        Assert.Contains("logical_request_id", createDraft, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("orientation", createDraft, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("motion_source_type", updateMotion, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("motion_video_media_id", updateMotion, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("motion_video_url", updateMotion, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("request_json=jsonb_set(COALESCE(request_json, '{}'::jsonb), '{durationSeconds}'", updateMotion, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("source_stage_status='ready'", updateMotion, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TikTokStagingSql_KeepsOriginalSourceUrlSeparateFromStagedMp4Url()
    {
        var source = ReadRepositorySource();
        var updateTikTok = GetMethodSection(source, "UpdateMotionTikTokAsync");
        var updateMotion = GetMethodSection(source, "UpdateMotionAsync");

        Assert.Contains("UpdateMotionAsync(id, DanceSellMotionSourceTypes.TikTok, sourceUrl", updateTikTok, StringComparison.Ordinal);
        Assert.Contains("motion_source_url=@sourceUrl", updateMotion, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("motion_video_url=@publicUrl", updateMotion, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PersistMotionDurationSql_MergesDurationWithoutDroppingOtherRequestFields()
    {
        var source = ReadRepositorySource();
        var section = GetMethodSection(source, "PersistMotionDurationAsync");

        Assert.Contains("jsonb_set(COALESCE(request_json, '{}'::jsonb), '{durationSeconds}'", section, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("updated_at=now()", section, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("autoFinish", section, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResetReferenceSql_InvalidatesPreviouslyApprovedReferenceWhenSourceImageChanges()
    {
        var source = ReadRepositorySource();
        var section = GetMethodSection(source, "ResetReferenceAsync");

        Assert.Contains("prepared_reference_status=@status", section, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("prepared_reference_media_id=NULL", section, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("prepared_reference_url=NULL", section, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("prepared_reference_approved_at=NULL", section, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reference_approved_at=NULL", section, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UPDATE dance_sell.dance_sell_reference_versions", section, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SET is_selected = false", section, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM dance_sell.dance_sell_reference_versions", section, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RemoveProductSql_AtomicallyPromotesCharacterToApprovedReference()
    {
        var source = ReadRepositorySource();
        var section = GetMethodSection(source, "RemoveProductAndUseCharacterReferenceAsync");

        Assert.Contains("product_media_id=NULL", section, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("product_object_key=NULL", section, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("product_image_url=NULL", section, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("prepared_reference_media_id=character_media_id", section, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("prepared_reference_object_key=character_object_key", section, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("prepared_reference_url=character_image_url", section, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("prepared_reference_status=CASE", section, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("THEN 'approved'", section, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SET is_selected=false", section, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnapproveReferenceSql_PreservesHistoryAndClearsApprovalState()
    {
        var source = ReadRepositorySource();
        var section = GetMethodSection(source, "UnapproveReferenceAsync");

        Assert.Contains("UPDATE dance_sell.dance_sell_reference_versions", section, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SET is_selected = false", section, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("prepared_reference_status='ready'", section, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("prepared_reference_approved_at=NULL", section, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reference_approved_at=NULL", section, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("prepared_reference_status='approved'", section, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM dance_sell.dance_sell_reference_versions", section, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UpdateCompletedAsyncSql_DoesNotReferenceMissingStatusParameter()
    {
        Assert.DoesNotContain("@status", DanceSellRepository.UpdateCompletedSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UpdateCompletedAsyncSql_PreservesExistingCompletedAt()
    {
        Assert.Contains("completed_at=COALESCE(completed_at, now())", DanceSellRepository.UpdateCompletedSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UpdateCompletedAsyncSql_PersistsWritableResultUrlWithoutGeneratedColumnWrite()
    {
        Assert.Contains("result_url=@resultVideoUrl", DanceSellRepository.UpdateCompletedSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("result_video_url=", DanceSellRepository.UpdateCompletedSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UpdateCallbackSql_PersistsResultUrlWithoutWritingGeneratedColumn()
    {
        var source = ReadRepositorySource();
        var section = GetMethodSection(source, "UpdateCallbackAsync");

        Assert.Contains("result_url=COALESCE(NULLIF(@resultVideoUrl, ''), result_url)", section, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("result_video_url=", section, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadRepositorySource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "TodoX.Web", "Services", "DanceSell", "DanceSellRepository.cs");
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate DanceSellRepository.cs from the test output directory.");
    }

    private static string GetMethodSection(string source, string methodName)
    {
        var start = source.IndexOf($"public async Task {methodName}(", StringComparison.Ordinal);
        if (start < 0)
        {
            start = source.IndexOf($"public async Task<DanceSellJobDto> {methodName}(", StringComparison.Ordinal);
        }

        if (start < 0)
        {
            start = source.IndexOf($"private async Task {methodName}(", StringComparison.Ordinal);
        }

        Assert.True(start >= 0, $"Could not locate {methodName}.");

        var nextMethod = source.IndexOf("\n    public ", start + methodName.Length, StringComparison.Ordinal);
        if (nextMethod < 0)
        {
            nextMethod = source.IndexOf("\n    private ", start + methodName.Length, StringComparison.Ordinal);
        }

        return nextMethod > start
            ? source[start..nextMethod]
            : source[start..];
    }
}
