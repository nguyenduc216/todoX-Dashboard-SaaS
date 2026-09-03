namespace TodoX.Web.Services;

public interface IPointBalanceChangeNotifier
{
    event Action<Guid>? Changed;
    void NotifyChanged(Guid customerId);
}

public sealed class PointBalanceChangeNotifier : IPointBalanceChangeNotifier
{
    private readonly ILogger<PointBalanceChangeNotifier> _logger;

    public PointBalanceChangeNotifier(ILogger<PointBalanceChangeNotifier> logger)
    {
        _logger = logger;
    }

    public event Action<Guid>? Changed;

    public void NotifyChanged(Guid customerId)
    {
        foreach (var handler in Changed?.GetInvocationList() ?? Array.Empty<Delegate>())
        {
            try
            {
                handler.DynamicInvoke(customerId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Point balance subscriber failed for customer {CustomerId}.", customerId);
            }
        }
    }
}
