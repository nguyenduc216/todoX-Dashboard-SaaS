namespace TodoX.Web.Services;

public interface IPointBalanceChangeNotifier
{
    event Action<Guid>? Changed;
    void NotifyChanged(Guid customerId);
}

public sealed class PointBalanceChangeNotifier : IPointBalanceChangeNotifier
{
    public event Action<Guid>? Changed;

    public void NotifyChanged(Guid customerId)
    {
        foreach (var handler in Changed?.GetInvocationList() ?? Array.Empty<Delegate>())
        {
            try
            {
                handler.DynamicInvoke(customerId);
            }
            catch
            {
                // A subscriber must not be able to terminate the caller's request or circuit.
            }
        }
    }
}
