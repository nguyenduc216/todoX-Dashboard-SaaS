using Dapper;
using Npgsql;
using TodoX.Web.Data;
using TodoX.Web.Models;
using TodoX.Web.Services.DanceSell;
using TodoX.Web.Services.VideoRender;

namespace TodoX.Web.Services.SystemDiagnostics;

public interface ISystemDiagnosticsService
{
    RuntimeBuildInfo GetBuildInfo();
    Task<SystemDiagnosticsSnapshot> RunFullDiagnosticAsync(CancellationToken ct = default);
    Task<RDanceJobDiagnosticResult> DiagnoseRDanceJobAsync(Guid jobId, CurrentUserSession user, CancellationToken ct = default);
}

public sealed class SystemDiagnosticsService : ISystemDiagnosticsService
{
    public static readonly string[] RequiredRDanceColumns =
    [
        "id",
        "tenant_id",
        "customer_id",
        "user_id",
        "render_job_id",
        "orientation",
        "character_orientation",
        "request_json",
        "result_video_url"
    ];

    private readonly IRuntimeBuildInfoProvider _buildInfo;
    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;
    private readonly IDanceSellPhase2Service _danceSell;
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _services;

    public SystemDiagnosticsService(
        IRuntimeBuildInfoProvider buildInfo,
        TodoXConnectionFactory factory,
        TenantContext tenant,
        IDanceSellPhase2Service danceSell,
        IConfiguration configuration,
        IServiceProvider services)
    {
        _buildInfo = buildInfo;
        _factory = factory;
        _tenant = tenant;
        _danceSell = danceSell;
        _configuration = configuration;
        _services = services;
    }

    public RuntimeBuildInfo GetBuildInfo() => _buildInfo.Get();

    public async Task<SystemDiagnosticsSnapshot> RunFullDiagnosticAsync(CancellationToken ct = default)
    {
        var database = await DiagnoseDatabaseAsync(ct);
        var checks = new List<DiagnosticCheckResult>
        {
            new()
            {
                Name = "Application",
                Status = string.Equals(GetBuildInfo().CommitSha, "unknown", StringComparison.OrdinalIgnoreCase)
                    ? DiagnosticStatus.Warning
                    : DiagnosticStatus.Pass,
                Message = string.Equals(GetBuildInfo().CommitSha, "unknown", StringComparison.OrdinalIgnoreCase)
                    ? "Build commit metadata is unknown."
                    : "Build commit metadata is available."
            },
            new()
            {
                Name = "Database",
                Status = database.Status,
                Message = database.Message
            },
            new()
            {
                Name = "Tenant",
                Status = database.TenantId.HasValue ? DiagnosticStatus.Pass : DiagnosticStatus.Fail,
                Message = database.TenantId.HasValue ? $"Tenant {database.TenantCode} resolved." : "Tenant was not resolved."
            },
            new()
            {
                Name = "RDance schema",
                Status = database.RDanceColumns.Any(x => x.ColumnName == "orientation" && x.Exists)
                    ? DiagnosticStatus.Pass
                    : DiagnosticStatus.Fail,
                Message = database.RDanceColumns.Any(x => x.ColumnName == "orientation" && x.Exists)
                    ? "dance_sell.dance_sell_jobs has orientation."
                    : "dance_sell.dance_sell_jobs is missing orientation."
            },
            new()
            {
                Name = "RDance repository",
                Status = DiagnosticStatus.Pass,
                Message = "Runtime diagnostic job loads through DanceSell.GetAsync(jobId, currentUser)."
            },
            new()
            {
                Name = "Provider configuration",
                Status = HasAnyProviderConfiguration() ? DiagnosticStatus.Pass : DiagnosticStatus.Warning,
                Message = HasAnyProviderConfiguration()
                    ? "Provider configuration sections are present."
                    : "Provider configuration sections were not detected."
            }
        };

        return new SystemDiagnosticsSnapshot
        {
            BuildInfo = GetBuildInfo(),
            Database = database,
            Checks = checks,
            CheckedAtUtc = DateTimeOffset.UtcNow
        };
    }

