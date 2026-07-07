namespace TodoX.Web.Models;

public sealed class NavigationMenuGroupDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? IconKey { get; set; }
    public int SortOrder { get; set; }
    public bool DefaultExpanded { get; set; }
    public List<NavigationMenuItemDto> Items { get; set; } = new();
}

public sealed class NavigationMenuItemDto
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Href { get; set; } = string.Empty;
    public string? IconKey { get; set; }
    public string[] ModuleKeys { get; set; } = Array.Empty<string>();
    public string VisibilityPolicy { get; set; } = "any_module";
    public bool MatchAll { get; set; }
    public int SortOrder { get; set; }
}
