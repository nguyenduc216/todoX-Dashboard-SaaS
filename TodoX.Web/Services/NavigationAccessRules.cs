using TodoX.Web.Models;

namespace TodoX.Web.Services;

public static class NavigationAccessRules
{
    public static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "/";
        }

        var normalized = path.Trim().Split('?', '#')[0].Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "/";
        }

        return normalized.Length > 1 ? normalized.TrimEnd('/') : normalized;
    }

    public static bool IsPublicPath(string? path)
    {
        path = NormalizePath(path);
        return path is "/access-denied"
            or "/privacy"
            or "/terms"
            or "/data-deletion"
            or "/avatar-builder"
            or "/auth/facebook/callback"
            or "/login"
            or "/register"
            or "/forgot-password";
    }

    public static bool IsAdminOnlyPath(string? path)
    {
        path = NormalizePath(path);
        if (path is "/" or "/access-denied")
        {
            return false;
        }

        return path.StartsWith("/admin/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/customers", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/customer-accounts", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/permissions", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/settings", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/activity-logs", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/wallets", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/services", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/render-jobs", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/render-job", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/landing/contacts", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/landing/industries", StringComparison.OrdinalIgnoreCase);
    }

    public static bool CanAccessPath(CurrentUserSession? user, string? path)
    {
        path = NormalizePath(path);
        if (IsPublicPath(path))
        {
            return true;
        }

        if (user?.IsAuthenticated != true)
        {
            return false;
        }

        if (user.IsCustomer && IsAdminOnlyPath(path))
        {
            return false;
        }

        return true;
    }
}
