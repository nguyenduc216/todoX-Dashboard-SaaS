using System.Text;
using Xunit;

namespace TodoX.Web.Tests;

// The license header has a copy icon; the full key is only known right after it is issued (TodoX stores its hash).
public sealed class MyEditVideoCopyKeyTests
{
    [Fact]
    public void LicenseHeaderHasCopyIconThatIssuesAKeyWhenNoneIsKnown()
    {
        var page = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
            "Components", "Pages", "MyEditVideo.razor"), Encoding.UTF8);
        Assert.Contains("OnClick=\"@(() => CopyLicenseKeyAsync(license))\"", page);
        Assert.Contains("Icons.Material.Filled.ContentCopy", page);
        var copy = page[page.IndexOf("private async Task CopyLicenseKeyAsync", StringComparison.Ordinal)..];
        Assert.Contains("_newKeys.TryGetValue(license.Id, out var key)", copy);
        Assert.Contains("Cấp key mới và sao chép", copy);
    }
}
