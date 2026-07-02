using TodoX.Web.Models;

namespace TodoX.Web.Services;

/// <summary>Shared Vietnamese labels for TodoX enums used across pages and dialogs.</summary>
public static class TodoXLabels
{
    public static string Status(TodoXAccountStatus status) => status switch
    {
        TodoXAccountStatus.Active => "Hoạt động",
        TodoXAccountStatus.Pending => "Chờ duyệt",
        TodoXAccountStatus.Locked => "Đã khóa",
        _ => status.ToString()
    };

    public static string Role(TodoXUserRole role) => role switch
    {
        TodoXUserRole.Admin => "Quản trị viên",
        TodoXUserRole.SystemOperator => "Vận hành hệ thống",
        TodoXUserRole.CustomerOwner => "Chủ tài khoản",
        TodoXUserRole.CustomerUser => "Người dùng",
        _ => role.ToString()
    };

    public static MudBlazor.Color StatusColor(TodoXAccountStatus status) => status switch
    {
        TodoXAccountStatus.Active => MudBlazor.Color.Success,
        TodoXAccountStatus.Pending => MudBlazor.Color.Warning,
        TodoXAccountStatus.Locked => MudBlazor.Color.Error,
        _ => MudBlazor.Color.Default
    };
}
