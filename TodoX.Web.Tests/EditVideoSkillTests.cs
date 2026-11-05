using TodoX.Web.Services.EditVideoTool;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class EditVideoSkillTests
{
    [Theory]
    [InlineData("TodoX BĐS vẽ bản đồ", "todox-bds-ve-ban-do")]
    [InlineData("Bất động sản – giới thiệu nhà phố", "todox-bat-dong-san-gioi-thieu-nha-pho")]
    [InlineData("todox", "todox")]
    public void Suggested_slug_is_plain_and_prefixed(string title, string expected) =>
        Assert.Equal(expected, EditVideoSkillService.SuggestSlug(title));

    [Theory]
    [InlineData("todox-bds-ve-ban-do", true)]
    [InlineData("todox-video", true)]
    [InlineData("bds-ve-ban-do", false)]
    [InlineData("todox-", false)]
    [InlineData("todox-Bad", false)]
    [InlineData("todox-../x", false)]
    public void Slug_must_start_with_todox_and_be_folder_safe(string slug, bool valid) =>
        Assert.Equal(valid, EditVideoSkillService.IsValidSlug(slug));

    [Fact]
    public void Skill_file_has_front_matter_with_a_quoted_one_line_description()
    {
        var md = EditVideoSkillService.BuildMarkdown(new EditVideoSkillRow
        {
            Slug = "todox-bds-ve-ban-do",
            Title = "TodoX BĐS – vẽ bản đồ",
            Description = "Dùng khi khách cần: bản đồ vị trí \"dự án\"\nvideo bất động sản",
            Content = "1. Bước một\r\n2. Bước hai"
        });

        Assert.StartsWith("---\nname: todox-bds-ve-ban-do\ndescription: \"Dùng khi khách cần: bản đồ vị trí \\\"dự án\\\" video bất động sản\"\n---\n", md);
        Assert.Contains("# TodoX BĐS – vẽ bản đồ\n\n1. Bước một\n2. Bước hai\n", md);
    }
}
