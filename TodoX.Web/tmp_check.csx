using System;
using System.Threading.Tasks;
using TodoX.Web.Data;
using Dapper;

var factory = new TodoXConnectionFactory();
using var conn = await factory.OpenAsync();
var rows = await conn.QueryAsync(@"
SELECT p.provider_code, p.enabled AS provider_enabled, p.priority,
       c.id AS capability_id, c.capability_code, c.enabled AS capability_enabled,
       c.is_default, c.allow_user_select, c.model_name
  FROM public.todox_ai_provider_capability c
  JOIN public.todox_ai_provider p ON p.id = c.provider_id
 WHERE c.capability_code = @code
 ORDER BY c.is_default DESC, p.priority, p.provider_name;", new { code = "avatar_generation" });
foreach (var row in rows)
{
    Console.WriteLine(row);
}
