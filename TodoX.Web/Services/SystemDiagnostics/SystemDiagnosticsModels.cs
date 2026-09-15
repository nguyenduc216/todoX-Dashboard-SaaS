using TodoX.Web.Services.DanceSell;

namespace TodoX.Web.Services.SystemDiagnostics;

public sealed class SystemDiagnosticsSnapshot
{
    public RuntimeBuildInfo BuildInfo { get; init; } = new();
    public DatabaseDiagnosticResult Database { get; init; } = new();
    public IReadOnlyList<DiagnosticCheckResult> Checks { get; init; } = Array.Empty<DiagnosticCheckResult>();
    public DateTimeOffset CheckedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class DatabaseDiagnosticResult
{
    public DiagnosticStatus Status { get; init; } = DiagnosticStatus.Warning;
    public string Message { get; init; } = string.Empty;
    public string? ServerVersion { get; init; }
    public Guid? TenantId { get; init; }
    public string? TenantCode { get; init; }
    public IReadOnlyList<RDanceSchemaColumnCheck> RDanceColumns { get; init; } = Array.Empty<RDanceSchemaColumnCheck>();
    public ExceptionDiagnostic? Exception { get; init; }
}

public sealed class RDanceJobDiagnosticResult
{
    public DiagnosticStatus Status { get; init; } = DiagnosticStatus.Warning;
    public string Message { get; init; } = string.Empty;
    public Guid JobId { get; init; }
    public DanceSellJobDto? Job { get; init; }
    public ExceptionDiagnostic? Exception { get; init; }
    public DateTimeOffset CheckedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class RDanceSchemaColumnCheck
{
    public string ColumnName { get; init; } = string.Empty;
    public bool Exists { get; init; }
    public bool Required { get; init; } = true;
    public DiagnosticStatus Status => Exists || !Required ? DiagnosticStatus.Pass : DiagnosticStatus.Fail;
}

public sealed class DiagnosticCheckResult
{
    public string Name { get; init; } = string.Empty;
    public DiagnosticStatus Status { get; init; }
    public string Message { get; init; } = string.Empty;
    public DateTimeOffset CheckedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class ExceptionDiagnostic
{
    public string Type { get; init; } = string.Empty;
    public string? SqlState { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? RepositoryOrMethod { get; init; }
    public string? InnerException { get; init; }
    public string StackTrace { get; init; } = string.Empty;
}

public enum DiagnosticStatus
{
    Pass,
    Warning,
    Fail
}
