using System.Text;
using System.Text.RegularExpressions;
using TodoX.Web.Models;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class AiProviderContractTests
{
    private static readonly string[] ProductionUnitTypes =
    [
        "credits",
        "tokens",
        "token_1000",
        "request",
        "requests",
        "image",
        "images",
        "second",
        "seconds",
        "video_second",
        "video_seconds",
        "minute",
        "minutes",
        "scene",
        "character_1000",
        "usd",
        "fixed"
    ];

    private static readonly string[] LegacySyncChangeTypes =
    [
        "insert",
        "update",
        "status_change",
        "price_change",
        "disable",
        "enable",
        "no_change"
    ];

    private static readonly string[] CurrentSyncChangeTypes =
    [
        "MODEL_ADDED",
        "MODEL_UPDATED",
        "MODEL_STATUS_CHANGED",
        "MODE_ADDED",
        "DURATION_ADDED",
        "DURATION_REMOVED",
        "RESOLUTION_ADDED",
        "PRICE_ADDED",
        "PRICE_CHANGED",
        "PRICE_DISABLED"
    ];

    private static readonly string[] ServiceEmittedSyncChangeTypes =
    [
        "MODEL_ADDED",
        "MODEL_STATUS_CHANGED",
        "MODE_ADDED",
        "DURATION_ADDED",
        "DURATION_REMOVED",
        "RESOLUTION_ADDED",
        "PRICE_ADDED",
        "PRICE_CHANGED",
        "PRICE_DISABLED"
    ];

    [Fact]
    public void CapabilityUnitTypes_MatchProductionContract()
    {
        Assert.Equal(ProductionUnitTypes, AiProviderCatalog.UnitTypes);
        Assert.Contains("image", AiProviderCatalog.UnitTypes);
        Assert.True(AiProviderCatalog.IsValidUnitType("image"));
    }

    [Fact]
    public void CapabilityUnitTypes_AcceptEveryProductionSupportedValue()
    {
        foreach (var unitType in ProductionUnitTypes)
        {
            Assert.True(AiProviderCatalog.IsValidUnitType(unitType), $"Expected unit_type '{unitType}' to be accepted.");
        }
    }

    [Fact]
    public void CapabilityUnitTypes_RejectArbitraryValues()
    {
        Assert.DoesNotContain("random", AiProviderCatalog.UnitTypes);
        Assert.DoesNotContain("banana", AiProviderCatalog.UnitTypes);
        Assert.False(AiProviderCatalog.IsValidUnitType("random"));
        Assert.False(AiProviderCatalog.IsValidUnitType(""));
        Assert.False(AiProviderCatalog.IsValidUnitType(null));
    }

    [Fact]
    public void CapabilityUiUsesTheCanonicalUnitTypeList()
    {
        var page = ReadSource("TodoX.Web", "Components", "Pages", "AiProviders.razor");

        Assert.Contains("foreach (var u in AiProviderCatalog.UnitTypes)", page, StringComparison.Ordinal);
    }

    [Fact]
    public void SyncChangeTypes_EmittedByServiceAreAcceptedByMigrationContract()
    {
        var service = ReadSource("TodoX.Web", "Services", "AiProviders", "AiProviderSyncService.cs");
        var migration = ReadSource("database", "manual", "ai-provider-catalog", "04_fix_sync_change_type_check.sql");

        foreach (var changeType in ServiceEmittedSyncChangeTypes)
        {
            Assert.Contains($"\"{changeType}\"", service, StringComparison.Ordinal);
        }

        foreach (var changeType in CurrentSyncChangeTypes)
        {
            Assert.Contains($"'{changeType}'", migration, StringComparison.Ordinal);
        }

        foreach (var legacyType in LegacySyncChangeTypes)
        {
            Assert.Contains($"'{legacyType}'", migration, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void SyncChangeMigration_DropsAndRecreatesTheNamedConstraint()
    {
        var migration = ReadSource("database", "manual", "ai-provider-catalog", "04_fix_sync_change_type_check.sql");

        Assert.Contains("DROP CONSTRAINT IF EXISTS ck_todox_ai_provider_sync_change_type", migration, StringComparison.Ordinal);
        Assert.Contains("ADD CONSTRAINT ck_todox_ai_provider_sync_change_type", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderModelInsertPathsPersistProviderCodeFromTodoXProvider()
    {
        var repository = ReadSource("TodoX.Web", "Services", "AiProviders", "AiProviderModelRepository.cs");
        var modelService = ReadSource("TodoX.Web", "Services", "AiProviders", "AiProviderModelService.cs");
        var syncService = ReadSource("TodoX.Web", "Services", "AiProviders", "AiProviderSyncService.cs");

        foreach (Match insert in Regex.Matches(repository, @"INSERT INTO public\.todox_ai_provider_model\s*\((?<columns>.*?)\)\s*VALUES\s*\((?<values>.*?)\)", RegexOptions.Singleline))
        {
            Assert.Contains("provider_code", insert.Groups["columns"].Value, StringComparison.OrdinalIgnoreCase);
            Assert.Matches(@"@(?:ProviderCode|providerCode)\b", insert.Groups["values"].Value);
        }

        Assert.Contains("provider_code = EXCLUDED.provider_code", repository, StringComparison.Ordinal);
        Assert.Contains("model.ProviderCode", repository, StringComparison.Ordinal);
        Assert.Contains("model.ProviderCode", modelService, StringComparison.Ordinal);
        Assert.Contains("ProviderCode = provider.ProviderCode", syncService, StringComparison.Ordinal);
        Assert.DoesNotContain("ProviderCode = snapshot", syncService, StringComparison.Ordinal);
        Assert.Contains("ON CONFLICT (provider_id, provider_model_code)", repository, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] parts)
    {
        var file = Path.Combine(new[] { FindRepoRoot() }.Concat(parts).ToArray());
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
}
