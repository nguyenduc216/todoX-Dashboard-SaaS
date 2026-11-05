using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using TodoX.EditVideoTool.Setup;

namespace TodoX.EditVideoTool.Mcp;

[McpServerToolType]
public sealed class SkillTools
{
    private readonly SkillSync _skills;

    public SkillTools(SkillSync skills)
    {
        _skills = skills;
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
    [Description("Set which version of a TodoX skill is the default on this computer (installed into Claude / Codex from now on). version 0 = always use the newest. Only when the user asks to switch.")]
    public string Use(
        [Description("Skill name (todox-...).")] string skill,
        [Description("Version number to keep using, or 0 to always follow the newest.")] int version)
    {
        var (ok, message) = _skills.Pin(skill, version <= 0 ? null : version);
        return JsonSerializer.Serialize(new { ok, message, next = ok ? "Tell the user; a new Claude conversation picks up the change." : null }, StatusTools.Json);
    }

    [McpServerTool(Name = "todox_skill_get", ReadOnly = true, Idempotent = true)]
    [Description("Get the instructions of one specific version of a TodoX skill, to follow it for this video only (the default is not changed). Pass the same skill and skillVersion to todox_auto_edit.")]
    public string Get(
        [Description("Skill name (todox-...).")] string skill,
        [Description("Version number.")] int version) =>
        _skills.ReadVersion(skill, version) ?? $"Máy này không có {skill} v{version}. Dùng todox_skill_versions để xem các phiên bản có sẵn.";

    [McpServerTool(Name = "todox_skills_update", Destructive = false, Idempotent = true)]
    [Description("Download TodoX's latest skill library and install it into Claude / Codex on this computer now. Reports new skills, new versions and removed skills.")]
    public async Task<string> SkillsUpdate(CancellationToken ct)
    {
        var result = await _skills.SyncAsync(notify: false, ct);
        return JsonSerializer.Serialize(new { result.Ok, message = result.Describe(), result.Added, result.Updated, result.Removed }, StatusTools.Json);
    }
}
