using System.Text;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class SystemDiagnosticsRegressionTests
{
    [Fact]
    public void BuildMetadataIsEmbeddedFromGitWhenAvailable()
    {
        var project = ReadRepoFile("TodoX.Web.csproj");

        Assert.Contains("PopulateBuildMetadataFromGit", project);
        Assert.Contains("git rev-parse HEAD", project);
        Assert.Contains("git rev-parse --abbrev-ref HEAD", project);
        Assert.Contains("git log -1 --pretty=%25s", project);
        Assert.Contains("<_Parameter1>BuildCommit</_Parameter1>", project);
        Assert.Contains("<_Parameter1>BuildBranch</_Parameter1>", project);
        Assert.Contains("<_Parameter1>BuildCommitMessage</_Parameter1>", project);
        Assert.Contains("<_Parameter1>PublishTimeUtc</_Parameter1>", project);
        Assert.DoesNotContain("30e8e9a", project, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SystemVersionEndpointUsesRuntimeBuildInfoWithoutExposingConfiguration()
    {
        var program = ReadRepoFile("Program.cs");

        Assert.Contains("AddSingleton<IRuntimeBuildInfoProvider, RuntimeBuildInfoProvider>", program);
        Assert.Contains("AddScoped<ISystemDiagnosticsService, SystemDiagnosticsService>", program);
        Assert.Contains("app.MapGet(\"/system/version\"", program);
        Assert.Contains("IRuntimeBuildInfoProvider buildInfoProvider", program);
        Assert.Contains("var build = buildInfoProvider.Get();", program);
        Assert.Contains("commit = build.CommitSha", program);
        Assert.Contains("shortCommit = build.CommitShortSha", program);
        Assert.Contains("publishTimeUtc = build.PublishTimeUtc", program);

        var endpointStart = program.IndexOf("app.MapGet(\"/system/version\"", StringComparison.Ordinal);
        var endpointEnd = program.IndexOf("app.MapPost(\"/api/ai/cost/estimate\"", endpointStart, StringComparison.Ordinal);
        var endpoint = program[endpointStart..endpointEnd];
        Assert.DoesNotContain("configuration.AsEnumerable()", endpoint);
        Assert.DoesNotContain("ConnectionStrings", endpoint, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ApiKey", endpoint, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Token", endpoint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DiagnosticsPageIsAdminOnlyAndVisibleFromAdminMenu()
    {
        var page = ReadRepoFile("Components", "Pages", "AdminSystemDiagnostics.razor");
        var layout = ReadRepoFile("Components", "Layout", "MainLayout.razor");

        Assert.Contains("@page \"/admin/system/diagnostics\"", page);
        Assert.Contains("AdminEndpointAuthorization.IsAdmin(AuthState.CurrentUser)", page);
        Assert.Contains("Admin permission required.", page);
        Assert.Contains("DiagnoseRDanceJobAsync", page);
        Assert.Contains("RunFullDiagnosticAsync", page);
        Assert.Contains("Exception details", page);
        Assert.Contains("System diagnostics", layout);
        Assert.Contains("Build @_runtimeBuild.CommitShortSha", layout);
        Assert.Contains("Href = \"/admin/system/diagnostics\"", layout);
    }

    [Fact]
    public void DatabaseDiagnosticChecksRDanceCompatibilityColumns()
    {
        var service = ReadRepoFile("Services", "SystemDiagnostics", "SystemDiagnosticsService.cs");

        foreach (var column in new[]
        {
            "id",
            "tenant_id",
            "customer_id",
            "user_id",
            "render_job_id",
            "orientation",
            "character_orientation",
            "request_json",
            "result_video_url"
        })
        {
            Assert.Contains($"\"{column}\"", service);
        }

        Assert.Contains("information_schema.columns", service);
        Assert.Contains("table_schema = 'dance_sell'", service);
        Assert.Contains("table_name = 'dance_sell_jobs'", service);
        Assert.Contains("x.ColumnName != \"character_orientation\"", service);
    }

    [Fact]
    public void RDanceDiagnosticUsesProductionLoadFlowAndPreservesRealException()
    {
        var service = ReadRepoFile("Services", "SystemDiagnostics", "SystemDiagnosticsService.cs");
        var page = ReadRepoFile("Components", "Pages", "AdminSystemDiagnostics.razor");

        Assert.Contains("_danceSell.GetAsync(jobId, user, ct)", service);
        Assert.Contains("DanceSell.GetAsync -> DanceSellRepository.GetByIdAsync", service);
        Assert.Contains("PostgresException", service);
        Assert.Contains("SqlState", service);
        Assert.Contains("ex.ToString()", service);
        Assert.Contains("PostgreSQL error code", page);
        Assert.Contains("Stack trace", page);
        Assert.DoesNotContain("Khong the tai day du thong tin video", service, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RDanceRepositoryProjectionStillUsesOrientationColumn()
    {
        var repository = ReadRepoFile("Services", "DanceSell", "DanceSellRepository.cs");

        Assert.Contains("orientation AS CharacterOrientation", repository);
        Assert.DoesNotContain("character_orientation AS CharacterOrientation", repository);
    }

    private static string ReadRepoFile(params string[] parts)
    {
        var webRoot = File.Exists(Path.Combine(RepoRoot, "TodoX.Web.csproj"))
            ? RepoRoot
            : Path.Combine(RepoRoot, "TodoX.Web");

        return File.ReadAllText(Path.Combine(new[] { webRoot }.Concat(parts).ToArray()), Encoding.UTF8);
    }

    private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
}
