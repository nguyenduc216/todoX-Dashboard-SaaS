using TodoX.Web.Models;
using TodoX.Web.Models.Landing;

namespace TodoX.Web.Services.Landing;

public static class LandingIntroVideoWriteAccess
{
    public static bool CanWrite(CurrentUserSession? user) => user is { IsAuthenticated: true, IsCustomer: false }
        && (user.IsRoot || user.Role is TodoXUserRole.Admin or TodoXUserRole.SystemOperator
            || user.Can(LandingIndustryPermissions.Update));

    public static void Demand(CurrentUserSession? user)
    {
        if (!CanWrite(user)) throw new UnauthorizedAccessException("Không có quyền cập nhật Video Intro.");
    }
}