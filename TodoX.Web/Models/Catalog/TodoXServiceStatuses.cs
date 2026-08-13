namespace TodoX.Web.Models.Catalog;

public static class TodoXServiceStatuses
{
    public const string Active = "active";
    public const string Inactive = "inactive";

    public static string Normalize(string? status)
    {
        var normalized = (status ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            Active or "enabled" => Active,
            Inactive or "disabled" => Inactive,
            _ => Inactive
        };
    }

    public static bool IsActive(string? status)
        => string.Equals(Normalize(status), Active, StringComparison.Ordinal);

    public static string LabelFor(string? status)
        => IsActive(status) ? "Hoạt động" : "Tạm ngưng";
}
