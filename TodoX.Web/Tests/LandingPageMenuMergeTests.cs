using TodoX.Web.Components.Layout;
using TodoX.Web.Models;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class LandingPageMenuMergeTests
{
    private static NavigationMenuGroupDto Group(string code = "landing_page") => new()
    {
        Id = Guid.NewGuid(), Code = code, Title = "Landing Page", SortOrder = 42,
        DefaultExpanded = false, IconKey = "Custom"
    };

    private static NavigationMenuItemDto Item(NavigationMenuGroupDto group, string code, string policy = "always") => new()
    {
        Id = Guid.NewGuid(), GroupId = group.Id, Code = code, Title = code,
        VisibilityPolicy = policy, ModuleKeys = policy == "always" ? [] : ["landing"], SortOrder = 5
    };

    private static NavigationMenuGroupDto Fallback()
    {
        var group = Group();
        group.Items = [Item(group, "landing_contacts"), Item(group, "landing_industries"), Item(group, "landing_intro_video")];
        return group;
    }

    [Fact]
    public void ExistingGroupReceivesMissingVideoWithoutChangingConfiguredItemsOrGroup()
    {
        var group = Group();
        var configured = Item(group, "landing_contacts", "any_module");
        group.Items.Add(configured);
        group.Items.Add(Item(group, "landing_industries"));
        var groups = new List<NavigationMenuGroupDto> { group };
        LandingPageMenuMerge.Merge(groups, Fallback());
        Assert.Single(groups);
        Assert.Same(group, groups[0]);
        Assert.Same(configured, group.Items[0]);
        Assert.Equal("any_module", configured.VisibilityPolicy);
        Assert.Equal(3, group.Items.Count);
        Assert.All(group.Items, item => Assert.Equal(group.Id, item.GroupId));
        Assert.Equal(42, group.SortOrder);
        Assert.False(group.DefaultExpanded);
    }

    [Fact]
    public void MissingGroupGetsSingleFallbackWithThreeChildren()
    {
        var groups = new List<NavigationMenuGroupDto>();
        LandingPageMenuMerge.Merge(groups, Fallback());
        Assert.Single(groups);
        Assert.Equal(3, groups[0].Items.Count);
        Assert.All(groups[0].Items, item => Assert.Equal(groups[0].Id, item.GroupId));
    }

    [Fact]
    public void AlreadyCompleteGroupDoesNotGainDuplicates()
    {
        var group = Fallback();
        var groups = new List<NavigationMenuGroupDto> { group };
        LandingPageMenuMerge.Merge(groups, Fallback());
        Assert.Same(group, Assert.Single(groups));
        Assert.Equal(3, group.Items.Count);
    }

    [Fact]
    public void RepeatedMergeIsIdempotent()
    {
        var groups = new List<NavigationMenuGroupDto> { Group() };
        LandingPageMenuMerge.Merge(groups, Fallback());
        var ids = groups[0].Items.Select(item => item.Id).ToArray();
        LandingPageMenuMerge.Merge(groups, Fallback());
        Assert.Single(groups);
        Assert.Equal(ids, groups[0].Items.Select(item => item.Id));
    }

    [Fact]
    public void DuplicateGroupsConsolidateWithoutLosingOtherItems()
    {
        var first = Group();
        first.Items.Add(Item(first, "landing_contacts"));
        var second = Group();
        var custom = Item(second, "landing_custom", "any_module");
        second.Items.Add(custom);
        second.Items.Add(Item(second, "landing_contacts"));
        var unrelated = Group("other");
        var groups = new List<NavigationMenuGroupDto> { first, unrelated, second };
        LandingPageMenuMerge.Merge(groups, Fallback());
        Assert.Equal(2, groups.Count);
        Assert.Same(unrelated, groups[1]);
        Assert.Equal(4, first.Items.Count);
        Assert.Equal(4, first.Items.Select(item => item.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        var moved = Assert.Single(first.Items, item => item.Code == "landing_custom");
        Assert.Equal(first.Id, moved.GroupId);
        Assert.Equal(custom.Id, moved.Id);
        Assert.Equal(custom.VisibilityPolicy, moved.VisibilityPolicy);
        Assert.Equal(custom.ModuleKeys, moved.ModuleKeys);
    }

    [Fact]
    public void ExistingVisibilityAndOtherGroupPermissionsRemainUntouched()
    {
        var group = Group();
        var restricted = Item(group, "landing_intro_video", "customer_only");
        group.Items.Add(restricted);
        var other = Group("other");
        var external = Item(other, "landing_contacts", "any_module");
        other.Items.Add(external);
        var groups = new List<NavigationMenuGroupDto> { other, group };
        LandingPageMenuMerge.Merge(groups, Fallback());
        Assert.Same(restricted, Assert.Single(group.Items, item => item.Code == "landing_intro_video"));
        Assert.Equal("customer_only", restricted.VisibilityPolicy);
        Assert.Same(external, Assert.Single(other.Items));
        Assert.DoesNotContain(group.Items, item => item.Code == "landing_contacts");
        Assert.Contains(group.Items, item => item.Code == "landing_industries");
    }
}