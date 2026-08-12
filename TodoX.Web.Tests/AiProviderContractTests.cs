using System.Text;
using System.Text.RegularExpressions;
using TodoX.Web.Models;
using TodoX.Web.Services.AiProviders;
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

    [Fact]
    public void ProviderModelInsertPathsProtectProductionNotNullRuntimeValues()
    {
        var repository = ReadSource("TodoX.Web", "Services", "AiProviders", "AiProviderModelRepository.cs");

        foreach (var insertBlock in ProviderModelInsertBlocks(repository))
        {
            Assert.Contains("COALESCE(NULLIF(@ProviderStatus, ''), 'UNKNOWN')", insertBlock, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("COALESCE(NULLIF(@ProviderPriceUnit, ''), 'credit')", insertBlock, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("COALESCE(NULLIF(@Source, ''), 'catalog')", insertBlock, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("CASE WHEN NULLIF(@RawJson, '') IS NULL THEN '{}'::jsonb ELSE CAST(@RawJson AS jsonb) END", insertBlock, StringComparison.OrdinalIgnoreCase);
            Assert.Matches(@"GREATEST\(@(?:FailureCount|failureCount), 0\)", insertBlock);
        }
    }

    [Fact]
    public void PriceUpsertUsesProductionNormalizedVariantIdentity()
    {
        var pricingRepository = ReadSource("TodoX.Web", "Services", "AiProviders", "AiPricingRepository.cs");
        var modelRepository = ReadSource("TodoX.Web", "Services", "AiProviders", "AiProviderModelRepository.cs");

        foreach (var source in new[] { pricingRepository, modelRepository })
        {
            Assert.Contains("(COALESCE(mode, ''::character varying))", source, StringComparison.Ordinal);
            Assert.Contains("(COALESCE(resolution, ''::character varying))", source, StringComparison.Ordinal);
            Assert.Contains("(COALESCE(duration_seconds, (0)::numeric))", source, StringComparison.Ordinal);
            Assert.Contains("(COALESCE(ratio, ''::character varying))", source, StringComparison.Ordinal);
            Assert.Contains("rate_type,", source, StringComparison.Ordinal);
            Assert.Contains("unit_type", source, StringComparison.Ordinal);
            Assert.Contains("WHERE active = true", source, StringComparison.Ordinal);
            Assert.Contains("AND effective_to IS NULL", source, StringComparison.Ordinal);
            Assert.DoesNotContain("ON CONFLICT (model_id, mode, resolution, duration_seconds, ratio)", source, StringComparison.Ordinal);
            Assert.DoesNotContain("ON CONFLICT (model_id, (COALESCE(mode, '')), (COALESCE(resolution, '')), (COALESCE(duration_seconds, 0)), (COALESCE(ratio, '')))", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void CatalogModelNormalizationUsesProductionSafeDefaults()
    {
        var syncService = ReadSource("TodoX.Web", "Services", "AiProviders", "AiProviderSyncService.cs");

        Assert.Contains("ProviderStatus = NormalizeNullable(snapshot.ProviderStatus) ?? \"UNKNOWN\"", syncService, StringComparison.Ordinal);
        Assert.Contains("ProviderPriceUnit = ResolveModelProviderPriceUnit(provider.ProviderCode, snapshot)", syncService, StringComparison.Ordinal);
        Assert.Contains("Normalize79AiModelProviderPriceUnit", syncService, StringComparison.Ordinal);
        Assert.Contains("\"79ai_credit\" or \"credits\" => \"credit\"", syncService, StringComparison.Ordinal);
        Assert.Contains("Source = NormalizeNullable(snapshot.Source) ?? \"catalog\"", syncService, StringComparison.Ordinal);
        Assert.Contains("RawJson = SanitizeRawJson(snapshot.RawJson) ?? \"{}\"", syncService, StringComparison.Ordinal);
        Assert.Contains("FailureCount = Math.Max(snapshot.FailureCount, 0)", syncService, StringComparison.Ordinal);
        Assert.Contains("DisplayName = NormalizeNullable(snapshot.DisplayName) ?? providerModelCode", syncService, StringComparison.Ordinal);
        Assert.Contains("reason = \"invalid/no media type\"", syncService, StringComparison.Ordinal);
        Assert.Contains("reason = \"invalid/no model code\"", syncService, StringComparison.Ordinal);
        Assert.Contains("ProviderCode = provider.ProviderCode", syncService, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, "credit")]
    [InlineData("79ai_credit", "credit")]
    [InlineData("credits", "credit")]
    public void CatalogModelNormalization_Maps79AiModelLevelProviderPriceUnitToCredit(string? inputUnit, string expectedUnit)
    {
        var detail = BuildCatalogDetail(new AiCatalogModelSnapshot
        {
            ProviderModelCode = "grok_video_heavy",
            DisplayName = "",
            MediaType = "video",
            ProviderPriceUnit = inputUnit,
            ProviderStatus = "",
            Source = "",
            RawJson = null,
            FailureCount = -3
        });

        Assert.Equal("79ai", detail.ProviderCode);
        Assert.Equal("credit", detail.ProviderPriceUnit);
        Assert.Equal(expectedUnit, detail.ProviderPriceUnit);
        Assert.Equal("UNKNOWN", detail.ProviderStatus);
        Assert.Equal("catalog", detail.Source);
        Assert.Equal("{}", detail.RawJson);
        Assert.Equal("grok_video_heavy", detail.DisplayName);
        Assert.Equal(0, detail.FailureCount);
    }

    [Fact]
    public void CatalogModelNormalization_UsesFirstPriceUnitWhenModelUnitIsMissing()
    {
        var detail = BuildCatalogDetail(new AiCatalogModelSnapshot
        {
            ProviderModelCode = "veo-3.1",
            DisplayName = "VEO 3.1",
            MediaType = "video",
            Prices = [new AiModelPriceDto { ProviderPriceUnit = "79ai_credit" }]
        });

        Assert.Equal("veo-3.1", detail.ProviderModelCode);
        Assert.Equal("VEO 3.1", detail.DisplayName);
        Assert.Equal("credit", detail.ProviderPriceUnit);
    }

    private static AiProviderModelDetailDto BuildCatalogDetail(AiCatalogModelSnapshot snapshot)
    {
        var method = typeof(AiProviderSyncService).GetMethod("BuildDetail", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        var provider = new AiProviderDetailDto
        {
            Id = 79,
            ProviderCode = "79ai",
            ProviderName = "79AI"
        };

        return Assert.IsType<AiProviderModelDetailDto>(method!.Invoke(null, [provider, snapshot]));
    }

    private static IEnumerable<string> ProviderModelInsertBlocks(string repository)
    {
        var marker = "INSERT INTO public.todox_ai_provider_model";
        var index = 0;
        while ((index = repository.IndexOf(marker, index, StringComparison.Ordinal)) >= 0)
        {
            var end = repository.IndexOf("ON CONFLICT", index, StringComparison.Ordinal);
            Assert.True(end > index, "Provider model insert block must contain ON CONFLICT.");
            yield return repository[index..end];
            index = end;
        }
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
