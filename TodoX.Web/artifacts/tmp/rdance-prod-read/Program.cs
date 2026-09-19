using System.Text.Json;
using System.Reflection;
using Npgsql;
using NpgsqlTypes;

Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "Microsoft.Extensions.Logging.Abstractions.dll"));

var repoRoot = Directory.GetCurrentDirectory();
using var settings = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(repoRoot, "appsettings.json")));
var connectionName = args.FirstOrDefault() ?? "TodoXSaaS";
var connectionString = settings.RootElement.GetProperty("ConnectionStrings").GetProperty(connectionName).GetString()
    ?? throw new InvalidOperationException("Missing TodoXAutomation connection string.");
var outputPath = Path.Combine(repoRoot, "artifacts", "codex", "rdance-first-submit-production-forensics", "evidence.json");
Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

var danceJobId = Guid.Parse("5ec35e57-4958-4991-b833-2e8c05e9debb");
var renderJobId = "5013d57f-aa63-408f-8a0f-57cbcff0c605";
var referenceMediaId = "647714d8-b37c-49d6-b851-d98aa02a0884";
var operationIds = new[]
{
    "4db26687-1883-4a18-a54a-23ceaf0cc502",
    "5013d57f-aa63-408f-8a0f-57cbcff0c605"
};
var successJobs = new[]
{
    new { Dance = "d4f7e068-9a07-45de-911c-72c193f80eb7", Render = (string?)"d200bd0d-901f-407b-847b-4a9688b426f8", Task = "831ee0b1e059f6f5" },
    new { Dance = "bd85376f-4d73-4f96-8977-656551d053e6", Render = (string?)null, Task = "816ee19e88659142" },
    new { Dance = "9d1076b8-e288-4cec-b23d-66b137f5b99f", Render = (string?)null, Task = "5edc5c1bd4d0f1cf" }
};

await using var conn = new NpgsqlConnection(connectionString);
await conn.OpenAsync();

static bool IsSensitiveKey(string key)
{
    var normalized = key.Replace("-", "_").ToLowerInvariant();
    return normalized.Contains("token") || normalized.Contains("authorization")
        || normalized.Contains("access_token") || normalized.Contains("secret")
        || normalized.Contains("password") || normalized.Contains("api_key")
        || normalized.Contains("credential");
}

static string SanitizeUrl(string value)
{
    if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Query))
        return value;

    var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Select(part =>
    {
        var key = Uri.UnescapeDataString(part.Split('=', 2)[0]);
        return IsSensitiveKey(key) ? $"{part.Split('=', 2)[0]}=<redacted>" : part;
    });
    return new UriBuilder(uri) { Query = string.Join("&", query) }.Uri.ToString();
}

static object? SanitizeJsonElement(JsonElement element)
{
    return element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().ToDictionary(
            property => property.Name,
            property => IsSensitiveKey(property.Name) ? (object?)"<redacted>" : SanitizeJsonElement(property.Value),
            StringComparer.OrdinalIgnoreCase),
        JsonValueKind.Array => element.EnumerateArray().Select(SanitizeJsonElement).ToList(),
        JsonValueKind.String => SanitizeString(element.GetString() ?? string.Empty),
        JsonValueKind.Number => element.GetRawText(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null
    };
}

static object? SanitizeString(string value)
{
    var trimmed = value.Trim();
    if ((trimmed.StartsWith("{") && trimmed.EndsWith("}")) || (trimmed.StartsWith("[") && trimmed.EndsWith("]")))
    {
        try
        {
            using var document = JsonDocument.Parse(trimmed);
            return SanitizeJsonElement(document.RootElement);
        }
        catch (JsonException)
        {
        }
    }
    return Uri.TryCreate(trimmed, UriKind.Absolute, out _) ? SanitizeUrl(value) : value;
}

static object? SanitizeValue(object? value)
{
    return value switch
    {
        null => null,
        string text => SanitizeString(text),
        JsonDocument document => SanitizeJsonElement(document.RootElement),
        JsonElement element => SanitizeJsonElement(element),
        DateTime dt => dt.ToUniversalTime().ToString("O"),
        DateTimeOffset dto => dto.ToUniversalTime().ToString("O"),
        Guid guid => guid.ToString(),
        byte[] bytes => $"<bytes:{bytes.Length}>",
        _ => value
    };
}

