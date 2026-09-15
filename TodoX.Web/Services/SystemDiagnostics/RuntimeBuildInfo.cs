using System.Diagnostics;
using System.Reflection;

namespace TodoX.Web.Services.SystemDiagnostics;

public interface IRuntimeBuildInfoProvider
{
    RuntimeBuildInfo Get();
}

public sealed class RuntimeBuildInfoProvider : IRuntimeBuildInfoProvider
{
    private readonly IWebHostEnvironment _environment;
    private readonly DateTimeOffset _processStartTimeUtc;

    public RuntimeBuildInfoProvider(IWebHostEnvironment environment)
    {
        _environment = environment;
        _processStartTimeUtc = DateTimeOffset.UtcNow;
        try
        {
            _processStartTimeUtc = Process.GetCurrentProcess().StartTime.ToUniversalTime();
        }
        catch
        {
            // Some hosts can deny process metadata. Keep diagnostics available.
        }
    }

    public RuntimeBuildInfo Get()
    {
        var assembly = typeof(Program).Assembly;
        var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
        var plus = informationalVersion.IndexOf('+');
        var version = plus >= 0 ? informationalVersion[..plus] : informationalVersion;
        var metadata = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Last().Value, StringComparer.OrdinalIgnoreCase);

        string Metadata(string key) =>
            metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : "unknown";

        var commit = Metadata("BuildCommit");
        return new RuntimeBuildInfo
        {
            ApplicationName = "todoX Dashboard SaaS",
            Environment = _environment.EnvironmentName,
            Branch = Metadata("BuildBranch"),
            CommitSha = commit,
            CommitShortSha = ShortSha(commit),
            CommitMessage = Metadata("BuildCommitMessage"),
            BuildTimeUtc = Metadata("BuildTimeUtc"),
            PublishTimeUtc = Metadata("PublishTimeUtc"),
            AssemblyVersion = assembly.GetName().Version?.ToString() ?? "unknown",
            InformationalVersion = informationalVersion,
            Version = version,
            RuntimeVersion = Environment.Version.ToString(),
            MachineName = Environment.MachineName,
            ProcessStartTimeUtc = _processStartTimeUtc.ToString("o")
        };
    }

    private static string ShortSha(string commit)
        => string.IsNullOrWhiteSpace(commit) || commit.Equals("unknown", StringComparison.OrdinalIgnoreCase)
            ? "unknown"
            : commit[..Math.Min(12, commit.Length)];
}

public sealed class RuntimeBuildInfo
{
    public string ApplicationName { get; init; } = string.Empty;
    public string Environment { get; init; } = string.Empty;
    public string Branch { get; init; } = string.Empty;
    public string CommitSha { get; init; } = string.Empty;
    public string CommitShortSha { get; init; } = string.Empty;
    public string CommitMessage { get; init; } = string.Empty;
    public string BuildTimeUtc { get; init; } = string.Empty;
    public string PublishTimeUtc { get; init; } = string.Empty;
    public string AssemblyVersion { get; init; } = string.Empty;
    public string InformationalVersion { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string RuntimeVersion { get; init; } = string.Empty;
    public string MachineName { get; init; } = string.Empty;
    public string ProcessStartTimeUtc { get; init; } = string.Empty;
}
