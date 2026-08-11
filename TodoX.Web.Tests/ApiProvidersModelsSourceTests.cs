using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace TodoX.Web.Tests;

public class ApiProvidersModelsSourceTests
{
    [Fact]
    public void ApiProvidersModelsControls_ReloadModelsWhenChanged()
    {
        var text = ReadStrictUtf8(Path.Combine("TodoX.Web", "Components", "Pages", "ApiProviders.razor"));

        Assert.Contains("private async Task ReloadModelsAsync()", text, StringComparison.Ordinal);
        Assert.Contains("ValueChanged=\"OnModelSearchChanged\"", text, StringComparison.Ordinal);
        Assert.Contains("ValueChanged=\"OnModelTypeFilterChanged\"", text, StringComparison.Ordinal);
        Assert.Contains("ValueChanged=\"OnModelsEnabledOnlyChanged\"", text, StringComparison.Ordinal);

        AssertHandlerReloads(text, "OnModelSearchChanged");
        AssertHandlerReloads(text, "OnModelTypeFilterChanged");
        AssertHandlerReloads(text, "OnModelsEnabledOnlyChanged");
    }

    private static void AssertHandlerReloads(string text, string handlerName)
    {
        var pattern = $@"private async Task {Regex.Escape(handlerName)}\([^)]*\)\s*\{{[^}}]*await ReloadModelsAsync\(\);";
        Assert.Matches(pattern, text);
    }

    private static string ReadStrictUtf8(string relativePath)
    {
        var file = Path.Combine(FindRepoRoot(), relativePath);
        Assert.True(File.Exists(file), $"Missing file: {file}");
        var text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
            .GetString(File.ReadAllBytes(file));
        Assert.DoesNotContain('\uFFFD', text);

        return text;
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
}