static async Task<List<Dictionary<string, object?>>> QueryAsync(
    NpgsqlConnection conn,
    string sql,
    IEnumerable<(string Name, object? Value, NpgsqlDbType? Type)>? parameters = null)
{
    await using var cmd = new NpgsqlCommand(sql, conn);
    if (parameters is not null)
    {
        foreach (var pair in parameters)
        {
            var parameter = pair.Type is NpgsqlDbType type
                ? cmd.Parameters.Add(pair.Name, type)
                : cmd.Parameters.AddWithValue(pair.Name, pair.Value ?? DBNull.Value);
            if (pair.Type is not null)
                parameter.Value = pair.Value ?? DBNull.Value;
        }
    }

    await using var reader = await cmd.ExecuteReaderAsync();
    var rows = new List<Dictionary<string, object?>>();
    while (await reader.ReadAsync())
    {
        var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < reader.FieldCount; i++)
            row[reader.GetName(i)] = reader.IsDBNull(i) ? null : SanitizeValue(reader.GetValue(i));
        rows.Add(row);
    }
    return rows;
}

var catalogTables = await QueryAsync(conn, """
SELECT table_schema, table_name
FROM information_schema.tables
WHERE table_schema NOT IN ('pg_catalog', 'information_schema')
ORDER BY table_schema, table_name;
""");

var catalogColumns = await QueryAsync(conn, """
SELECT table_schema, table_name, column_name, data_type, udt_name
FROM information_schema.columns
WHERE table_schema NOT IN ('pg_catalog', 'information_schema')
  AND ((table_schema = 'public' AND table_name IN ('todox_rdance_jobs', 'todox_render_jobs',
    'todox_render_events', 'todox_render_queue', 'todox_v2v_dance_jobs',
    'todox_rendervideo_retry_runs', 'todox_vdl_render_requests'))
    OR (table_schema = 'dance_sell' AND table_name IN ('dance_sell_jobs',
      'dance_sell_provider_operations', 'dance_sell_reference_versions'))
    OR (table_schema = 'render' AND table_name IN ('render_jobs', 'render_job_events',
      'render_job_inputs', 'render_job_steps', 'render_job_snapshots'))
    OR (table_schema = 'public' AND table_name = 'todox_ai_operation_assets')
    OR (table_schema = 'media' AND table_name = 'media_files'))
ORDER BY table_schema, table_name, ordinal_position;
""");

