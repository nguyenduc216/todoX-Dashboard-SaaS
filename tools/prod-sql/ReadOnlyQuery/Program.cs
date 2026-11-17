// Read-only query runner: every statement runs inside a READ ONLY transaction that is rolled back.
using System.Text.Json;
using Npgsql;
var settings = JsonDocument.Parse(File.ReadAllText(args[0]));
var cs = settings.RootElement.GetProperty("ConnectionStrings").GetProperty("TodoXSaaS").GetString();
var sql = File.ReadAllText(args[1]);
await using var conn = new NpgsqlConnection(cs);
await conn.OpenAsync();
await using var tx = await conn.BeginTransactionAsync();
await using (var ro = new NpgsqlCommand("SET TRANSACTION READ ONLY", conn, tx)) await ro.ExecuteNonQueryAsync();
foreach (var stmt in sql.Split("\n--#\n"))
{
    if (string.IsNullOrWhiteSpace(stmt)) continue;
    await using var cmd = new NpgsqlCommand(stmt, conn, tx);
    await using var r = await cmd.ExecuteReaderAsync();
    Console.WriteLine("=== " + stmt.Trim().Split('\n')[0]);
    while (await r.ReadAsync())
    {
        var cols = Enumerable.Range(0, r.FieldCount).Select(i => $"{r.GetName(i)}={(r.IsDBNull(i) ? "null" : Convert.ToString(r.GetValue(i)))}");
        Console.WriteLine(string.Join(" | ", cols));
    }
}
await tx.RollbackAsync();
