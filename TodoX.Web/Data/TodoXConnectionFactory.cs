using System.Data;
using Npgsql;

namespace TodoX.Web.Data;

/// <summary>Creates open Npgsql connections to the todo_saas (Foundation V2) database.</summary>
public sealed class TodoXConnectionFactory
{
    private readonly string _connectionString;

    public TodoXConnectionFactory(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("TodoXSaaS")
            ?? throw new InvalidOperationException("Missing connection string 'TodoXSaaS'.");
    }

    public async Task<IDbConnection> OpenAsync(CancellationToken ct = default)
    {
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        return connection;
    }
}

/// <summary>Creates open Npgsql connections to the todox automation database (settings, jobs, render pipeline).</summary>
public sealed class TodoXAutomationConnectionFactory
{
    private readonly string _connectionString;

    public TodoXAutomationConnectionFactory(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("TodoXAutomation")
            ?? throw new InvalidOperationException("Missing connection string 'TodoXAutomation'.");
    }

    public async Task<IDbConnection> OpenAsync(CancellationToken ct = default)
    {
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        return connection;
    }
}