if (!string.Equals(connectionName, "TodoXAutomation", StringComparison.OrdinalIgnoreCase))
{
    const string saasDanceColumns = """
id, tenant_id, customer_id, user_id, title, reference_mode, character_media_id,
product_media_id, direct_reference_media_id, motion_media_id, selected_reference_version_id,
reference_render_job_id, motion_render_job_id, placement_mode, image_prompt, video_prompt,
prompt, mode, orientation, character_orientation, current_stage, status, result_media_id,
result_url, result_video_url, error_code, error_message, created_at, updated_at, completed_at,
render_job_id, logical_request_id, character_image_url, motion_video_url, provider_code,
provider_model, provider_task_id, provider_status, request_json::text AS request_json,
submit_response_json::text AS submit_response_json, poll_response_json::text AS poll_response_json,
callback_json::text AS callback_json, error_json::text AS error_json, poll_count, next_poll_at,
submitted_at, last_polled_at, reference_provider_code, reference_provider_model,
reference_provider_capability_id, reference_provider_account_id, reference_approved_at,
motion_provider_code, motion_provider_model, motion_provider_capability_id,
motion_provider_account_id, character_object_key, product_object_key, product_image_url,
motion_source_type, motion_source_url, motion_video_media_id, motion_video_object_key,
prepared_reference_media_id, prepared_reference_object_key, prepared_reference_url,
prepared_reference_status, prepared_reference_approved_at, source_stage_status, source_stage_error
""";
    const string saasOperationColumns = """
id, dance_sell_job_id, render_job_id, parent_operation_id, operation_type, attempt_no,
reference_mode, provider_code, provider_capability_id, provider_account_id, provider_model,
provider_task_id, status, provider_status, request_json::text AS request_json,
response_json::text AS response_json, callback_json::text AS callback_json,
error_json::text AS error_json, error_code, error_message, created_at, started_at,
submitted_at, completed_at, failed_at, updated_at
""";
    const string saasRenderColumns = """
id, tenant_id, customer_id, user_id, job_code, logical_request_id, job_type,
operation_type, business_entity_type, business_entity_id, parent_job_id, parent_render_job_id,
retry_of_job_id, title, prompt, source_type, source_url, target_duration_sec, scene_count,
status, current_step, progress_percent, attempt_count, max_attempts, provider_id,
provider_capability_id, provider_account_id, provider_code, model_code, provider_task_id,
provider_status, input_json::text AS input_json, prompt_json::text AS prompt_json,
reference_json::text AS reference_json, output_json::text AS output_json,
result_summary_json::text AS result_summary_json, last_provider_response_json::text AS last_provider_response_json,
provider_request_json::text AS provider_request_json, provider_response_json::text AS provider_response_json,
error_code, error_message, queued_at, started_at, completed_at, cancelled_at, created_at,
updated_at, provider_video_id_base
""";

    var saasDanceJobs = await QueryAsync(conn, $"""
SELECT {saasDanceColumns}
FROM dance_sell.dance_sell_jobs
WHERE id = @dance_id OR render_job_id = @render_id
   OR logical_request_id = @dance_id_text
ORDER BY created_at ASC;
""", [
        ("dance_id", danceJobId, NpgsqlDbType.Uuid),
        ("render_id", Guid.Parse(renderJobId), NpgsqlDbType.Uuid),
        ("dance_id_text", danceJobId.ToString(), NpgsqlDbType.Text)
    ]);
    var saasDanceRelated = await QueryAsync(conn, $"""
SELECT {saasDanceColumns}
FROM dance_sell.dance_sell_jobs
WHERE id IN (@success_1, @success_2, @success_3)
ORDER BY created_at ASC;
""", [
        ("success_1", Guid.Parse(successJobs[0].Dance), NpgsqlDbType.Uuid),
        ("success_2", Guid.Parse(successJobs[1].Dance), NpgsqlDbType.Uuid),
        ("success_3", Guid.Parse(successJobs[2].Dance), NpgsqlDbType.Uuid)
    ]);
    var saasOperations = await QueryAsync(conn, $"""
SELECT {saasOperationColumns}
FROM dance_sell.dance_sell_provider_operations
WHERE id IN (@operation_1, @operation_2)
   OR dance_sell_job_id = @dance_id
   OR render_job_id = @render_id
ORDER BY created_at ASC;
""", [
        ("operation_1", Guid.Parse(operationIds[0]), NpgsqlDbType.Uuid),
        ("operation_2", Guid.Parse(operationIds[1]), NpgsqlDbType.Uuid),
        ("dance_id", danceJobId, NpgsqlDbType.Uuid),
        ("render_id", Guid.Parse(renderJobId), NpgsqlDbType.Uuid)
    ]);
    var saasSuccessOperations = await QueryAsync(conn, $"""
SELECT {saasOperationColumns}
FROM dance_sell.dance_sell_provider_operations
WHERE dance_sell_job_id IN (@success_1, @success_2, @success_3)
ORDER BY dance_sell_job_id, attempt_no, created_at ASC;
""", [
        ("success_1", Guid.Parse(successJobs[0].Dance), NpgsqlDbType.Uuid),
        ("success_2", Guid.Parse(successJobs[1].Dance), NpgsqlDbType.Uuid),
        ("success_3", Guid.Parse(successJobs[2].Dance), NpgsqlDbType.Uuid)
    ]);
    var saasRenderJobs = await QueryAsync(conn, $"""
SELECT {saasRenderColumns}
FROM render.render_jobs
WHERE id IN (@render_1, @render_2, @render_3, @render_4)
   OR business_entity_id = @dance_id
   OR retry_of_job_id = @render_id OR parent_render_job_id = @render_id
ORDER BY created_at ASC;
""", [
        ("render_1", Guid.Parse("4db26687-1883-4a18-a54a-23ceaf0cc502"), NpgsqlDbType.Uuid),
        ("render_2", Guid.Parse(renderJobId), NpgsqlDbType.Uuid),
        ("render_3", Guid.Parse("976685e0-66ea-4889-b94d-93ab79c02fb8"), NpgsqlDbType.Uuid),
        ("render_4", Guid.Parse("ad2ed483-9d92-477c-bd4a-ce382ccf2b6a"), NpgsqlDbType.Uuid),
        ("render_id", Guid.Parse(renderJobId), NpgsqlDbType.Uuid),
        ("dance_id", danceJobId, NpgsqlDbType.Uuid)
    ]);
    var saasSuccessRenderJobs = await QueryAsync(conn, $"""
SELECT {saasRenderColumns}
FROM render.render_jobs
WHERE id = @render_id OR business_entity_id IN (@success_1, @success_2, @success_3)
ORDER BY created_at ASC;
""", [
        ("render_id", Guid.Parse(successJobs[0].Render!), NpgsqlDbType.Uuid),
        ("success_1", Guid.Parse(successJobs[0].Dance), NpgsqlDbType.Uuid),
        ("success_2", Guid.Parse(successJobs[1].Dance), NpgsqlDbType.Uuid),
        ("success_3", Guid.Parse(successJobs[2].Dance), NpgsqlDbType.Uuid)
    ]);
    var saasRenderEvents = await QueryAsync(conn, """
SELECT id, job_id, render_job_id, event_type, level, message, data_json::text AS data_json,
       provider_code, model_code, provider_account_id, provider_task_id, created_at
FROM render.render_job_events
WHERE job_id = @render_id OR render_job_id = @render_id
   OR data_json::text ILIKE '%' || @dance_id || '%'
   OR data_json::text ILIKE '%' || @operation_1 || '%'
   OR data_json::text ILIKE '%' || @operation_2 || '%'
ORDER BY created_at ASC, id ASC;
""", [
        ("render_id", Guid.Parse(renderJobId), NpgsqlDbType.Uuid),
        ("dance_id", danceJobId.ToString(), NpgsqlDbType.Text),
        ("operation_1", operationIds[0], NpgsqlDbType.Text),
        ("operation_2", operationIds[1], NpgsqlDbType.Text)
    ]);
    var saasInputs = await QueryAsync(conn, """
SELECT id, render_job_id, input_type, input_role, media_id, object_key, public_url,
       provider_url, mime_type, input_text, input_url, input_json::text AS input_json, created_at
FROM render.render_job_inputs
WHERE render_job_id = @render_id
ORDER BY created_at ASC;
""", [("render_id", Guid.Parse(renderJobId), NpgsqlDbType.Uuid)]);
    var saasAssets = await QueryAsync(conn, """
SELECT id, operation_id, asset_role, media_id, object_key, public_url, provider_url,
       mime_type, metadata_json::text AS metadata_json, created_at
FROM public.todox_ai_operation_assets
WHERE operation_id IN (
    SELECT id FROM dance_sell.dance_sell_provider_operations
    WHERE id IN (@operation_1, @operation_2)
       OR dance_sell_job_id = @dance_id
       OR render_job_id = @render_id
)
OR media_id = @reference_media
ORDER BY created_at ASC;
""", [
        ("operation_1", Guid.Parse(operationIds[0]), NpgsqlDbType.Uuid),
        ("operation_2", Guid.Parse(operationIds[1]), NpgsqlDbType.Uuid),
        ("dance_id", danceJobId, NpgsqlDbType.Uuid),
        ("render_id", Guid.Parse(renderJobId), NpgsqlDbType.Uuid),
        ("reference_media", Guid.Parse(referenceMediaId), NpgsqlDbType.Uuid)
    ]);
    var saasSuccessAssets = await QueryAsync(conn, """
SELECT id, operation_id, asset_role, media_id, object_key, public_url, provider_url,
       mime_type, metadata_json::text AS metadata_json, created_at
FROM public.todox_ai_operation_assets
WHERE operation_id IN (
    SELECT id FROM dance_sell.dance_sell_provider_operations
    WHERE dance_sell_job_id IN (@success_1, @success_2, @success_3)
)
ORDER BY operation_id, created_at ASC;
""", [
        ("success_1", Guid.Parse(successJobs[0].Dance), NpgsqlDbType.Uuid),
        ("success_2", Guid.Parse(successJobs[1].Dance), NpgsqlDbType.Uuid),
        ("success_3", Guid.Parse(successJobs[2].Dance), NpgsqlDbType.Uuid)
    ]);
    var saasMedia = await QueryAsync(conn, """
SELECT id, file_category, file_name, file_ext, mime_type, file_size_bytes, storage_provider,
       object_key, file_url, public_url, width, height, checksum, metadata::text AS metadata,
       is_active, created_at, updated_at
FROM media.media_files
WHERE id = @reference_media
   OR id IN (
      SELECT character_media_id FROM dance_sell.dance_sell_jobs WHERE id = @dance_id
      UNION SELECT product_media_id FROM dance_sell.dance_sell_jobs WHERE id = @dance_id
      UNION SELECT motion_media_id FROM dance_sell.dance_sell_jobs WHERE id = @dance_id
      UNION SELECT motion_video_media_id FROM dance_sell.dance_sell_jobs WHERE id = @dance_id
      UNION SELECT prepared_reference_media_id FROM dance_sell.dance_sell_jobs WHERE id = @dance_id
   )
ORDER BY created_at ASC;
""", [
        ("reference_media", Guid.Parse(referenceMediaId), NpgsqlDbType.Uuid),
        ("dance_id", danceJobId, NpgsqlDbType.Uuid)
    ]);
    var catalogOnly = new
    {
        generatedAtUtc = DateTime.UtcNow.ToString("O"),
        database = connectionName,
        accessMode = "SELECT only",
        connection = new { host = "redacted", database = "redacted", username = "redacted" },
        requested = new { danceJobId, renderJobId, referenceMediaId, operationIds, successJobs },
        catalog = new { tables = catalogTables, columns = catalogColumns },
        note = "This connection uses the current dance_sell/render schemas.",
        primary = new
        {
            danceJobs = saasDanceJobs,
            relatedDanceJobs = saasDanceRelated,
            operations = saasOperations,
            successOperations = saasSuccessOperations,
            renderJobs = saasRenderJobs,
            renderEvents = saasRenderEvents,
            renderInputs = saasInputs,
            assets = saasAssets,
            successAssets = saasSuccessAssets,
            media = saasMedia
        },
        successComparisons = new { danceJobs = saasDanceRelated, renderJobs = saasSuccessRenderJobs }
    };
    await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(catalogOnly, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"Wrote catalog-only evidence for {connectionName}: {outputPath}");
    return;
}

