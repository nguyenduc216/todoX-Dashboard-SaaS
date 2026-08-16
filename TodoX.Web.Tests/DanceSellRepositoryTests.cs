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
        Assert.Contains("source_stage_status='ready'", updateMotion, StringComparison.OrdinalIgnoreCase);
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
