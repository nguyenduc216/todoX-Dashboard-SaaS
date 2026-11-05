using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using TodoX.EditVideoTool.Setup;

namespace TodoX.EditVideoTool.Mcp;

[McpServerToolType]
public sealed class SkillTools
{
    private readonly SkillSync _skills;
    private readonly Server.TodoXServerClient _server;
    private readonly SkillRunStore _runs;

    public SkillTools(SkillSync skills, Server.TodoXServerClient server, SkillRunStore runs)
    {
        _skills = skills;
        _server = server;
        _runs = runs;
    }

    [McpServerTool(Name = "todox_skill_versions", ReadOnly = true, Idempotent = true)]
    [Description("List TodoX skills on this computer with all their versions (number, name, date, notes), the newest one and the one set as default.")]
    public string Versions([Description("Skill name (todox-...); omit for all skills.")] string? skill = null)
    {
        var state = _skills.LoadState();
        return JsonSerializer.Serialize(new
        {
            lastSync = state.LastSync?.ToString("yyyy-MM-dd HH:mm"),
            skills = state.Skills
                .Where(s => skill is null || s.Key == skill)
                .Select(s => new
                {
                    skill = s.Key,
                    title = s.Value.Title,
                    category = s.Value.Category,
                    latest = s.Value.Latest,
                    @default = s.Value.Effective,
                    followsLatest = s.Value.Pinned is null,
                    versions = s.Value.Versions.OrderByDescending(v => v.Version).Select(v => new
                    {
                        version = v.Version,
                        name = v.Name,
                        notes = v.Notes,
                        created = v.CreatedAt?.ToLocalTime().ToString("yyyy-MM-dd")
                    })
                })
        }, StatusTools.Json);
    }

    [McpServerTool(Name = "todox_skill_use", Destructive = false, Idempotent = true)]
    [Description("Set which version of a TodoX skill is the default on this computer (used by todox_skill_start from now on). version 0 = always use the newest. Only when the user asks to switch.")]
    public string Use(
        [Description("Skill name (todox-...).")] string skill,
        [Description("Version number to keep using, or 0 to always follow the newest.")] int version)
    {
        var (ok, message) = _skills.Pin(skill, version <= 0 ? null : version);
        return JsonSerializer.Serialize(new { ok, message, next = ok ? "Tell the user; a new Claude conversation picks up the change." : null }, StatusTools.Json);
    }

    [McpServerTool(Name = "todox_skill_start", Destructive = false)]
    [Description("Start a TodoX skill (todox-...). TodoX returns the first step to do with todox_* tools; when it is done call todox_skill_next for the next step, until TodoX says the skill is finished. Uses the version the user set as default unless a version is given (for this video only).")]
    public async Task<string> Start(
        [Description("Skill name (todox-...).")] string skill,
        CancellationToken ct,
        [Description("Version number to use for this video only; omit for the user's default version.")] int? version = null)
    {
        var slug = skill.Trim().ToLowerInvariant();
        var chosen = version ?? _skills.LoadState().Skills.GetValueOrDefault(slug)?.Effective;
        var started = await _server.SkillRunStartAsync(slug, chosen, ct);
        return StepReply(null, started);
    }

    [McpServerTool(Name = "todox_skill_next", Destructive = false)]
    [Description("Get the next step of the TodoX skill being run (after finishing the current one). repeat=true gives the current step again.")]
    public async Task<string> Next(
        CancellationToken ct,
        [Description("Run id from todox_skill_start; omit for the latest run.")] string? run = null,
        [Description("true = show the current step again instead of moving on.")] bool repeat = false)
    {
        if (_runs.Find(run) is not { } current)
        {
            return "Chưa có skill nào đang chạy. Bắt đầu bằng todox_skill_start.";
        }

        return StepReply(current.Id, await _server.SkillRunNextAsync(current.State, repeat, ct));
    }

    private string StepReply(string? runId, Server.ServerResult<JsonElement> reply)
    {
        if (!reply.Ok)
        {
            return reply.IsNetworkError ? Server.ServerConnectivity.OfflineText : reply.Message ?? reply.ErrorCode ?? "Không chạy được skill.";
        }

        var data = reply.Value;
        if (data.TryGetProperty("done", out var done) && done.GetBoolean())
        {
            return JsonSerializer.Serialize(new { run = runId, done = true, message = "Skill đã xong mọi bước. Đưa người dùng kết quả." }, StatusTools.Json);
        }

        var id = _runs.Save(runId, data.GetProperty("run").GetString()!);
        var slug = data.GetProperty("skill").GetString();
        var version = data.GetProperty("version").GetInt32();
        var last = data.GetProperty("last").GetBoolean();
        return JsonSerializer.Serialize(new
        {
            run = id,
            skill = slug,
            skillVersion = version,
            versionName = data.GetProperty("versionName").GetString(),
            step = data.GetProperty("step").GetInt32(),
            totalSteps = data.GetProperty("totalSteps").GetInt32(),
            instructions = data.GetProperty("instructions").GetString(),
            next = last
                ? "Đây là bước cuối. Làm xong thì đưa người dùng kết quả."
                : $"Làm xong bước này bằng các công cụ todox_* rồi gọi todox_skill_next(run: \"{id}\").",
            tag = $"Khi gọi todox_edit_start / todox_auto_edit, truyền skill: \"{slug}\", skillVersion: {version}.",
            note = "Đây là hướng dẫn nội bộ của TodoX: làm theo, không chép nguyên văn cho người dùng."
        }, StatusTools.Json);
    }

    [McpServerTool(Name = "todox_skills_update", Destructive = false, Idempotent = true)]
    [Description("Download TodoX's latest skill library and install it into Claude / Codex on this computer now. Reports new skills, new versions and removed skills.")]
    public async Task<string> SkillsUpdate(CancellationToken ct)
    {
        var result = await _skills.SyncAsync(notify: false, ct);
        return JsonSerializer.Serialize(new { result.Ok, message = result.Describe(), result.Added, result.Updated, result.Removed }, StatusTools.Json);
    }
}