const string danceJobColumns = """
id, request_id, status, prompt, source_video_file_id, source_video_mime,
source_video_duration, source_video_url, source_resolved_url, source_video_79ai_url,
source_file_size_bytes, source_file_mime, source_prepared_at, provider_code,
provider_model, provider_job_id, provider_status, provider_request_json::text AS provider_request_json,
provider_response_json::text AS provider_response_json, submit_attempts, poll_count,
last_poll_at, result_video_url, error_message, queued_at, submitted_at, completed_at,
failed_at, created_at, updated_at
""";

var primaryDanceRows = await QueryAsync(conn, $"""
SELECT {danceJobColumns}
FROM public.todox_rdance_jobs
WHERE id = @dance_id OR provider_job_id = @render_id
   OR provider_job_id = @operation_1 OR provider_job_id = @operation_2
   OR source_video_file_id::text = @reference_media
ORDER BY created_at ASC;
""", [
    ("dance_id", danceJobId, NpgsqlDbType.Uuid),
    ("render_id", renderJobId, NpgsqlDbType.Text),
    ("operation_1", operationIds[0], NpgsqlDbType.Text),
    ("operation_2", operationIds[1], NpgsqlDbType.Text),
    ("reference_media", referenceMediaId, NpgsqlDbType.Text)
]);

