namespace TodoX.Web.Services;

public interface IPointBalanceChangeNotifier
{
    event Action<Guid>? Changed;
    void NotifyChanged(Guid customerId);
}

public sealed class PointBalanceChangeNotifier : IPointBalanceChangeNotifier
{
    public event Action<Guid>? Changed;

    public void NotifyChanged(Guid customerId) => Changed?.Invoke(customerId);
}
