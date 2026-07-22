using TodoX.Web.Services.AiProviders;
using TodoX.Web.Services.DanceSell;
using TodoX.Web.Services.Render;
using Xunit;

namespace TodoX.Web.Tests;

public class AiCoreRuntimeTask3Tests
{
    [Fact]
    public void AccountClaimSql_UsesSkipLockedAndActiveLeases()
    {
        Assert.Contains("FOR UPDATE OF a SKIP LOCKED", AiProviderAccountRepository.ClaimAccountSql);
        Assert.Contains("todox_ai_provider_account_lease", AiProviderAccountRepository.ClaimAccountSql);
        Assert.Contains("lease_status = 'active'", AiProviderAccountRepository.ClaimAccountSql);
        Assert.DoesNotContain("current_concurrency", AiProviderAccountRepository.ClaimAccountSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Redactor_RemovesKnownSecretShapes()
    {
        var redacted = AiSecretRedactor.Redact(
            """{"Authorization":"Bearer abc","api_key":"secret","nested":{"client_secret":"hidden","private_key":"pem"},"ok":true}""");

        Assert.NotNull(redacted);
        Assert.DoesNotContain("abc", redacted);
        Assert.DoesNotContain(":\"secret\"", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hidden", redacted);
        Assert.DoesNotContain("pem", redacted);
        Assert.Contains("[redacted]", redacted);
    }

    [Fact]
    public void KieRedactor_DelegatesToSharedSecretRedactor()
    {
        var redacted = KieJsonRedactor.Redact("""{"access_token":"token-value"}""");

        Assert.DoesNotContain("token-value", redacted);
        Assert.Contains("[redacted]", redacted);
    }

    [Fact]
    public void RuntimeSources_DoNotExposeLegacyTableNames()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var sources = string.Join('\n', new[]
        {
            File.ReadAllText(Path.Combine(root, "TodoX.Web", "Services", "DanceSell", "DanceSellAiOperations.cs")),
            File.ReadAllText(Path.Combine(root, "TodoX.Web", "Services", "Render", "RenderJobService.cs")),
            File.ReadAllText(Path.Combine(root, "TodoX.Web", "Services", "AiProviders", "AiImageBillingService.cs")),
            File.ReadAllText(Path.Combine(root, "TodoX.Web", "Services", "AiProviders", "AiImageBillingDashboardService.cs"))
        });

        Assert.DoesNotContain("todox_ai_feature_provider_route", sources);
        Assert.DoesNotContain("dance_sell_provider_operations", sources);
        Assert.DoesNotContain("todox_ai_operation_assets", sources);
        Assert.DoesNotContain("todox_ai_operation_billing_transactions", sources);
        Assert.DoesNotContain("ai_image_billing_records", sources);
        Assert.DoesNotContain("ai_image_provider_attempts", sources);
        Assert.DoesNotContain("render_job_snapshots", sources);
    }
}
