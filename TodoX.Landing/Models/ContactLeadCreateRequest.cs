using System.Text.Json.Serialization;

namespace TodoX.Landing.Models;

public sealed class ContactLeadCreateRequest
{
    public string? FullName { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Company { get; set; }
    public string? Industry { get; set; }
    public string? Need { get; set; }
    public string? Message { get; set; }
    public string? SourceUrl { get; set; }
    public string? ReferrerUrl { get; set; }
    public string? UtmSource { get; set; }
    public string? UtmMedium { get; set; }
    public string? UtmCampaign { get; set; }
    public string? UtmContent { get; set; }
    public string? UtmTerm { get; set; }
    public bool ConsentAccepted { get; set; }
    public string? Website { get; set; }
}

public sealed class ContactLeadCreateResponse
{
    public bool Success { get; init; }
    public string? LeadCode { get; init; }
    public string Message { get; init; } = string.Empty;
}

public sealed class ContactLeadValidationResult
{
    public bool IsValid => Errors.Count == 0;
    public Dictionary<string, string[]> Errors { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void Add(string field, string error)
    {
        Errors[field] = Errors.TryGetValue(field, out var existing)
            ? existing.Append(error).ToArray()
            : [error];
    }
}

public sealed class NormalizedContactLead
{
    public string FullName { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
    public string? Email { get; init; }
    public string? Company { get; init; }
    public string? Industry { get; init; }
    public string? Need { get; init; }
    public string? Message { get; init; }
    public string? SourceUrl { get; init; }
    public string? ReferrerUrl { get; init; }
    public string? UtmSource { get; init; }
    public string? UtmMedium { get; init; }
    public string? UtmCampaign { get; init; }
    public string? UtmContent { get; init; }
    public string? UtmTerm { get; init; }
    public bool ConsentAccepted { get; init; }
}

public sealed class ContactLeadInsertContext
{
    public string RequestId { get; init; } = string.Empty;
    public string? IpAddress { get; init; }
    public string? UserAgent { get; init; }
}
