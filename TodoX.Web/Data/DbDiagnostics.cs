using Microsoft.Extensions.Logging;
using Npgsql;

namespace TodoX.Web.Data;

/// <summary>
/// Shared helpers for diagnosing PostgreSQL write failures and defensively clipping string values
/// to their column limits. Never logs secrets (API keys, tokens) â€” only lengths and DB metadata.
/// </summary>
public static class DbDiagnostics
{
    /// <summary>
    /// Known max lengths for the string columns we write. Used to clip values before insert/update
    /// and to suggest ALTER COLUMN when a value legitimately needs more room.
    /// Keyed as "table.column".
    /// </summary>
    private static readonly IReadOnlyDictionary<string, int> ColumnMaxLengths = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        // todox_ai_provider_usage_log
        ["todox_ai_provider_usage_log.provider_code"] = 100,
        ["todox_ai_provider_usage_log.capability_code"] = 100,
        ["todox_ai_provider_usage_log.feature_code"] = 100,
        ["todox_ai_provider_usage_log.model_name"] = 255,
        ["todox_ai_provider_usage_log.request_id"] = 100,
        ["todox_ai_provider_usage_log.job_id"] = 100,
        ["todox_ai_provider_usage_log.unit_type"] = 50,
        ["todox_ai_provider_usage_log.status"] = 50,

        // todox_ai_provider_capability
        ["todox_ai_provider_capability.provider_code"] = 100,
        ["todox_ai_provider_capability.capability_code"] = 100,
        ["todox_ai_provider_capability.display_name"] = 255,
        ["todox_ai_provider_capability.model_name"] = 255,
        ["todox_ai_provider_capability.unit_type"] = 50,

        // todox_ai_character
        ["todox_ai_character.character_code"] = 50,
        ["todox_ai_character.provider_code"] = 50,
        ["todox_ai_character.model_name"] = 255,
        ["todox_ai_character.status"] = 50,

        // auth.user_avatar_renders
        ["auth.user_avatar_renders.model"] = 50,
        ["auth.user_avatar_renders.status"] = 50,
        ["auth.user_avatar_renders.prompt_input"] = 255,
        ["auth.user_avatar_renders.prompt_used"] = 255,

        // todox_ai_character_render
        ["todox_ai_character_render.render_code"] = 50,
        ["todox_ai_character_render.provider_code"] = 50,
        ["todox_ai_character_render.model_name"] = 255,
        ["todox_ai_character_render.output_format"] = 50,
        ["todox_ai_character_render.quality"] = 50,
        ["todox_ai_character_render.resolution"] = 50,
        ["todox_ai_character_render.status"] = 50
    };

    /// <summary>Clips a value to the known max length for "table.column", logging a warning if it had to trim.</summary>
    public static string? Clip(ILogger logger, string table, string column, string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        if (!ColumnMaxLengths.TryGetValue($"{table}.{column}", out var max) || value.Length <= max)
        {
            return value;
        }

        logger.LogWarning(
            "DB_VALUE_CLIPPED table={Table} column={Column} originalLength={OriginalLength} maxLength={MaxLength}. " +
            "Consider: ALTER TABLE public.{Table} ALTER COLUMN {Column} TYPE varchar({Suggested}); (or TEXT).",
            table, column, value.Length, max, table, column, Math.Max(255, max));
        return value[..max];
    }

    /// <summary>Logs the lengths of the given "field=value" pairs (values are never echoed in full â€” only lengths).</summary>
    public static void LogFieldLengths(ILogger logger, string operation, params (string Field, string? Value)[] fields)
    {
        var summary = string.Join(", ", fields.Select(f => $"{f.Field}={(f.Value?.Length ?? 0)}"));
        logger.LogInformation("DB_FIELD_LENGTHS op={Operation} {Lengths}", operation, summary);
    }

    /// <summary>
    /// If the exception is (or wraps) a PostgresException, logs the full DB diagnostics
    /// (SqlState, table, column, constraint, message, detail, hint) and returns true.
    /// Does not log any API key or parameter values.
    /// </summary>
    public static bool LogPostgresException(ILogger logger, Exception ex, string operation)
    {
        var pg = ex as PostgresException ?? ex.InnerException as PostgresException;
        if (pg is null) return false;

        logger.LogError(pg,
            "DB_WRITE_FAILED op={Operation} sqlState={SqlState} table={TableName} column={ColumnName} " +
            "constraint={ConstraintName} message={MessageText} detail={Detail} hint={Hint}",
            operation, pg.SqlState, pg.TableName, pg.ColumnName,
            pg.ConstraintName, pg.MessageText, pg.Detail, pg.Hint);
        return true;
    }
}
