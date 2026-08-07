namespace TodoX.SkillEndpoint;

public sealed class SkillEndpointOptions
{
    public const string SectionName = "SkillEndpoint";

    public string ApiKey { get; set; } = string.Empty;
    public string TodoXOperationsBaseUrl { get; set; } = string.Empty;
    public string? TodoXOperationsApiKey { get; set; }
    public string AuditLogPath { get; set; } = "logs/skill-audit.ndjson";
}
