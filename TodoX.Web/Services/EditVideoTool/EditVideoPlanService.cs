using System.Text.Json;
using TodoX.EditVideo.Planning;

namespace TodoX.Web.Services.EditVideoTool;

public sealed record EditVideoSignedPlan(AutoEditPlan Plan, string Document);

/// <summary>
/// Builds the edit plan on the server (TodoX's editing know-how never ships in the exe) and signs it so the tool
/// only renders plans TodoX issued.
/// </summary>
public sealed class EditVideoPlanService
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly EditVideoLicenseSigner _signer;

    public EditVideoPlanService(EditVideoLicenseSigner signer)
    {
        _signer = signer;
    }

    /// <param name="skill">The skill version's fixed settings: they fill what the customer did not ask for.</param>
    public EditVideoResult<EditVideoSignedPlan> Create(PlanRequest? request, EditVideoSkillSettings? skill = null)
    {
        if (request?.Clips is not { Count: > 0 } clips || clips.Count > 300)
        {
            return EditVideoResult<EditVideoSignedPlan>.Fail(EditVideoErrorCodes.InvalidRequest, "Between 1 and 300 clips are required.");
        }

        if (clips.Any(c => string.IsNullOrWhiteSpace(c.Id) || c.Id.Length > 40 || c.DurationSeconds is < 0 or > 6 * 3600))
        {
            return EditVideoResult<EditVideoSignedPlan>.Fail(EditVideoErrorCodes.InvalidRequest, "Clip metadata is invalid.");
        }

        if (!_signer.IsAvailable)
        {
            return EditVideoResult<EditVideoSignedPlan>.Fail(EditVideoErrorCodes.SigningUnavailable, "Plan service is not configured.");
        }

        AutoEditPlan plan;
        try
        {
            plan = EditPlanner.Plan(request with
            {
                Aspect = request.Aspect ?? skill?.Aspect,
                TargetSeconds = request.TargetSeconds ?? skill?.TargetSeconds,
                StyleId = request.StyleId ?? skill?.StyleId,
                Title = Clip(request.Title, 80),
                Subtitle = Clip(request.Subtitle, 120),
                Script = Clip(request.Script, 6000),
                OutroText = Clip(request.OutroText, 120)
            });
            if (skill is not null)
            {
                plan = ApplySkillLook(plan, clips, skill);
            }
        }
        catch (ArgumentException ex)
        {
            return EditVideoResult<EditVideoSignedPlan>.Fail(EditVideoErrorCodes.InvalidRequest, ex.Message);
        }

        var document = _signer.SignDocument(JsonSerializer.SerializeToUtf8Bytes(plan, Json));
        return EditVideoResult<EditVideoSignedPlan>.Success(new EditVideoSignedPlan(plan, document));
    }

    /// <summary>One change (cut, arrange, captions…) to a plan this server signed earlier; returns the re-signed plan.</summary>
    public EditVideoResult<EditVideoSignedPlan> Edit(PlanEditRequest? request)
    {
        if (request?.Edit is null || request.Clips is not { Count: > 0 and <= 300 } clips
            || clips.Any(c => string.IsNullOrWhiteSpace(c.Id) || c.Id.Length > 40 || c.DurationSeconds is < 0 or > 6 * 3600))
        {
            return EditVideoResult<EditVideoSignedPlan>.Fail(EditVideoErrorCodes.InvalidRequest, "Plan, clips and edit are required.");
        }

        if (!_signer.IsAvailable)
        {
            return EditVideoResult<EditVideoSignedPlan>.Fail(EditVideoErrorCodes.SigningUnavailable, "Plan service is not configured.");
        }

        var body = _signer.VerifyBody(request.Document);
        var current = body is null ? null : JsonSerializer.Deserialize<AutoEditPlan>(body, Json);
        if (current is null)
        {
            return EditVideoResult<EditVideoSignedPlan>.Fail(EditVideoErrorCodes.InvalidRequest, "The plan was not issued by TodoX.");
        }

        AutoEditPlan plan;
        try
        {
            plan = PlanEditor.Apply(current, clips, request.Edit);
        }
        catch (ArgumentException ex)
        {
            return EditVideoResult<EditVideoSignedPlan>.Fail(EditVideoErrorCodes.InvalidRequest, ex.Message);
        }

        var document = _signer.SignDocument(JsonSerializer.SerializeToUtf8Bytes(plan, Json));
        return EditVideoResult<EditVideoSignedPlan>.Success(new EditVideoSignedPlan(plan, document));
    }

    /// <summary>Caption look, transition and music level from the skill, through the same checks as a manual edit.</summary>
    private static AutoEditPlan ApplySkillLook(AutoEditPlan plan, IReadOnlyList<ClipInput> clips, EditVideoSkillSettings skill)
    {
        if (skill.CaptionStyle is not null)
        {
            plan = PlanEditor.Apply(plan, clips, new PlanEdit("captions", CaptionStyle: skill.CaptionStyle));
        }

        if (skill.Transition is not null || skill.TransitionSeconds is not null)
        {
            plan = PlanEditor.Apply(plan, clips, new PlanEdit("arrange", Transition: skill.Transition, TransitionSeconds: skill.TransitionSeconds));
        }

        if (skill.MusicVolume is not null && plan.Music is not null)
        {
            plan = PlanEditor.Apply(plan, clips, new PlanEdit("audio", MusicVolume: skill.MusicVolume));
        }

        return plan;
    }

    private static string? Clip(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length <= max ? value.Trim() : value.Trim()[..max];
}
