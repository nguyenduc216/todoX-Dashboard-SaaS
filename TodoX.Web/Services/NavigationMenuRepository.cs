using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Models;

namespace TodoX.Web.Services;

public sealed class NavigationMenuRepository
{
    private readonly TodoXConnectionFactory _factory;

    public NavigationMenuRepository(TodoXConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<IReadOnlyList<NavigationMenuGroupDto>> GetActiveMenuAsync(CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var groups = (await conn.QueryAsync<NavigationMenuGroupDto>(
            """
            SELECT id AS Id,
                   code AS Code,
                   title AS Title,
                   icon_key AS IconKey,
                   sort_order AS SortOrder,
                   default_expanded AS DefaultExpanded
              FROM system.navigation_menu_groups
             WHERE is_active = true
             ORDER BY sort_order, title;
            """)).ToList();

        var items = (await conn.QueryAsync<NavigationMenuItemDto>(
            """
            SELECT id AS Id,
                   group_id AS GroupId,
                   code AS Code,
                   title AS Title,
                   href AS Href,
                   icon_key AS IconKey,
                   COALESCE(module_keys, ARRAY[]::text[]) AS ModuleKeys,
                   visibility_policy AS VisibilityPolicy,
                   match_all AS MatchAll,
                   sort_order AS SortOrder
              FROM system.navigation_menu_items
             WHERE is_active = true
             ORDER BY sort_order, title;
            """)).ToList();

        var itemsByGroup = items.GroupBy(x => x.GroupId).ToDictionary(x => x.Key, x => x.ToList());
        foreach (var group in groups)
        {
            if (itemsByGroup.TryGetValue(group.Id, out var groupItems))
            {
                group.Items = groupItems;
            }
        }

        return groups;
    }
}
