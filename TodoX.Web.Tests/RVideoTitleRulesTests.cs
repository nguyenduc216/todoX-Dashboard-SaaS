using TodoX.Web.Services.VideoRender;
using Xunit;

namespace TodoX.Web.Tests;

/// <summary>"Jobs của tôi" must not list rows of "Video mới": a short title is taken from the video's content.</summary>
public sealed class RVideoTitleRulesTests
{
    private static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("Video mới", true)]
    [InlineData(" video mới ", true)]
    [InlineData("Prompt workspace", true)]
    [InlineData("{\"scenes\":[]}", true)]
    [InlineData("Video Tuyên tạo KOC", false)]
    public void Placeholder_titles_are_recognised(string? title, bool placeholder)
        => Assert.Equal(placeholder, RVideoTitleRules.IsPlaceholder(title));

    [Fact]
    public void The_script_title_comes_first_then_the_description_then_a_text_prompt()
    {
        Assert.Equal("Đạo lý người quê: thương cha mẹ",
            RVideoTitleRules.FromContent("{\"video_title\":\"Đạo lý người quê: thương cha mẹ\"}", "mô tả"));
        Assert.Equal("Áo nơ xinh Hot Trend",
            RVideoTitleRules.FromContent("{\"meta\":{\"product_name\":\"Áo nơ xinh Hot Trend\"}}", null));
        Assert.Equal("Review homestay Rừng lá kim Đà Lạt cho gia đình",
            RVideoTitleRules.FromContent("{\"scenes\":[]}", "Review homestay Rừng lá kim Đà Lạt cho gia đình. Có cảnh sáng sớm."));
        Assert.Equal("Người quê kể chuyện mùa gặt", RVideoTitleRules.FromContent("- Người quê kể chuyện mùa gặt\nCảnh 1...", null));
        Assert.Null(RVideoTitleRules.FromContent(null, "  "));
        Assert.Null(RVideoTitleRules.FromContent("{\"scenes\":[]}", null));
    }

    [Fact]
    public void Long_text_is_cut_at_a_word()
    {
        var title = RVideoTitleRules.Shorten("Chia sẻ chuyên gia tài chính về cách xây dựng nền tảng tài chính vững chắc cho gia đình trẻ ở thành phố lớn");
        Assert.NotNull(title);
        Assert.True(title!.Length <= RVideoTitleRules.MaxLength + 1);
        Assert.EndsWith("…", title);
        Assert.DoesNotContain("  ", title);
    }

    [Fact]
    public void Saving_content_titles_the_video_and_old_videos_are_fixed_by_script()
    {
        var service = File.ReadAllText(Path.Combine(Root, "TodoX.Web", "Services", "VideoRender", "RVideoJobService.cs"));
        Assert.Equal(2, service.Split("RVideoTitleRules.FillPlaceholderAsync(conn, _tenant.TenantId, jobId: jobId)").Length - 1);
        var assistant = File.ReadAllText(Path.Combine(Root, "TodoX.Web", "Services", "PromptAssistant", "ServicePromptAssistantRepository.cs"));
        Assert.Contains("RVideoTitleRules.FillPlaceholderAsync(conn, _tenant.TenantId, projectId: videoProjectId)", assistant);

        var sql = Path.Combine(Root, "database", "manual", "rvideo", "20261116_rvideo_titles_from_content.sql");
        Assert.True(File.Exists(sql));
        Assert.True(File.Exists(sql.Replace(".sql", ".test.sql")));
        var script = File.ReadAllText(sql);
        Assert.DoesNotContain("alter table", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("drop table", script, StringComparison.OrdinalIgnoreCase);
    }
}
