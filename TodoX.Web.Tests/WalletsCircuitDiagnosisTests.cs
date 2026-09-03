using Dapper;
using Microsoft.Extensions.Configuration;
using TodoX.Web.Models;
using TodoX.Web.Data;
using TodoX.Web.Services;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class WalletsCircuitDiagnosisTests
{
    [Fact]
    public async Task WalletPageLoadDiagnostics_ReportsSchemaAndSectionFailures()
    {
        var config = BuildConfiguration();
        var connectionFactory = new TodoXConnectionFactory(config);
        var tenant = new TenantContext(connectionFactory, config);
        var billing = new BillingRepository(connectionFactory, tenant);
        var user = new CurrentUserSession
        {
            UserId = Guid.NewGuid(),
            DisplayName = "Diag",
            Email = "diag@local",
            Role = TodoXUserRole.SystemOperator,
            IsAuthenticated = true,
            IsRoot = true
        };

        await tenant.EnsureLoadedAsync();

        using var conn = await connectionFactory.OpenAsync();
        var customerColumns = (await conn.QueryAsync<(string column_name, string data_type)>(
            """
            SELECT column_name, data_type
              FROM information_schema.columns
             WHERE table_schema='crm'
               AND table_name='customers'
             ORDER BY ordinal_position;
            """)).ToList();

        Assert.Contains(customerColumns, c => c.column_name == "company_name");
        Assert.Contains(customerColumns, c => c.column_name == "full_name");
        Assert.Contains(customerColumns, c => c.column_name == "email");

        await RecordFailureAsync("rates", () => billing.GetPointRatesAsync(user));
        await RecordFailureAsync("wallets", () => billing.GetWalletsAsync(null, user));
        await RecordFailureAsync("history", () => billing.GetRecentTransactionsAsync(null, user));
        await RecordFailureAsync("vouchers", () => billing.GetPointVouchersAsync(user));
    }

    private static async Task RecordFailureAsync<T>(string name, Func<Task<T>> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"SECTION={name}; EX={ex.GetType().FullName}; MSG={ex.Message}; INNER={ex.InnerException?.GetType().FullName}:{ex.InnerException?.Message}", ex);
        }
    }

    private static IConfigurationRoot BuildConfiguration()
        => new ConfigurationBuilder()
            .SetBasePath(FindRepoRoot())
            .AddJsonFile(Path.Combine("TodoX.Web", "appsettings.json"), optional: false)
            .AddJsonFile(Path.Combine("TodoX.Web", "appsettings.Development.json"), optional: true)
            .AddEnvironmentVariables()
            .Build();

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
