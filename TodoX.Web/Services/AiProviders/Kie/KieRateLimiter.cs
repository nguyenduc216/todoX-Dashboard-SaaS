using Microsoft.Extensions.Options;

namespace TodoX.Web.Services.AiProviders.Kie;

public sealed record KieRateLimitDecision(bool Allowed, TimeSpan RetryAfter, string Reason)
{
    public static KieRateLimitDecision Allow() => new(true, TimeSpan.Zero, "allowed");
    public static KieRateLimitDecision Deny(TimeSpan retryAfter, string reason) => new(false, retryAfter, reason);
}

public interface IKieRateLimiter
{
    Task<KieRateLimitDecision> AcquireSubmitPermitAsync(string accountKey, CancellationToken cancellationToken);
}

public sealed class InMemoryKieRateLimiter : IKieRateLimiter
{
    private readonly IOptionsMonitor<KieOptions> _options;
    private readonly object _gate = new();
    private readonly Dictionary<string, Queue<DateTimeOffset>> _submitWindows = new(StringComparer.OrdinalIgnoreCase);

    public InMemoryKieRateLimiter(IOptionsMonitor<KieOptions> options)
    {
        _options = options;
    }

    public Task<KieRateLimitDecision> AcquireSubmitPermitAsync(string accountKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = string.IsNullOrWhiteSpace(accountKey) ? "default" : accountKey.Trim();
        var limit = Math.Max(1, _options.CurrentValue.RateLimitRequestsPer10S);
        var now = DateTimeOffset.UtcNow;
        var windowStart = now.AddSeconds(-10);

        lock (_gate)
        {
            if (!_submitWindows.TryGetValue(key, out var window))
            {
                window = new Queue<DateTimeOffset>();
                _submitWindows[key] = window;
            }

            while (window.Count > 0 && window.Peek() <= windowStart)
            {
                window.Dequeue();
            }

            if (window.Count >= limit)
            {
                var retryAfter = window.Peek().AddSeconds(10) - now;
                return Task.FromResult(KieRateLimitDecision.Deny(
                    retryAfter <= TimeSpan.Zero ? TimeSpan.FromSeconds(1) : retryAfter,
                    "submit_window_exhausted"));
            }

            window.Enqueue(now);
            return Task.FromResult(KieRateLimitDecision.Allow());
        }
    }
}
