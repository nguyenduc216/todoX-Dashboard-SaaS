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

    public EditVideoResult<EditVideoSignedPlan> Create(PlanRequest? request)
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
                Title = Clip(request.Title, 80),
                Subtitle = Clip(request.Subtitle, 120),
                Script = Clip(request.Script, 6000),
                OutroText = Clip(request.OutroText, 120)
            });
        }
        catch (ArgumentException ex)
        {
            return EditVideoResult<EditVideoSignedPlan>.Fail(EditVideoErrorCodes.InvalidRequest, ex.Message);
        }

        var document = _signer.SignDocument(JsonSerializer.SerializeToUtf8Bytes(plan, Json));
        return EditVideoResult<EditVideoSignedPlan>.Success(new EditVideoSignedPlan(plan, document));
    }

    private static string? Clip(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length <= max ? value.Trim() : value.Trim()[..max];
}
