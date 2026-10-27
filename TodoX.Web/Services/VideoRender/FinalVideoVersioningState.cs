using Dapper;
using TodoX.Web.Data;

namespace TodoX.Web.Services.VideoRender;

public sealed record FinalVideoVersioningState(bool Ready, string Reason)
{
    internal static FinalVideoVersioningState FromFlag(string? value, int activeCount)
        => activeCount != 1 ? new(false, "final_versioning_setting_missing_or_ambiguous")
            : value is not null && (value.Equals("true", StringComparison.OrdinalIgnoreCase)
                || value.Equals("1", StringComparison.OrdinalIgnoreCase) || value.Equals("yes", StringComparison.OrdinalIgnoreCase))
                ? new(true, "ready") : new(false, "final_versioning_required");

    internal static async Task<FinalVideoVersioningState> ReadAsync(TodoXConnectionFactory factory, CancellationToken ct)
    {
        try
        {
            using var connection = await factory.OpenAsync(ct);
            if (await connection.ExecuteScalarAsync<bool>(new CommandDefinition(SchemaReadySql, cancellationToken: ct)) != true)
                return new(false, "final_versioning_schema_not_ready");
            var values = (await connection.QueryAsync<string>(new CommandDefinition(
                "SELECT setting_value FROM system.app_settings WHERE setting_key=@key AND is_active=true;",
                new { key = SceneMediaVersioningFlags.FinalVideos }, cancellationToken: ct))).ToArray();
            return FromFlag(values.FirstOrDefault(), values.Length);
        }
        catch (Npgsql.NpgsqlException) { return new(false, "final_versioning_unavailable"); }
    }

    // Catalog-only: safe even when the feature tables/columns have not been installed.
    // render_job_id -> render.render_jobs is an optional DDL integrity constraint, not a
    // prerequisite of final composition/selection. Match required FK endpoints/keys, not counts/names.
    internal const string SchemaReadySql = """
        WITH required(table_name, columns) AS (VALUES
          ('system.app_settings','setting_key,setting_value,is_active'),
          ('video_render.video_projects','id,tenant_id,selected_final_video_version_id,final_video_url,final_video_path,status,error_message,updated_at'),
          ('video_render.video_project_scenes','id,project_id,tenant_id,scene_index,duration_seconds,selected_video_version_id'),
          ('video_render.scene_video_versions','id,project_id,scene_id,tenant_id,status'),
          ('video_render.final_video_versions','id,project_id,tenant_id,customer_id,created_by,version_number,logical_request_id,render_job_id,composition_config_json,transition_config_json,audio_config_json,subtitle_config_json,storage_key,status,created_at,updated_at,is_selected,selected_at,selected_by,completed_at,public_url,source_file_path,poster_url,duration_seconds,mime_type,error_code,error_message'),
          ('video_render.final_video_version_items','id,final_video_version_id,scene_id,scene_video_version_id,item_order,config_json')),
        missing AS (
          SELECT r.table_name,c.name FROM required r CROSS JOIN LATERAL unnest(string_to_array(r.columns,',')) c(name)
          WHERE NOT EXISTS (SELECT 1 FROM pg_attribute a WHERE a.attrelid=to_regclass(r.table_name) AND a.attname=c.name AND a.attnum>0 AND NOT a.attisdropped)),
        indexes(table_name,index_name,key_columns,partial) AS (VALUES
          ('video_render.final_video_versions','ux_final_video_one_selected','project_id',true),
          ('video_render.final_video_versions','ux_final_video_version_number','project_id,version_number',false),
          ('video_render.final_video_versions','ux_final_video_logical_request','logical_request_id',false),
          ('video_render.final_video_version_items','ux_final_video_item_order','final_video_version_id,item_order',false),
          ('video_render.final_video_version_items','ux_final_video_item_scene','final_video_version_id,scene_id',false)),
        missing_indexes AS (
          SELECT x.index_name FROM indexes x WHERE NOT EXISTS (
            SELECT 1 FROM pg_index i JOIN pg_class c ON c.oid=i.indexrelid
            WHERE i.indrelid=to_regclass(x.table_name) AND c.relname=x.index_name AND i.indisunique AND i.indisvalid
              AND (SELECT string_agg(a.attname,',' ORDER BY k.ordinality)
                FROM unnest(i.indkey) WITH ORDINALITY k(attnum,ordinality)
                JOIN pg_attribute a ON a.attrelid=i.indrelid AND a.attnum=k.attnum)=x.key_columns
              AND CASE WHEN x.partial THEN pg_get_expr(i.indpred,i.indrelid)='is_selected' ELSE i.indpred IS NULL END)),
        required_fks(source_table,source_columns,target_table,target_columns) AS (VALUES
          ('video_render.video_projects','selected_final_video_version_id','video_render.final_video_versions','id'),
          ('video_render.final_video_versions','project_id','video_render.video_projects','id'),
          ('video_render.final_video_version_items','final_video_version_id','video_render.final_video_versions','id'),
          ('video_render.final_video_version_items','scene_id','video_render.video_project_scenes','id'),
          ('video_render.final_video_version_items','scene_video_version_id','video_render.scene_video_versions','id')),
        missing_fks AS (
          SELECT r.* FROM required_fks r WHERE NOT EXISTS (
            SELECT 1 FROM pg_constraint fk
            WHERE fk.contype='f' AND fk.convalidated
              AND fk.conrelid=to_regclass(r.source_table) AND fk.confrelid=to_regclass(r.target_table)
              AND fk.conkey=ARRAY(SELECT a.attnum FROM unnest(string_to_array(r.source_columns,',')) WITH ORDINALITY c(name,position)
                JOIN pg_attribute a ON a.attrelid=fk.conrelid AND a.attname=c.name AND a.attnum>0 AND NOT a.attisdropped ORDER BY c.position)
              AND fk.confkey=ARRAY(SELECT a.attnum FROM unnest(string_to_array(r.target_columns,',')) WITH ORDINALITY c(name,position)
                JOIN pg_attribute a ON a.attrelid=fk.confrelid AND a.attname=c.name AND a.attnum>0 AND NOT a.attisdropped ORDER BY c.position)))
        SELECT NOT EXISTS(SELECT 1 FROM missing) AND NOT EXISTS(SELECT 1 FROM missing_indexes) AND NOT EXISTS(SELECT 1 FROM missing_fks);
        """;
}
