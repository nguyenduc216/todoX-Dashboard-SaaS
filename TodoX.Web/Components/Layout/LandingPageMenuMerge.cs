using TodoX.Web.Models;

namespace TodoX.Web.Components.Layout;

public static class LandingPageMenuMerge
{
    public static void Merge(List<NavigationMenuGroupDto> groups, NavigationMenuGroupDto fallback)
    {
        var landingGroups = groups.Where(group => string.Equals(group.Code, "landing_page", StringComparison.OrdinalIgnoreCase)).ToList();
        var destination = landingGroups.FirstOrDefault();
        if (destination is null)
        {
            destination = fallback;
            groups.Add(destination);
        }

        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        destination.Items.RemoveAll(item => !codes.Add(item.Code));
        foreach (var duplicate in landingGroups.Skip(1))
        {
            foreach (var item in duplicate.Items)
            {
                if (!codes.Add(item.Code)) continue;
                var moved = CopyItem(item, destination.Id);
                destination.Items.Add(moved);
            }

            groups.Remove(duplicate);
        }

        // A configured item elsewhere must retain its original group and permissions.
        var allCodes = groups.SelectMany(group => group.Items).Select(item => item.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var item in fallback.Items)
        {
            if (!allCodes.Add(item.Code)) continue;
            destination.Items.Add(CopyItem(item, destination.Id));
        }
    }

    private static NavigationMenuItemDto CopyItem(NavigationMenuItemDto item, Guid groupId) => new()
    {
        Id = item.Id,
        GroupId = groupId,
        Code = item.Code,
        Title = item.Title,
        Href = item.Href,
        IconKey = item.IconKey,
        ModuleKeys = item.ModuleKeys.ToArray(),
        VisibilityPolicy = item.VisibilityPolicy,
        MatchAll = item.MatchAll,
        SortOrder = item.SortOrder
    };
}