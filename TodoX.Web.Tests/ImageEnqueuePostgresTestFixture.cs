using Npgsql;

namespace TodoX.Web.Tests;

/// <summary>
/// Approval-gated harness for future real-transaction reconciliation tests. Never opens a
/// connection without both explicit operator approval and a separately verified test database.
/// No database test invokes this harness until an isolated schema and disposable rows are approved.
/// </summary>
internal sealed class ImageEnqueuePostgresTestFixture : IAsyncDisposable
{
    private readonly NpgsqlDataSource _source;

    private ImageEnqueuePostgresTestFixture(NpgsqlDataSource source) => _source = source;

    internal static ImageEnqueuePostgresTestFixture CreateApproved()
    {
        if (Environment.GetEnvironmentVariable("TODOX_RECONCILIATION_DB_TEST_APPROVED") != "YES"
            || Environment.GetEnvironmentVariable("TODOX_RECONCILIATION_DB_OWNER_CONFIRMED") != "YES"
            || Environment.GetEnvironmentVariable("TODOX_RECONCILIATION_DISPOSABLE_ROWS_APPROVED") != "YES")
            throw new InvalidOperationException("Explicit reconciliation PostgreSQL test approval is required.");

        var value = Environment.GetEnvironmentVariable("TODOX_RECONCILIATION_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("A dedicated reconciliation test connection string is required.");

        var builder = new NpgsqlConnectionStringBuilder(value);
        // An allowlisted exact database name must be confirmed by the test DB owner; reject
        // common production database aliases before any connection is opened.
        var expected = Environment.GetEnvironmentVariable("TODOX_RECONCILIATION_TEST_DATABASE_NAME");
        if (string.IsNullOrWhiteSpace(expected) || !string.Equals(builder.Database, expected, StringComparison.Ordinal)
            || !expected.Contains("test", StringComparison.OrdinalIgnoreCase)
            || string.Equals(builder.Database, "postgres", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The isolated test database identity has not been verified.");

        return new ImageEnqueuePostgresTestFixture(NpgsqlDataSource.Create(builder.ConnectionString));
    }

    internal async Task<(NpgsqlConnection First, NpgsqlConnection Second)> OpenIndependentConnectionsAsync(CancellationToken ct)
    {
        var first = await _source.OpenConnectionAsync(ct);
        try
        {
            var second = await _source.OpenConnectionAsync(ct);
            if (first.ProcessID == second.ProcessID)
                throw new InvalidOperationException("Test requires independent PostgreSQL sessions.");
            return (first, second);
        }
        catch
        {
            await first.DisposeAsync();
            throw;
        }
    }

    internal async Task<NpgsqlConnection> OpenAsync(CancellationToken ct) => await _source.OpenConnectionAsync(ct);

    internal string ConnectionString => _source.ConnectionString;

    public ValueTask DisposeAsync() => _source.DisposeAsync();
}