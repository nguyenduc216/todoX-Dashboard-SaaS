using System.Text;
using Xunit;

namespace TodoX.Web.Tests;

public class AdminAvatarManagerEncodingTests
{
    [Fact]
    public void AdminAvatarManagerScope_IsStrictUtf8_AndVietnameseTextIsNotMojibake()
    {
        var repoRoot = FindRepoRoot();
        foreach (var relativePath in AvatarManagerScopeFiles)
        {
            var file = Path.Combine(repoRoot, relativePath);
            Assert.True(File.Exists(file), $"Missing file: {file}");

            var text = ReadStrictUtf8(file);
            Assert.DoesNotContain('\uFFFD', text);

            foreach (var bad in BadMojibakeFragments)
            {
                Assert.DoesNotContain(bad, text, StringComparison.Ordinal);
            }
        }

        var pageText = ReadStrictUtf8(Path.Combine(repoRoot, "TodoX.Web", "Components", "Pages", "AdminAvatarManager.razor"));
        foreach (var expected in ExpectedAdminAvatarText)
        {
            Assert.Contains(expected, pageText, StringComparison.Ordinal);
        }
    }

    private static string ReadStrictUtf8(string file)
        => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
            .GetString(File.ReadAllBytes(file));

    private static readonly string[] AvatarManagerScopeFiles =
    [
        Path.Combine("TodoX.Web", "Components", "Pages", "AdminAvatarManager.razor"),
        Path.Combine("TodoX.Web", "Components", "Avatar", "AvatarEditorForm.razor"),
        Path.Combine("TodoX.Web", "Components", "Avatar", "PublicAvatarPromptEditDialog.razor"),
        Path.Combine("TodoX.Web", "Services", "AvatarTemplates", "AvatarTemplateModels.cs"),
        Path.Combine("TodoX.Web", "Services", "AvatarTemplates", "AvatarTemplateService.cs")
    ];

    private static readonly string[] ExpectedAdminAvatarText =
    [
        "Qu\u1EA3n l\u00FD avatar m\u1EABu",
        "Danh s\u00E1ch avatar",
        "Th\u00F4ng tin avatar",
        "\u1EA2nh k\u1EBFt qu\u1EA3",
        "Nh\u1EADt k\u00FD render",
        "Prompt \u0111\u00E3 d\u00F9ng"
    ];

    private static readonly string[] BadMojibakeFragments =
    [
        "Qu?n",
        "m?u",
        "Danh s\u00EF\u00BF\u00BDch",
        "C\u00EF\u00BF\u00BDng khai",
        "\u00EF\u00BF\u00BDang",
        "Chua c?",
        "?nh k?t qu?",
        "Nh?t k\u00EF\u00BF\u00BD",
        "Qu\u00E1\u00BA\u00A3n",
        "m\u00C3\u00A3u",
        "m\u00E1\u00BA\u00ABu",
        "\u00C3\u00B4",
        "\u00C4\u2018",
        "\u00C6\u00B0"
    ];

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "TodoX.Dashboard.sln"))
                && Directory.Exists(Path.Combine(dir.FullName, "TodoX.Web")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate todoX-Dashboard-SaaS repo root.");
    }
}