    public async Task<RDanceJobDiagnosticResult> DiagnoseRDanceJobAsync(Guid jobId, CurrentUserSession user, CancellationToken ct = default)
    {
        try
        {
            var job = await _danceSell.GetAsync(jobId, user, ct);
            return new RDanceJobDiagnosticResult
            {
                Status = DiagnosticStatus.Pass,
                Message = "Job loaded through DanceSell.GetAsync.",
                JobId = jobId,
                Job = job
            };
        }
        catch (Exception ex)
        {
            return new RDanceJobDiagnosticResult
            {
                Status = DiagnosticStatus.Fail,
                Message = ex.Message,
                JobId = jobId,
                Exception = ExceptionDiagnosticFactory.Create(ex, "DanceSell.GetAsync -> DanceSellRepository.GetByIdAsync")
            };
        }
    }

    private async Task<DatabaseDiagnosticResult> DiagnoseDatabaseAsync(CancellationToken ct)
    {
        try
        {
            await _tenant.EnsureLoadedAsync(ct);
            using var conn = await _factory.OpenAsync(ct);
            var serverVersion = await conn.QuerySingleAsync<string>("SELECT version();");
            var existingColumns = (await conn.QueryAsync<string>(
                    """
                    SELECT column_name
                    FROM information_schema.columns
                    WHERE table_schema = 'dance_sell'
                      AND table_name = 'dance_sell_jobs'
                      AND column_name = ANY(@columns)
                    ORDER BY column_name;
                    """,
                    new { columns = RequiredRDanceColumns }))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var columns = RequiredRDanceColumns
                .Select(column => new RDanceSchemaColumnCheck
                {
                    ColumnName = column,
                    Exists = existingColumns.Contains(column)
                })
                .ToArray();

            var missingRequired = columns.Where(x => !x.Exists && x.ColumnName != "character_orientation").ToArray();
            var status = missingRequired.Length == 0 ? DiagnosticStatus.Pass : DiagnosticStatus.Fail;
            var message = missingRequired.Length == 0
                ? "Database connected and RDance required columns are available."
                : "Database connected but RDance required columns are missing.";

            return new DatabaseDiagnosticResult
            {
                Status = status,
                Message = message,
                ServerVersion = serverVersion,
                TenantId = _tenant.TenantId,
                TenantCode = _tenant.TenantCode,
                RDanceColumns = columns
            };
        }
        catch (Exception ex)
        {
            return new DatabaseDiagnosticResult
            {
                Status = DiagnosticStatus.Fail,
                Message = ex.Message,
                TenantCode = _tenant.TenantCode,
                Exception = ExceptionDiagnosticFactory.Create(ex, "SystemDiagnosticsService.DiagnoseDatabaseAsync")
            };
        }
    }

    private bool HasAnyProviderConfiguration()
        => _configuration.GetSection("Kie").Exists()
           || _configuration.GetSection("Ai79").Exists()
           || _configuration.GetSection("YEScale").Exists()
           || _services.GetServices<IHostedService>().OfType<RVideoLifecycleWorker>().Any();
}

public static class ExceptionDiagnosticFactory
{
    public static ExceptionDiagnostic Create(Exception ex, string? repositoryOrMethod = null)
    {
        var postgres = FindPostgresException(ex);
        return new ExceptionDiagnostic
        {
            Type = ex.GetType().FullName ?? ex.GetType().Name,
            SqlState = postgres?.SqlState,
            Message = ex.Message,
            RepositoryOrMethod = repositoryOrMethod,
            InnerException = ex.InnerException?.ToString(),
            StackTrace = ex.ToString()
        };
    }

    private static PostgresException? FindPostgresException(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres)
            {
                return postgres;
            }
        }

        return null;
    }
}
