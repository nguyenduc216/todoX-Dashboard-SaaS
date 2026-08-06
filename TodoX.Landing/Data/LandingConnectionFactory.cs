using System.Data;
using Npgsql;

namespace TodoX.Landing.Data;

public sealed class LandingConnectionFactory
{
    private readonly string? _connectionString;

    public LandingConnectionFactory(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("TodoXSaaS");
    }

    public async Task<IDbConnection> OpenAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            throw new LandingSchemaUnavailableException("Missing connection string 'TodoXSaaS'.");
        }

        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        return connection;
    }
}

public sealed class LandingSchemaUnavailableException : Exception
{
    public LandingSchemaUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
