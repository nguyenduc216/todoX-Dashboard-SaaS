using System.Security.Cryptography;
using TodoX.EditVideo.Planning;
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
    public void Customer_skill_file_has_front_matter_and_never_the_steps()
    {
        var md = EditVideoSkillService.BuildCustomerMarkdown("todox-bds-nha-pho", "Nhà phố",
            "Dùng khi khách cần: video \"nhà phố\"\ngiới thiệu", 3, "Bản tối giản");

        Assert.StartsWith("---\nname: todox-bds-nha-pho\ndescription: \"Dùng khi khách cần: video \\\"nhà phố\\\" giới thiệu\"\n---\n", md);
        Assert.Contains("**v3 – Bản tối giản**", md);
        Assert.Contains("`todox_skill_start`", md);
        Assert.Contains("`skill: \"todox-bds-nha-pho\"`", md);
    }

    [Fact]
    public void Content_is_split_into_steps_at_level_two_headings()
    {
        var steps = EditVideoSkillService.SplitSteps("Chỉ dùng todox_.\r\n\r\n## Bước 1\r\nMở thư mục.\n### chi tiết\nx\n## Bước 2\nDựng.\n");

        Assert.Equal(2, steps.Count);
        Assert.StartsWith("Chỉ dùng todox_.", steps[0]);
        Assert.Contains("### chi tiết", steps[0]);
        Assert.Equal("## Bước 2\nDựng.", steps[1]);
        Assert.Equal(["một khối"], EditVideoSkillService.SplitSteps("một khối"));
        Assert.Contains("bước 2/2", EditVideoSkillService.BuildStep("Nhà phố", 3, "Gọn", steps, 1));
    }

    [Fact]
    public void Preview_shows_the_machine_file_the_steps_and_the_fixed_settings()
    {
        var preview = EditVideoSkillService.BuildPreview("todox-a", "A", "khi nào", "## Bước 1\nbí quyết", "{\"aspect\":\"16:9\",\"targetSeconds\":45}", 2, "x");

        Assert.Contains("khung 16:9 · 45 giây", preview);
        Assert.Contains("bí quyết", preview);
        Assert.DoesNotContain("bí quyết", EditVideoSkillService.BuildCustomerMarkdown("todox-a", "A", "khi nào", 2, "x"));
    }

    [Fact]
    public void Skill_settings_fill_only_what_the_customer_left_unset()
    {
        using var signer = new EditVideoLicenseSigner(ECDsa.Create(ECCurve.NamedCurves.nistP256));
        var plans = new EditVideoPlanService(signer);
        var settings = EditVideoSkillSettings.Parse("{\"aspect\":\"16:9\",\"targetSeconds\":12,\"captionStyle\":\"box\",\"transition\":\"slide\",\"bogus\":1}");
        ClipInput[] clips = [new("c0", 30, 1920, 1080, true, true)];

        var bySkill = plans.Create(new PlanRequest(clips), settings).Value!.Plan;
        Assert.Equal((1920, 1080), (bySkill.Width, bySkill.Height));
        Assert.Equal(12, bySkill.TotalSeconds, 1);
        Assert.Equal(("box", "slide"), (bySkill.Style.CaptionStyle, bySkill.Style.Transition));

        var byCustomer = plans.Create(new PlanRequest(clips, Aspect: "9:16"), settings).Value!.Plan;
        Assert.Equal((1080, 1920), (byCustomer.Width, byCustomer.Height));
        Assert.Equal("{}", EditVideoSkillSettings.Parse("not json").ToJson());
    }

    [Fact]
    public void Download_keeps_latest_markdown_for_older_tools()
    {
        var file = new EditVideoSkillFile("todox-a", "A", null, 2,
        [
            new EditVideoSkillVersionFile(1, "a", null, DateTimeOffset.UnixEpoch, "one"),
            new EditVideoSkillVersionFile(2, "b", null, DateTimeOffset.UnixEpoch, "two")
        ]);

        Assert.Equal(2, file.Version);
        Assert.Equal("two", file.Markdown);
    }
}
