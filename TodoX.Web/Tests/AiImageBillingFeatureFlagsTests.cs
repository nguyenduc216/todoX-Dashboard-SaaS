using Microsoft.Extensions.Configuration;
using TodoX.Web.Services.AiProviders;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class AiImageBillingFeatureFlagsTests
{
    [Fact]
    public void IsReconciliationWorkerEnabled_ReturnsFalse_WhenBillingSchemaFlagMissing()
    {
        var configuration = new ConfigurationBuilder().Build();

        var enabled = AiImageBillingFeatureFlags.IsReconciliationWorkerEnabled(configuration);

        Assert.False(enabled);
    }

    [Fact]
    public void IsReconciliationWorkerEnabled_ReturnsFalse_WhenBillingSchemaFlagDisabled()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AiImageBilling:HasBillingSchema"] = "false"
            })
            .Build();

        var enabled = AiImageBillingFeatureFlags.IsReconciliationWorkerEnabled(configuration);

        Assert.False(enabled);
    }

    [Fact]
    public void IsReconciliationWorkerEnabled_ReturnsTrue_WhenBillingSchemaFlagEnabled()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AiImageBilling:HasBillingSchema"] = "true"
            })
            .Build();

        var enabled = AiImageBillingFeatureFlags.IsReconciliationWorkerEnabled(configuration);

        Assert.True(enabled);
    }
}
