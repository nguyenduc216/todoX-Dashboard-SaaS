using System.Diagnostics;

namespace TodoX.Web.Services.Render;

/// <summary>
/// System-wide coordinator for Google Cloud / Vertex AI bulk image renders. Protects the shared
/// Google quota across every browser session and background job by:
///   1. Capping concurrent Vertex calls (default 3, mirroring Avatar Builder), and
///   2. Retrying HTTP 429 / RESOURCE_EXHAUSTED with bounded exponential backoff + jitter, while
///      serialising the backoff waits so only one scene waits on quota at a time.
/// Registered as a singleton so the limit is global, not per-circuit. Non-retryable failures
/// (validation / prompt / auth) are surfaced immediately without consuming retry budget.
/// </summary>
public sealed class GoogleVertexRateLimiter
{
    private readonly SemaphoreSlim _concurrency;
    private readonly SemaphoreSlim _backoffGate = new(1, 1);
    private readonly int _maxAttempts;
    private readonly IReadOnlyList<TimeSpan> _backoffSchedule;
    private readonly ILogger<GoogleVertexRateLimiter> _logger;

    public GoogleVertexRateLimiter(IConfiguration config, ILogger<GoogleVertexRateLimiter> logger)
    {
        _logger = logger;
        var maxConcurrency = Math.Clamp(config.GetValue("RenderQueue:VertexMaxConcurrency", 3), 1, 8);
        _concurrency = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        _maxAttempts = Math.Clamp(config.GetValue("RenderQueue:VertexMaxQuotaRetries", 3), 1, 6);

        // Base waits 10s, 20s, 40s (doubling) unless overridden. Jitter added per attempt.
        var baseSeconds = Math.Clamp(config.GetValue("RenderQueue:VertexRetryBaseSeconds", 10), 1, 120);
        var schedule = new List<TimeSpan>();
        for (var attempt = 0; attempt < _maxAttempts; attempt++)
        {
            schedule.Add(TimeSpan.FromSeconds(baseSeconds * Math.Pow(2, attempt)));
        }
        _backoffSchedule = schedule;
    }

    public int MaxConcurrency => _concurrency.CurrentCount;

    /// <summary>
    /// Runs <paramref name="renderAsync"/> under the shared concurrency cap. If it throws a quota
    /// error (429 / RESOURCE_EXHAUSTED) or returns a result whose quota-ness is reported via
    /// <paramref name="isQuotaError"/>, the call is retried with backoff up to the configured limit.
    /// <paramref name="onQuotaWait"/> is invoked before each backoff sleep so callers can surface a
    /// "waiting for quota" state to the UI.
    /// </summary>
    public async Task<T> ExecuteAsync<T>(
        Func<int, Task<T>> renderAsync,
        Func<T, bool> isQuotaError,
        Func<int, TimeSpan, Task>? onQuotaWait = null,
        string? context = null,
        CancellationToken ct = default)
    {
        await _concurrency.WaitAsync(ct);
        try
        {
            for (var attempt = 1; attempt <= _maxAttempts; attempt++)
            {
                var quota = false;
                T result = default!;
                Exception? quotaException = null;

                try
                {
                    result = await renderAsync(attempt);
                    quota = isQuotaError(result);
                }
                catch (Exception ex) when (IsQuotaException(ex))
                {
                    quota = true;
                    quotaException = ex;
                }

                if (!quota)
                {
                    return result;
                }

                if (attempt >= _maxAttempts)
                {
                    _logger.LogWarning(
                        "VERTEX_QUOTA_RETRY_EXHAUSTED context={Context} attempts={Attempts}",
                        context, _maxAttempts);
                    if (quotaException is not null) throw quotaException;
                    return result;
                }

                var wait = ComputeWait(attempt);
                _logger.LogWarning(
                    "VERTEX_QUOTA_BACKOFF context={Context} attempt={Attempt}/{MaxAttempts} waitSeconds={WaitSeconds}",
                    context, attempt, _maxAttempts, wait.TotalSeconds);

                if (onQuotaWait is not null)
                {
                    await onQuotaWait(attempt, wait);
                }

                // Serialise backoff waits so multiple queued scenes do not all hammer Vertex at once.
                await _backoffGate.WaitAsync(ct);
                var sw = Stopwatch.StartNew();
                try
                {
                    await Task.Delay(wait, ct);
                }
                finally
                {
                    sw.Stop();
                    _backoffGate.Release();
                }
            }

            // Unreachable: the loop either returns or throws above.
            throw new InvalidOperationException("Vertex rate limiter retry loop exited unexpectedly.");
        }
        finally
        {
            _concurrency.Release();
        }
    }

    private TimeSpan ComputeWait(int attempt)
    {
        var baseWait = _backoffSchedule[Math.Min(attempt - 1, _backoffSchedule.Count - 1)];
        // Full jitter in [0, 1s) to de-correlate retries across scenes.
        var jitterMs = Random.Shared.Next(0, 1000);
        return baseWait + TimeSpan.FromMilliseconds(jitterMs);
    }

    /// <summary>True when an exception represents a Google quota / rate-limit error (HTTP 429).</summary>
    public static bool IsQuotaException(Exception ex)
        => IsQuotaMessage(ex.Message)
           || (ex.InnerException is not null && IsQuotaMessage(ex.InnerException.Message));

    /// <summary>True when an error string looks like a Google 429 / RESOURCE_EXHAUSTED response.</summary>
    public static bool IsQuotaMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return false;
        return message.Contains("429", StringComparison.Ordinal)
            || message.Contains("RESOURCE_EXHAUSTED", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Too Many Requests", StringComparison.OrdinalIgnoreCase)
            || message.Contains("rate limit", StringComparison.OrdinalIgnoreCase)
            || message.Contains("quota", StringComparison.OrdinalIgnoreCase);
    }
}