var primaryRequestId = primaryDanceRows
    .FirstOrDefault(row => string.Equals(row.GetValueOrDefault("id")?.ToString(), danceJobId.ToString(), StringComparison.OrdinalIgnoreCase))
    ?.GetValueOrDefault("request_id")?.ToString();

var relatedDanceRows = primaryRequestId is null
    ? new List<Dictionary<string, object?>>()
    : await QueryAsync(conn, $"""
SELECT {danceJobColumns}
FROM public.todox_rdance_jobs
WHERE request_id = @request_id
ORDER BY created_at ASC;
""", [("request_id", primaryRequestId, NpgsqlDbType.Text)]);

var successDanceRows = await QueryAsync(conn, $"""
SELECT {danceJobColumns}
FROM public.todox_rdance_jobs
WHERE id IN (@success_1, @success_2, @success_3)
ORDER BY created_at ASC;
""", [
    ("success_1", Guid.Parse(successJobs[0].Dance), NpgsqlDbType.Uuid),
    ("success_2", Guid.Parse(successJobs[1].Dance), NpgsqlDbType.Uuid),
    ("success_3", Guid.Parse(successJobs[2].Dance), NpgsqlDbType.Uuid)
]);

const string renderJobColumns = """
job_id, source_url, status, public_video_url, final_payload_json::text AS final_payload_json,
created_at, updated_at
""";

