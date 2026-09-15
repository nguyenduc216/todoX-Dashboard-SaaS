using TodoX.Web.Models;

namespace TodoX.Web.Components.Pages;

public enum HomeShellMode
{
    Loading,
    SignIn,
    Customer,
    Admin
}

public static class HomePageState
{
    public static HomeShellMode Resolve(CurrentUserSession? user, bool isInitialized)
        => !isInitialized
            ? HomeShellMode.Loading
            : user is null
                ? HomeShellMode.SignIn
                : user.IsCustomer
                    ? HomeShellMode.Customer
                    : HomeShellMode.Admin;
}
