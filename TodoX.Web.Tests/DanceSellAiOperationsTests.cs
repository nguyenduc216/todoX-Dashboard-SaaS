using Microsoft.Extensions.Configuration;
using TodoX.Web.Services.AiProviders.Kie;
using TodoX.Web.Services.DanceSell;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class DanceSellAiOperationsTests
{
    [Fact]
    public void ReferenceModes_DefineDirectAndGeneratedFlows()
    {
        Assert.Contains(DanceSellReferenceModes.DirectReference, DanceSellReferenceModes.All);
        Assert.Contains(DanceSellReferenceModes.GenerateReference, DanceSellReferenceModes.All);
        Assert.Equal(DanceSellReferenceModes.GenerateReference, new DanceSellCreateJobRequest().ReferenceMode);
    }

    [Fact]
    public void DirectReferenceJsonRequest_DoesNotRequireCharacterOrProductProviderSecret()
    {
        var properties = typeof(DanceSellJsonBusinessRequest)
            .GetProperties()
            .Select(x => x.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("DirectReferenceMediaId", properties);
        Assert.Contains("ReferenceMode", properties);
        Assert.Contains("MotionProviderCode", properties);
        Assert.DoesNotContain("ApiKey", properties);
        Assert.DoesNotContain("CredentialConfigName", properties);
    }

    [Fact]
    public void KieParser_ReadsCreditsConsumedAndUsageMetadata()
    {
        var raw = """
        {
          "code": 200,
          "data": {
            "taskId": "task-credits",
            "model": "kling-2.6/motion-control",
            "state": "success",
            "resultJson": "{\"resultUrls\":[\"https://cdn.example/result.mp4\"]}",
            "costTime": 12.5,
            "completeTime": 1735689600000,
            "createTime": 1735689500000,
            "updateTime": 1735689600000,
            "progress": 100,
            "creditsConsumed": 154
          }
        }
        """;

        var parsed = KieResponseParser.ParseTaskDetail(raw, 200);

        Assert.Equal("task-credits", parsed.TaskId);
        Assert.Equal("kling-2.6/motion-control", parsed.Model);
        Assert.Equal(154, parsed.CreditsConsumed);
        Assert.Equal(100, parsed.Progress);
        Assert.Single(parsed.ResultUrls);
    }

    [Fact]
    public void KieCallback_ReadsCreditsConsumed()
    {
        var raw = """
        {
          "taskId": "callback-task",
          "state": "success",
          "resultJson": "{\"resultUrls\":[\"https://cdn.example/result.mp4\"]}",
          "creditsConsumed": "252"
        }
        """;

        var parsed = KieResponseParser.ParseCallback(raw);

        Assert.Equal("callback-task", parsed.TaskId);
        Assert.Equal(252, parsed.CreditsConsumed);
    }

    [Fact]
    public void OperationSqlFolder_ContainsRequiredScriptsAndDefaultKieRoutes()
    {
        var root = FindRepoRoot();
        var folder = Path.Combine(root, "database/manual/ai-operation-logs");
        var expected = new[]
        {
            "01_create_provider_accounts.sql",
            "02_create_feature_provider_routes.sql",
            "03_create_provider_operations.sql",
            "04_create_operation_assets.sql",
            "05_create_balance_ledger.sql",
            "06_create_operation_billing.sql",
            "07_extend_dance_sell_jobs.sql",
            "08_seed_dance_sell_routes.sql",
            "09_verify_ai_operation_logs.sql",
            "README.md"
        };

        foreach (var file in expected)
        {
            Assert.True(File.Exists(Path.Combine(folder, file)), file);
        }

        var seed = File.ReadAllText(Path.Combine(folder, "08_seed_dance_sell_routes.sql"));
        Assert.Contains("gpt-image-2-image-to-image", seed);
        Assert.Contains("kling-2.6/motion-control", seed);
        Assert.Contains("'dance_sell'", seed);
        Assert.Contains("'reference_image'", seed);
        Assert.Contains("'motion_video'", seed);
    }

    [Fact]
    public async Task BillingDisabled_DoesNotChargeRealPoints()
    {
        var billing = new AiOperationBillingService(new ConfigurationBuilder().Build());
        var operation = await billing.ChargeAsync(new DanceSellProviderOperationDto
        {
            TodoxPointsEstimated = 10,
            BillingStatus = DanceSellBillingStatuses.Estimated
        });

        Assert.Equal(DanceSellBillingStatuses.NotRequired, operation.BillingStatus);
        Assert.Null(operation.TodoxPointsCharged);
    }

    [Fact]
    public void Redactor_RemovesSecretsFromTechnicalJson()
    {
        var redacted = KieJsonRedactor.Redact("""{"Authorization":"Bearer abc","nested":{"api_key":"secret"},"ok":true}""");

        Assert.DoesNotContain("Bearer abc", redacted);
        Assert.DoesNotContain("secret", redacted);
        Assert.Contains("[redacted]", redacted);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "database")) && Directory.Exists(Path.Combine(dir.FullName, "TodoX.Web")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
