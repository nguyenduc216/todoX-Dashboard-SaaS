namespace TodoX.Web.Services;

public interface IPointBalanceChangeNotifier
{
    event Action? Changed;
    void NotifyChanged();
}

public sealed class PointBalanceChangeNotifier : IPointBalanceChangeNotifier
{
    public event Action? Changed;

    public void NotifyChanged() => Changed?.Invoke();
}
