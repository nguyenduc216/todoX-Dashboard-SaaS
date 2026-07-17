using System.Text;
using Xunit;

namespace TodoX.Web.Tests;

public class AiProviderQuickDefaultsSourceTests
{
    [Fact]
    public void AiProvidersPage_UsesBatchDefaultSaveAndCapabilityBackedDropdowns()
    {
        var text = ReadStrictUtf8(Path.Combine("TodoX.Web", "Components", "Pages", "AiProviders.razor"));

        Assert.Contains("C\u00E0i \u0111\u1EB7t nhanh Provider m\u1EB7c \u0111\u1ECBnh", text, StringComparison.Ordinal);
        Assert.Contains("AiFeatureProviderCatalog.QuickDefaultFeatures", text, StringComparison.Ordinal);
        Assert.Contains("GetSelectableProvidersAsync(feature.CapabilityCode)", text, StringComparison.Ordinal);
        Assert.Contains("SetDefaultCapabilitiesAsync(selectedIds", text, StringComparison.Ordinal);
        Assert.Contains("ProviderCapabilityId", text, StringComparison.Ordinal);
        Assert.DoesNotContain("nano-banana", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("grok-video", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AvatarBuilderRuntime_UsesChibiCapabilityInsteadOfAvatarCapability()
    {
        var page = ReadStrictUtf8(Path.Combine("TodoX.Web", "Components", "Pages", "AvatarBuilder.razor"));
        var service = ReadStrictUtf8(Path.Combine("TodoX.Web", "Services", "AvatarTemplates", "AvatarTemplateService.cs"));

        Assert.Contains("GetSelectableProvidersAsync(AiProviderCatalog.ChibiAvatarGeneration)", page, StringComparison.Ordinal);
        Assert.Contains("ResolveProviderForCapabilityAsync(AiProviderCatalog.ChibiAvatarGeneration", service, StringComparison.Ordinal);
        Assert.Contains("CapabilityCode = AiProviderCatalog.ChibiAvatarGeneration", service, StringComparison.Ordinal);
        Assert.DoesNotContain("GetSelectableProvidersAsync(\"avatar_generation\")", page, StringComparison.Ordinal);
        Assert.DoesNotContain("ResolveProviderForCapabilityAsync(\"avatar_generation\"", service, StringComparison.Ordinal);
    }

    [Fact]
    public void AiProviderBatchDefaultRepository_UsesOneTransactionAndValidatesSelections()
    {
        var repository = ReadStrictUtf8(Path.Combine("TodoX.Web", "Services", "AiProviders", "AiProviderRepository.cs"));
        var service = ReadStrictUtf8(Path.Combine("TodoX.Web", "Services", "AiProviders", "AiProviderService.cs"));

        Assert.Contains("SetDefaultCapabilitiesAsync", service, StringComparison.Ordinal);
        Assert.Contains("public async Task SetDefaultCapabilitiesAsync", repository, StringComparison.Ordinal);
        Assert.Contains("using var tx = conn.BeginTransaction();", repository, StringComparison.Ordinal);
        Assert.Contains("ProviderEnabled", repository, StringComparison.Ordinal);
        Assert.Contains("GroupBy(x => x.CapabilityCode", repository, StringComparison.Ordinal);
        Assert.Contains("is_default = false", repository, StringComparison.Ordinal);
        Assert.Contains("is_default = true", repository, StringComparison.Ordinal);
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
