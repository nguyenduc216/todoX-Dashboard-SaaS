using System;
using System.Threading.Tasks;
using Npgsql;

var cs = "Host=113.160.249.61;Port=5432;Database=todox;Username=postgres;Password=Q3sNSrRwxquzAhO24pjfHovL;Pooling=true;Minimum Pool Size=0;Maximum Pool Size=20";
await using var conn = new NpgsqlConnection(cs);
await conn.OpenAsync();
var sql = @"select table_schema, table_name, column_name, data_type, character_maximum_length
from information_schema.columns
where table_schema in ('public','auth','marketing','render','billing','content','crm','settings','social','reup')
  and data_type = 'character varying'
  and character_maximum_length = 50
order by table_schema, table_name, ordinal_position;";
await using var cmd = new NpgsqlCommand(sql, conn);
await using var reader = await cmd.ExecuteReaderAsync();
while (await reader.ReadAsync())
{
    Console.WriteLine($"{reader.GetString(0)}.{reader.GetString(1)}.{reader.GetString(2)} | {reader.GetString(3)}({reader.GetInt32(4)})");
}
