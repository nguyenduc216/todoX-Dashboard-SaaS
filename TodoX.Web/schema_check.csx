using System;
using Npgsql;

var cs = "Host=113.160.249.61;Port=5432;Database=todox;Username=postgres;Password=Q3sNSrRwxquzAhO24pjfHovL;Pooling=true;Minimum Pool Size=0;Maximum Pool Size=20";
await using var conn = new NpgsqlConnection(cs);
await conn.OpenAsync();
var sql = @"select table_name, column_name, data_type, character_maximum_length
from information_schema.columns
where table_schema='public'
  and table_name in ('todox_ai_character','todox_ai_character_render','todox_ai_provider_usage_log')
order by table_name, ordinal_position;";
await using var cmd = new NpgsqlCommand(sql, conn);
await using var reader = await cmd.ExecuteReaderAsync();
while (await reader.ReadAsync())
{
    var len = reader.IsDBNull(3) ? "text/other" : reader.GetInt32(3).ToString();
    Console.WriteLine($"{reader.GetString(0)}.{reader.GetString(1)} | {reader.GetString(2)}({len})");
}
