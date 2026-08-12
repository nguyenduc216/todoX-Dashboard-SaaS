using System.Net;
using System.Net.Http.Headers;
using TodoX.Web.Models;
using TodoX.Web.Services.AiProviders;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class AiProviderDurationPricingTests
{
    [Fact]
    public async Task CatalogClient_NormalizesVeoOmniDurationsAndVerifiedPrices()
    {
        var client = CreateClient(
            """
            {
              "models": [
                {
                  "model": "veo-omni",
                  "id_base": "veo",
                  "name": "VEO Omni",
                  "status": "ON",
                  "server": "gommo",
                  "rate_type": "credit",
                  "modes": ["flash"],
                  "durations": [4, 6, 8, 10],
                  "resolutions": ["720p"],
                  "prices": [
                    { "mode": "flash", "duration": 4, "resolution": "720p", "price": 1260, "price_default": 1400 }
                  ]
                }
              ]
            }
            """);

        var result = await client.FetchAsync(Provider());
        var model = Assert.Single(result.Models);

        Assert.Equal("veo-omni", model.ProviderModelCode);
        Assert.Equal("veo", model.ProviderModelIdBase);
        Assert.Equal("gommo", model.ServerCode);
        Assert.Equal([4, 6, 8, 10], model.SupportedDurations);
        Assert.Contains(model.Prices, x => x.Mode == "flash" && x.Resolution == "4K" && x.DurationSeconds == 10 && x.ProviderPrice == 7200);
    }

    [Fact]
    public async Task CatalogClient_KeepsSeedanceDurationsWithoutInventingPrices()
    {
        var client = CreateClient(
            """
            {
              "models": [
                {
                  "model": "seedance-2-pro",
                  "name": "Seedance 2 Pro",
                  "status": "ON",
                  "media_type": "video",
                  "durations": [5, 10],
                  "modes": ["fast", "professional"]
                }
              ]
            }
            """);

        var result = await client.FetchAsync(Provider());
        var model = Assert.Single(result.Models);

        Assert.Equal([5, 10], model.SupportedDurations);
        Assert.Empty(model.Prices);
    }

    [Fact]
    public async Task CatalogClient_KeepsDistinctVeoCodesAndPersistsGrokWhenReturned()
    {
        var client = CreateClient(
            """
            {
              "models": [
                { "model": "veo-fast-live", "name": "VEO Fast", "id_base": "veo", "type": "video", "durations": [4], "resolutions": ["720p"], "prices": [{ "mode": "fast", "duration": 4, "resolution": "720p", "price": 800 }] },
                { "model": "veo-lite-live", "name": "VEO Lite", "id_base": "veo", "type": "video", "durations": [6], "resolutions": ["1080p"], "prices": [{ "mode": "lite", "duration": 6, "resolution": "1080p", "price": 1100 }] },
                { "model": "veo-omni", "name": "VEO Omni", "id_base": "veo", "type": "video", "modes": ["flash"], "durations": [4], "resolutions": ["720p"] },
                { "model": "grok-video-live", "name": "Grok Video", "id_base": "grok", "type": "video", "server": "xai", "variants": [{ "mode": "standard", "duration": 6, "resolution": "720p", "ratio": "16:9", "price": 900 }] }
              ]
            }
            """);

        var result = await client.FetchAsync(Provider());

        Assert.Contains(result.Models, x => x.ProviderModelCode == "grok-video-live" && x.DisplayName == "Grok Video");
        Assert.Equal(4, result.Models.Select(x => x.ProviderModelCode).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains(result.Models, x => x.ProviderModelCode == "veo-fast-live");
        Assert.Contains(result.Models, x => x.ProviderModelCode == "veo-lite-live");
        Assert.Contains(result.Models, x => x.ProviderModelCode == "veo-omni");
    }

    [Fact]
    public async Task CatalogClient_InsertsUnknownValidModelAndPreservesNestedSeedanceModes()
    {
        var client = CreateClient(
            """
            {
              "items": [
                {
                  "model_key": "seedance-2-pro-live",
                  "label": "Seedance 2.0 Pro",
                  "modality": "video",
                  "variant_options": [
                    { "mode": "fast", "duration_seconds": 5, "resolution": "720p", "aspect_ratio": "16:9" },
                    { "mode": "professional", "duration_seconds": 10, "resolution": "1080p", "aspect_ratio": "9:16" }
                  ]
                },
                {
                  "model_id": "provider-new-video",
                  "display_name": "Provider New Video",
                  "media_type": "video",
                  "options": [{ "duration": 8, "size": "720p", "ratio": "1:1" }]
                }
              ]
            }
            """);

        var result = await client.FetchAsync(Provider());
        var seedance = Assert.Single(result.Models, x => x.ProviderModelCode == "seedance-2-pro-live");
        var unknown = Assert.Single(result.Models, x => x.ProviderModelCode == "provider-new-video");

        Assert.Equal(["fast", "professional"], seedance.SupportedModes);
        Assert.Equal([5, 10], seedance.SupportedDurations);
        Assert.Equal(["720p", "1080p"], seedance.SupportedResolutions);
        Assert.Equal(["16:9", "9:16"], seedance.SupportedRatios);
        Assert.Empty(seedance.Prices);
        Assert.Equal("Provider New Video", unknown.DisplayName);
        Assert.Equal(["720p"], unknown.SupportedResolutions);
    }

    [Fact]
    public void ProviderSync_SourceContract_UsesProviderIdAndProviderModelCodeWithIgnoredDiagnostics()
    {
        var repository = ReadSource("TodoX.Web", "Services", "AiProviders", "AiProviderModelRepository.cs");
        var pricingRepository = ReadSource("TodoX.Web", "Services", "AiProviders", "AiPricingRepository.cs");
        var sync = ReadSource("TodoX.Web", "Services", "AiProviders", "AiProviderSyncService.cs");

        Assert.Contains("ON CONFLICT (provider_id, provider_model_code)", repository, StringComparison.Ordinal);
        Assert.Contains("NormalizeCatalogSnapshotsAsync", sync, StringComparison.Ordinal);
        Assert.Contains("normalizedCatalog.Models", sync, StringComparison.Ordinal);
        Assert.Contains("snapshot.ProviderModelCode = code", sync, StringComparison.Ordinal);
        Assert.Contains("duplicate provider_model_code", sync, StringComparison.Ordinal);
        Assert.Contains("invalid/no model code", sync, StringComparison.Ordinal);
        Assert.Contains("ignored_models = ignoredModels", sync, StringComparison.Ordinal);
        Assert.Contains("result.PriceChangedCount,", sync, StringComparison.Ordinal);
        Assert.Contains("0,", sync, StringComparison.Ordinal);
        Assert.DoesNotContain("normalizedCatalog.IgnoredCount,\r\n                BuildSummaryJson(result, normalizedCatalog.IgnoredCount)", sync, StringComparison.Ordinal);
        Assert.DoesNotContain("provider_model_id_base = ANY", sync, StringComparison.OrdinalIgnoreCase);
        AssertPriceConflictTargetMatchesActiveVariantIndex(repository);
        AssertPriceConflictTargetMatchesActiveVariantIndex(pricingRepository);
    }

    [Fact]
    public void PriceRepository_UsesNormalizedVariantIdentity()
    {
        var pricingRepository = ReadSource("TodoX.Web", "Services", "AiProviders", "AiPricingRepository.cs");
        var modelRepository = ReadSource("TodoX.Web", "Services", "AiProviders", "AiProviderModelRepository.cs");

        foreach (var source in new[] { pricingRepository, modelRepository })
        {
            AssertPriceConflictTargetMatchesActiveVariantIndex(source);
            Assert.DoesNotContain("ON CONFLICT (model_id, mode, resolution, duration_seconds, ratio)", source, StringComparison.Ordinal);
            Assert.DoesNotContain("ON CONFLICT (model_id, (COALESCE(mode, '')), (COALESCE(resolution, '')), (COALESCE(duration_seconds, 0)), (COALESCE(ratio, '')))", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void PriceVariantIdentity_NormalizesOnlyActiveOpenRowsWithRateAndUnit()
    {
        var basePrice = new AiModelPriceDto
        {
            ModelId = 10,
            Mode = null,
            Resolution = null,
            DurationSeconds = null,
            Ratio = null,
            RateType = "credit",
            UnitType = "scene",
            Active = true,
            EffectiveTo = null
        };

        Assert.Equal(ActiveVariantKey(basePrice), ActiveVariantKey(new AiModelPriceDto { ModelId = 10, Mode = "", Resolution = "", DurationSeconds = 0, Ratio = "", RateType = "credit", UnitType = "scene", Active = true }));
        Assert.NotEqual(ActiveVariantKey(basePrice), ActiveVariantKey(new AiModelPriceDto { ModelId = 10, RateType = "usd", UnitType = "scene", Active = true }));
        Assert.NotEqual(ActiveVariantKey(basePrice), ActiveVariantKey(new AiModelPriceDto { ModelId = 10, RateType = "credit", UnitType = "request", Active = true }));
        Assert.Null(ActiveVariantKey(new AiModelPriceDto { ModelId = 10, RateType = "credit", UnitType = "scene", Active = false }));
        Assert.Null(ActiveVariantKey(new AiModelPriceDto { ModelId = 10, RateType = "credit", UnitType = "scene", Active = true, EffectiveTo = DateTime.UtcNow }));
    }

    [Fact]
    public void EstimateCost_MultipliesQuantityBySceneCount()
    {
        var model = new AiProviderModelListItemDto { Id = 1, ProviderId = 2, DisplayName = "VEO Omni" };
        var policy = new AiPricingPolicyDto { ProviderCreditPerInternalPoint = 1000, DefaultMarkupPercent = 20, MinimumSellPoints = 1, RoundingRule = "CEIL", Enabled = true, IsDefault = true };
        var price = new AiModelPriceDto { ProviderPrice = 1800, InternalCostPoints = 1.8m, SellPoints = 3, SellPriceMode = "FIXED", Active = true };

        var estimate = AiPricingEngine.BuildEstimate(model, policy, price, quantity: 4);

        Assert.True(estimate.Success);
        Assert.Equal(7200, estimate.ProviderTotalCost);
        Assert.Equal(7.2m, estimate.InternalTotalCostPoints);
        Assert.Equal(12, estimate.EstimatedTodoXPoints);
    }

    [Fact]
    public void ProviderSync_PreservesFixedSellPointsAndMarksRemovedPricesInactive()
    {
        var pricingRepository = ReadSource("TodoX.Web", "Services", "AiProviders", "AiPricingRepository.cs");
        var syncService = ReadSource("TodoX.Web", "Services", "AiProviders", "AiProviderSyncService.cs");

        Assert.Contains("sell_points = EXCLUDED.sell_points", pricingRepository);
        Assert.Contains("sell_price_mode = EXCLUDED.sell_price_mode", pricingRepository);
        Assert.Contains("markup_percent = EXCLUDED.markup_percent", pricingRepository);
        Assert.Contains("minimum_points = EXCLUDED.minimum_points", pricingRepository);
        Assert.Contains("PRICE_DISABLED", syncService);
        Assert.Contains("MarkPriceInactiveAsync", syncService);
        Assert.Contains("existingPrice.SellPoints", syncService);
        Assert.Contains("existingPrice.SellPriceMode", syncService);
    }

    [Fact]
    public void ProviderSync_UsesManualAndScheduledTriggers_WithFreshRetryTimeout()
    {
        var syncInterface = ReadSource("TodoX.Web", "Services", "AiProviders", "AiProviderSyncService.cs");
        var pricingPage = ReadSource("TodoX.Web", "Components", "Pages", "AiProviders.razor");
        var syncService = ReadSource("TodoX.Web", "Services", "AiProviders", "AiProviderSyncService.cs");
        var worker = ReadSource("TodoX.Web", "Services", "AiProviders", "AiProviderCatalogSyncWorker.cs");

        Assert.Contains("Task<AiProviderSyncResultDto> SyncScheduledProviderAsync", syncInterface);
        Assert.Contains("\"manual\"", syncService);
        Assert.Contains("\"scheduled\"", syncService);
        Assert.Contains("lookupTimeout", worker);
        Assert.Contains("RunAttemptAsync", worker);
        Assert.Contains("SyncScheduledProviderAsync", worker);
        Assert.Contains("CreateLinkedTokenSource(stoppingToken)", worker);
        Assert.Contains("AiProviderCatalogSync", worker);
        Assert.Contains("DailyHourLocal", worker);
        Assert.Contains("PriceSourceLabel", pricingPage);
        Assert.Contains("verified_seed", pricingPage);
        Assert.Contains("SavePriceAsync", pricingPage);
    }

    [Fact]
    public async Task CatalogClient_For79AiUsesSecureCredentialAndPostsImageAndVideoForms()
    {
        var handler = new RecordingJsonHandler(
            """{"data":[{"model":"img-model","type":"image","price":12,"access_token":"phase-c-token"}]}""",
            """{"items":[{"model":"video-model","type":"video","durations":[5],"resolutions":["720p"],"prices":[{"duration":5,"resolution":"720p","price":34}]}]}""");
        var resolver = new FakeResolver("phase-c-token");
        var repository = new FakeCredentialRepository
        {
            Account = new ProviderCredentialAccount
            {
                Id = resolver.AccountId,
                ProviderCode = "79ai",
                Environment = "production",
                ConfigJson = """{"domain":"79ai.net"}"""
            }
        };
        var client = new Ai79CatalogClient(new HttpClient(handler), resolver, repository);

        var result = await client.FetchAsync(new AiProviderDetailDto
        {
            ProviderCode = "79ai",
            BaseUrl = "https://api.gommo.net/ai",
            ConfigJson = "{}"
        });

        Assert.True(result.Configured);
        Assert.Equal("/models", result.ModelsPath);
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, r =>
        {
            Assert.Equal(HttpMethod.Post, r.Method);
            Assert.Equal("https://api.gommo.net/ai/models", r.Uri);
            Assert.Equal("application/x-www-form-urlencoded", r.ContentType);
            Assert.Contains("access_token=phase-c-token", r.Body);
            Assert.Contains("domain=79ai.net", r.Body);
            Assert.DoesNotContain("phase-c-token", r.Uri, StringComparison.Ordinal);
            Assert.Null(r.Authorization);
        });
        Assert.Contains("type=image", handler.Requests[0].Body);
        Assert.Contains("type=video", handler.Requests[1].Body);
        Assert.Equal(("79ai", "access_token"), resolver.LastResolve);
        Assert.Contains(result.Models, x => x.ProviderModelCode == "img-model" && x.MediaType == "image");
        Assert.Contains(result.Models, x => x.ProviderModelCode == "video-model" && x.MediaType == "video");
        Assert.DoesNotContain("phase-c-token", string.Join("\n", result.Models.Select(x => x.RawJson)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CatalogClient_For79AiDoesNotRequireLegacyPathsAndReturnsSanitizedCredentialError()
    {
        var handler = new RecordingJsonHandler("""{"models":[]}""");
        var resolver = new FakeResolver("phase-c-token") { Fail = true };
        var client = new Ai79CatalogClient(new HttpClient(handler), resolver, new FakeCredentialRepository());

        var result = await client.FetchAsync(new AiProviderDetailDto
        {
            ProviderCode = "79ai",
            BaseUrl = "https://api.gommo.net/ai",
            ConfigJson = "{}"
        });

        Assert.False(result.Configured);
        Assert.Equal("Không tìm thấy credential 79AI đang hoạt động.", result.Message);
        Assert.Empty(handler.Requests);
        Assert.DoesNotContain("phase-c-token", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CatalogClient_SourceContracts_DoNotUseLegacy79AiCredentialOrGetFor79Ai()
    {
        var client = ReadSource("TodoX.Web", "Services", "AiProviders", "AiCatalogClient.cs");
        var patch = ReadSource("database", "manual", "ai-provider-secure-credentials", "03_fix_provider_account_credential_secure_ref_check.sql");

        Assert.Contains("IProviderCredentialResolver", client, StringComparison.Ordinal);
        Assert.Contains("ResolveAsync(\"79ai\", \"access_token\"", client, StringComparison.Ordinal);
        Assert.Contains("PostAsync(uri, body, ct)", client, StringComparison.Ordinal);
        Assert.Contains("\"/models\"", client, StringComparison.Ordinal);
        Assert.Contains("[\"type\"] = mediaType", client, StringComparison.Ordinal);
        Assert.DoesNotContain("todox_video_79ai_provider_keys", client, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GetAsync(uri, ct);", client.Substring(0, client.IndexOf("LoadModelsAsync", StringComparison.Ordinal)), StringComparison.Ordinal);

        Assert.Contains("secure_credential_id IS NOT NULL", patch, StringComparison.Ordinal);
        Assert.Contains("todox_ai_provider_account_credential_ref_ck", patch, StringComparison.Ordinal);
    }

    private static Ai79CatalogClient CreateClient(string json)
        => new(
            new HttpClient(new JsonHandler(json)) { BaseAddress = new Uri("https://catalog.local") },
            new FakeResolver("phase-c-token"),
            new FakeCredentialRepository
            {
                Account = new ProviderCredentialAccount
                {
                    ProviderCode = "79ai",
                    Environment = "production",
                    ConfigJson = """{"domain":"79ai.net"}"""
                }
            });

    private static AiProviderDetailDto Provider()
        => new()
        {
            Id = 79,
            ProviderCode = "79ai",
            ProviderName = "79AI",
            BaseUrl = "https://catalog.local",
            ConfigJson = """{"catalog":{"video_models_path":"/catalog/video-models"}}"""
        };

    private static void AssertPriceConflictTargetMatchesActiveVariantIndex(string source)
    {
        Assert.Contains("ON CONFLICT (", source, StringComparison.Ordinal);
        Assert.Contains("model_id,", source, StringComparison.Ordinal);
        Assert.Contains("(COALESCE(mode, ''::character varying))", source, StringComparison.Ordinal);
        Assert.Contains("(COALESCE(resolution, ''::character varying))", source, StringComparison.Ordinal);
        Assert.Contains("(COALESCE(duration_seconds, (0)::numeric))", source, StringComparison.Ordinal);
        Assert.Contains("(COALESCE(ratio, ''::character varying))", source, StringComparison.Ordinal);
        Assert.Contains("rate_type,", source, StringComparison.Ordinal);
        Assert.Contains("unit_type", source, StringComparison.Ordinal);
        Assert.Contains("WHERE active = true", source, StringComparison.Ordinal);
        Assert.Contains("AND effective_to IS NULL", source, StringComparison.Ordinal);
    }

    private static string? ActiveVariantKey(AiModelPriceDto price)
        => price.Active && price.EffectiveTo is null
            ? string.Join("|",
                price.ModelId,
                price.Mode ?? string.Empty,
                price.Resolution ?? string.Empty,
                price.DurationSeconds ?? 0,
                price.Ratio ?? string.Empty,
                price.RateType ?? string.Empty,
                price.UnitType ?? string.Empty)
            : null;

    private static string ReadSource(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TodoX.Dashboard.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray()));
    }

    private sealed class JsonHandler : HttpMessageHandler
    {
        private readonly string _json;
        private bool _served;

        public JsonHandler(string json)
        {
            _json = json;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var json = _served ? """{"models":[]}""" : _json;
            _served = true;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json)
            });
        }
    }

    private sealed class RecordingJsonHandler : HttpMessageHandler
    {
        private readonly Queue<string> _responses;
        public List<RequestSnapshot> Requests { get; } = new();

        public RecordingJsonHandler(params string[] responses)
        {
            _responses = new Queue<string>(responses);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RequestSnapshot(
                request.Method,
                request.RequestUri!.ToString(),
                body,
                request.Content?.Headers.ContentType?.MediaType,
                request.Headers.Authorization));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responses.Count == 0 ? """{"models":[]}""" : _responses.Dequeue())
            };
        }
    }

    private sealed record RequestSnapshot(HttpMethod Method, string Uri, string Body, string? ContentType, AuthenticationHeaderValue? Authorization);

    private sealed class FakeResolver : IProviderCredentialResolver
    {
        private readonly string _secret;
        public Guid AccountId { get; } = Guid.NewGuid();
        public bool Fail { get; set; }
        public (string ProviderCode, string CredentialRole)? LastResolve { get; private set; }

        public FakeResolver(string secret)
        {
            _secret = secret;
        }

        public Task<ResolvedProviderCredential> ResolveAsync(string providerCode, string credentialRole, CancellationToken ct = default)
        {
            LastResolve = (providerCode, credentialRole);
            if (Fail)
            {
                throw new InvalidOperationException("missing");
            }

            return Task.FromResult(new ResolvedProviderCredential
            {
                ProviderAccountId = AccountId,
                ProviderCode = providerCode,
                CredentialRole = credentialRole,
                Secret = _secret,
                MaskedHint = "****oken"
            });
        }
    }

    private sealed class FakeCredentialRepository : IProviderCredentialRepository
    {
        public ProviderCredentialAccount? Account { get; set; }

        public Task<ProviderCredentialAccount?> GetAccountByIdAsync(Guid providerAccountId, CancellationToken ct = default)
            => Task.FromResult<ProviderCredentialAccount?>(Account ?? new ProviderCredentialAccount { Id = providerAccountId, ProviderCode = "79ai", ConfigJson = """{"domain":"79ai.net"}""" });

        public Task<ProviderCredentialAccount?> GetPreferredAccountAsync(string providerCode, string environment = "production", CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ProviderCredentialMapping?> GetActiveMappingAsync(Guid providerAccountId, string credentialRole, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ProviderSecureCredentialRecord?> GetSecureCredentialAsync(Guid secureCredentialId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ProviderSecureCredentialRecord?> GetActiveSecureCredentialAsync(Guid providerAccountId, string credentialRole, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Guid> InsertSecureCredentialAsync(Guid providerAccountId, string credentialRole, ProtectedProviderCredential protectedCredential, Guid? userId, string metadataJson, CancellationToken ct = default) => throw new NotImplementedException();
        public Task DeactivatePriorSecureCredentialsAsync(Guid providerAccountId, string credentialRole, Guid keepSecureCredentialId, Guid? userId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task UpsertMappingAsync(Guid providerAccountId, string credentialRole, Guid secureCredentialId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task DeactivatePriorMappingsAsync(Guid providerAccountId, string credentialRole, Guid keepSecureCredentialId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task SetProviderAccountEnabledDefaultAsync(Guid providerAccountId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task UpdateLastUsedAsync(Guid secureCredentialId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ProviderAccountCredentialMetadata?> GetCredentialMetadataAsync(Guid providerAccountId, string credentialRole, CancellationToken ct = default) => throw new NotImplementedException();
    }
}
