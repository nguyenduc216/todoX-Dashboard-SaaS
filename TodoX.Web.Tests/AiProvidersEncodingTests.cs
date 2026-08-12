using System.Text;
using Xunit;

namespace TodoX.Web.Tests;

public class AiProvidersEncodingTests
{
    [Fact]
    public void AiProvidersPage_IsStrictUtf8_AndContainsExpectedVietnameseText()
    {
        var text = ReadStrictUtf8(Path.Combine("TodoX.Web", "Components", "Pages", "AiProviders.razor"));

        Assert.DoesNotContain('\uFFFD', text);

        foreach (var bad in BadMojibakeFragments)
        {
            Assert.DoesNotContain(bad, text, StringComparison.Ordinal);
        }

        foreach (var expected in ExpectedVietnameseFragments)
        {
            Assert.Contains(expected, text, StringComparison.Ordinal);
        }
    }

    private static string ReadStrictUtf8(string relativePath)
    {
        var file = Path.Combine(FindRepoRoot(), relativePath);
        Assert.True(File.Exists(file), $"Missing file: {file}");

        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
            .GetString(File.ReadAllBytes(file));
    }

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

    private static readonly string[] BadMojibakeFragments =
    [
        "Ã",
        "Âº",
        "Â¡",
        "Ä‘",
        "â€"
    ];

    private static readonly string[] ExpectedVietnameseFragments =
    [
        "Quản lý",
        "Cài đặt",
        "Trạng thái",
        "Đồng bộ",
        "Giá vốn Provider",
        "MẶC ĐỊNH",
        "NÂNG CAO"
    ];
}
