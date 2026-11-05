using System.Text.Json;

namespace TodoX.Web.Services.EditVideoTool;

/// <summary>Where a customer is in a skill run. Signed by the server, so the tool cannot jump or forge steps.</summary>
public sealed record EditVideoSkillRunState(string Skill, int Version, int Step, Guid LicenseId, DateTimeOffset ExpiresAt);

/// <summary>One step handed to Claude. <see cref="Run"/> is the signed state to send back for the next step.</summary>
public sealed record EditVideoSkillStep(
    string Run, string Skill, string Title, int Version, string VersionName, int Step, int TotalSteps, string Instructions, bool Last);

/// <summary>
/// Runs a TodoX skill step by step: the skill's content never leaves the server as a whole; Claude receives the
/// step it is on, and asks for the next one when done.
/// </summary>
public sealed class EditVideoSkillRunService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(12);

    private readonly EditVideoSkillService _skills;
    private readonly EditVideoLicenseSigner _signer;
    private readonly TimeProvider _clock;

    public EditVideoSkillRunService(EditVideoSkillService skills, EditVideoLicenseSigner signer, TimeProvider? clock = null)
    {
        _skills = skills;
        _signer = signer;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<EditVideoResult<EditVideoSkillStep>> StartAsync(string? skill, int? version, Guid licenseId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(skill))
        {
            return EditVideoResult<EditVideoSkillStep>.Fail(EditVideoErrorCodes.InvalidRequest, "Thiếu tên skill (todox-...).");
        }

        var found = await _skills.FindVersionAsync(skill, version, ct);
        if (found is null)
        {
            return EditVideoResult<EditVideoSkillStep>.Fail("not_found", version is null
                ? $"Không có skill \"{skill}\" đang hoạt động."
                : $"Skill \"{skill}\" không có phiên bản v{version}.");
        }

        return Step(EditVideoSkillService.NormalizeSlug(skill), found, 0, licenseId);
    }

    /// <summary>The step after the one in <paramref name="run"/>, or the same step again when <paramref name="repeat"/>.</summary>
    public async Task<EditVideoResult<EditVideoSkillStep>> NextAsync(string? run, bool repeat, Guid licenseId, CancellationToken ct = default)
    {
        var body = _signer.VerifyBody(run);
        var state = body is null ? null : JsonSerializer.Deserialize<EditVideoSkillRunState>(body, Json);
        if (state is null || state.LicenseId != licenseId)
        {
            return EditVideoResult<EditVideoSkillStep>.Fail(EditVideoErrorCodes.InvalidRequest, "Lượt chạy skill không hợp lệ. Bắt đầu lại bằng todox_skill_start.");
        }

        if (state.ExpiresAt < _clock.GetUtcNow())
        {
            return EditVideoResult<EditVideoSkillStep>.Fail(EditVideoErrorCodes.InvalidRequest, "Lượt chạy skill đã hết hạn. Bắt đầu lại bằng todox_skill_start.");
        }

        var found = await _skills.FindVersionAsync(state.Skill, state.Version, ct);
        if (found is null)
        {
            return EditVideoResult<EditVideoSkillStep>.Fail("not_found", $"Skill \"{state.Skill}\" v{state.Version} không còn.");
        }

        var steps = EditVideoSkillService.SplitSteps(found.Content);
        var index = repeat ? state.Step : state.Step + 1;
        if (index >= steps.Count)
        {
            return EditVideoResult<EditVideoSkillStep>.Fail("skill_done", "Đã xong mọi bước của skill này.");
        }

        return Step(state.Skill, found, index, licenseId);
    }

    private EditVideoResult<EditVideoSkillStep> Step(string slug, EditVideoSkillVersionRow version, int index, Guid licenseId)
    {
        if (!_signer.IsAvailable)
        {
            return EditVideoResult<EditVideoSkillStep>.Fail(EditVideoErrorCodes.SigningUnavailable, "Skill service is not configured.");
        }

        var steps = EditVideoSkillService.SplitSteps(version.Content);
        if (steps.Count == 0)
        {
            return EditVideoResult<EditVideoSkillStep>.Fail("not_found", "Skill này chưa có nội dung.");
        }

        var state = new EditVideoSkillRunState(slug, version.VersionNo, index, licenseId, _clock.GetUtcNow().Add(Lifetime));
        var run = _signer.SignDocument(JsonSerializer.SerializeToUtf8Bytes(state, Json));
        return EditVideoResult<EditVideoSkillStep>.Success(new EditVideoSkillStep(
            run, slug, version.Title, version.VersionNo, version.VersionName, index + 1, steps.Count,
            EditVideoSkillService.BuildStep(version.Title, version.VersionNo, version.VersionName, steps, index),
            index == steps.Count - 1));
    }
}