var renderRows = await QueryAsync(conn, $"""
SELECT {renderJobColumns}
FROM public.todox_render_jobs
WHERE job_id = @render_id OR final_payload_json::text ILIKE '%' || @dance_id || '%'
   OR final_payload_json::text ILIKE '%' || @operation_1 || '%'
   OR final_payload_json::text ILIKE '%' || @operation_2 || '%'
ORDER BY created_at ASC;
""", [
    ("render_id", renderJobId, NpgsqlDbType.Text),
    ("dance_id", danceJobId.ToString(), NpgsqlDbType.Text),
    ("operation_1", operationIds[0], NpgsqlDbType.Text),
    ("operation_2", operationIds[1], NpgsqlDbType.Text)
]);

var successRenderIds = successJobs.Where(item => item.Render is not null).Select(item => item.Render!).ToArray();
var successRenderRows = successRenderIds.Length == 0
    ? new List<Dictionary<string, object?>>()
    : await QueryAsync(conn, $"SELECT {renderJobColumns} FROM public.todox_render_jobs WHERE job_id = @render_id;",
        [("render_id", successRenderIds[0], NpgsqlDbType.Text)]);

var renderEvents = await QueryAsync(conn, """
SELECT id, job_id, event_type, payload_json::text AS payload_json, created_at
FROM public.todox_render_events
WHERE job_id = @render_id OR payload_json::text ILIKE '%' || @dance_id || '%'
   OR payload_json::text ILIKE '%' || @operation_1 || '%'
   OR payload_json::text ILIKE '%' || @operation_2 || '%'
ORDER BY created_at ASC, id ASC;
""", [
    ("render_id", renderJobId, NpgsqlDbType.Text),
    ("dance_id", danceJobId.ToString(), NpgsqlDbType.Text),
    ("operation_1", operationIds[0], NpgsqlDbType.Text),
    ("operation_2", operationIds[1], NpgsqlDbType.Text)
]);

var successEvents = await QueryAsync(conn, """
SELECT id, job_id, event_type, payload_json::text AS payload_json, created_at
FROM public.todox_render_events
WHERE payload_json::text ILIKE '%' || @task_1 || '%'
   OR payload_json::text ILIKE '%' || @task_2 || '%'
   OR payload_json::text ILIKE '%' || @task_3 || '%'
ORDER BY created_at ASC, id ASC;
""", [
    ("task_1", successJobs[0].Task, NpgsqlDbType.Text),
    ("task_2", successJobs[1].Task, NpgsqlDbType.Text),
    ("task_3", successJobs[2].Task, NpgsqlDbType.Text)
]);

async Task<List<Dictionary<string, object?>>> JsonMatchAsync(string tableName, string[] needles)
{
    var predicates = string.Join(" OR ", needles.Select((_, index) => $"to_jsonb(t)::text ILIKE '%' || @needle_{index} || '%'"));
    var parameters = needles.Select((needle, index) => (Name: $"needle_{index}", Value: (object?)needle, Type: (NpgsqlDbType?)NpgsqlDbType.Text));
    return await QueryAsync(conn, $"SELECT to_jsonb(t)::text AS row_json FROM public.{tableName} AS t WHERE {predicates};", parameters);
}

var needles = new[] { danceJobId.ToString(), renderJobId }.Concat(operationIds).ToArray();
var queueRows = await JsonMatchAsync("todox_render_queue", needles);
var v2vRows = await JsonMatchAsync("todox_v2v_dance_jobs", needles);
var retryRows = await JsonMatchAsync("todox_rendervideo_retry_runs", needles);
var vdlRows = await JsonMatchAsync("todox_vdl_render_requests", needles);

var result = new
{
    generatedAtUtc = DateTime.UtcNow.ToString("O"),
    database = connectionName,
    accessMode = "SELECT only",
    connection = new { host = "redacted", database = "redacted", username = "redacted" },
    requested = new { danceJobId, renderJobId, referenceMediaId, operationIds, successJobs },
    catalog = new { tables = catalogTables, columns = catalogColumns },
    primary = new
    {
        danceJobs = primaryDanceRows,
        relatedDanceJobs = relatedDanceRows,
        renderJobs = renderRows,
        renderEvents,
        queueRows,
        v2vRows,
        retryRows,
        vdlRows
    },
    successComparisons = new { danceJobs = successDanceRows, renderJobs = successRenderRows, renderEvents = successEvents }
};

await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Wrote {outputPath}");
Console.WriteLine($"Primary Dance Sell rows: {primaryDanceRows.Count}; related Dance Sell rows: {relatedDanceRows.Count}");
Console.WriteLine($"Render rows: {renderRows.Count}; render events: {renderEvents.Count}");
Console.WriteLine($"Success Dance Sell rows: {successDanceRows.Count}; success events: {successEvents.Count}");
