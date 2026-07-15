namespace TodoX.Web.Services.AiProviders;

public enum YEScaleBalanceStatus
{
    Available,
    Unavailable,
    NotSupported
}

public sealed class YEScaleAccountSnapshot
{
    public YEScaleBalanceStatus Status { get; init; }
    public decimal? BalanceUsd { get; init; }
    public decimal? BalanceVnd { get; init; }
    public decimal? EquivalentTodoXPoints { get; init; }
    public DateTimeOffset QueriedAt { get; init; }
    public string? Message { get; init; }
}

public interface IYEScaleAccountService
{
    Task<YEScaleAccountSnapshot> GetSnapshotAsync(bool forceRefresh = false, CancellationToken ct = default);
}

public sealed class YEScaleAccountService : IYEScaleAccountService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan RefreshRateLimit = TimeSpan.FromSeconds(30);

    private readonly object _gate = new();
    private YEScaleAccountSnapshot? _cached;
    private DateTimeOffset _lastRefreshAttempt = DateTimeOffset.MinValue;

    public Task<YEScaleAccountSnapshot> GetSnapshotAsync(bool forceRefresh = false, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            if (!forceRefresh && _cached is not null && now - _cached.QueriedAt < CacheDuration)
            {
                return Task.FromResult(_cached);
            }

            if (forceRefresh && now - _lastRefreshAttempt < RefreshRateLimit && _cached is not null)
            {
                return Task.FromResult(_cached);
            }

            _lastRefreshAttempt = now;
            _cached = new YEScaleAccountSnapshot
            {
                Status = YEScaleBalanceStatus.NotSupported,
                QueriedAt = now,
                Message = "YEScale balance API is not verified for the application access key. Use internal billing reconciliation until an official endpoint is confirmed."
            };
            return Task.FromResult(_cached);
        }
    }
}
