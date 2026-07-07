namespace TodoX.Web.Services.Reup;

public sealed class ReupTaskPageGate
{
    private readonly HashSet<Guid> _pages = new();
    private readonly object _sync = new();

    public bool TryEnter(Guid pageId)
    {
        lock (_sync)
        {
            return _pages.Add(pageId);
        }
    }

    public void Exit(Guid pageId)
    {
        lock (_sync)
        {
            _pages.Remove(pageId);
        }
    }
}
