using TodoX.Web.Models;

namespace TodoX.Web.Services;

public static class AdminEndpointAuthorization
{
    public static RouteGroupBuilder RequireTodoXAdmin(this RouteGroupBuilder group)
    {
        group.AddEndpointFilter<RequireTodoXAdminFilter>();

        return group;
    }

    public static bool IsAdmin(CurrentUserSession? user)
        => user?.IsAuthenticated == true
           && (user.IsRoot || user.Role is TodoXUserRole.Admin or TodoXUserRole.SystemOperator);
}

internal sealed class RequireTodoXAdminFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var auth = context.HttpContext.RequestServices.GetRequiredService<AuthStateService>();
        var user = auth.CurrentUser;
        if (user?.IsAuthenticated != true)
        {
            return Results.Unauthorized();
        }

        return AdminEndpointAuthorization.IsAdmin(user)
            ? await next(context)
            : Results.StatusCode(StatusCodes.Status403Forbidden);
    }
}
