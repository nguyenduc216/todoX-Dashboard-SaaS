using TodoX.Web.Models;

namespace TodoX.Web.Services;

public sealed class AuthStateService
{
    public CurrentUserSession? CurrentUser { get; private set; }
    public bool IsAuthenticated => CurrentUser?.IsAuthenticated == true;
    public event Action? OnChange;

    public void SignIn(CurrentUserSession user)
    {
        CurrentUser = user;
        NotifyStateChanged();
    }

    public void SignOut()
    {
        CurrentUser = null;
        NotifyStateChanged();
    }

    private void NotifyStateChanged() => OnChange?.Invoke();
}
