using System.Text;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class AiProviderSyncSchemaContractTests
{
    [Fact]
    public void ProviderSyncUsesCanonicalProductionSchemaAndGuidIdentifiers()
    {
        var models = ReadSource("TodoX.Web", "Models", "AiProviderModels.cs");
        var repository = ReadSource("TodoX.Web", "Services", "AiProviders", "AiProviderModelRepository.cs");
        var service = ReadSource("TodoX.Web", "Services", "AiProviders", "AiProviderSyncService.cs");

        Assert.Contains("public Guid Id { get; set; }", models, StringComparison.Ordinal);
        Assert.Contains("public Guid SyncId { get; set; }", models, StringComparison.Ordinal);
        Assert.Contains("public Guid? SyncId { get; set; }", service, StringComparison.Ordinal);

        foreach (var column in new[]
        {
            "trigger_type",
            "triggered_by",
            "models_inserted",
            "models_updated",
            "models_unavailable",
            "pricing_rows_changed",
            "old_value_json",
            "new_value_json",
            "changed_fields"
        })
        {
            Assert.Contains(column, repository, StringComparison.Ordinal);
        }

        foreach (var forbidden in new[]
        {
            "\"trigger\"",
            "requested_by",
            "model_catalog_endpoint",
            "model_inserted_count",
            "model_updated_count",
            "model_unavailable_count",
            "price_changed_count",
            "message AS Message",
            "before_json",
            "after_json"
        })
        {
            Assert.DoesNotContain(forbidden, repository, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("\"manual\"", service, StringComparison.Ordinal);
        Assert.Contains("\"scheduled\"", service, StringComparison.Ordinal);
        Assert.Contains("triggeredBy", service, StringComparison.Ordinal);
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
