using Microsoft.AspNetCore.Components.Routing;
using Topo.Services;

namespace Topo.Shared
{
    // Who a menu group or link is shown to. Flags, so one entry can serve several audiences.
    [Flags]
    public enum NavAudience
    {
        None = 0,
        SignedOut = 1,
        Youth = 2,
        Leader = 4,
        SignedIn = Youth | Leader,
        Everyone = SignedOut | SignedIn
    }

    // One link in the menu. Icon is an Open Iconic class (oi-home). A link whose Href starts with http opens in a new tab.
    // ActivePrefix keeps the link highlighted on any page whose path starts with it (Scouting History has two pages).
    // TitleFor replaces Title when the text depends on state (Enable Logging / Stop Logging).
    // SubItems are pages that belong to this one (SIA Progress under SIA), one level deep. They are not in the sidebar: they show as tabs
    // across the top of this page and of each other (the NavTabs component).
    public record NavItem(
        string Title,
        string Href,
        string Icon,
        NavAudience Audience = NavAudience.Everyone,
        NavLinkMatch Match = NavLinkMatch.Prefix,
        string? ActivePrefix = null,
        Func<StorageService, string>? TitleFor = null,
        IReadOnlyList<NavItem>? SubItems = null)
    {
        public bool IsExternal => Href.StartsWith("http", StringComparison.OrdinalIgnoreCase);

        public string TitleText(StorageService storage) => TitleFor?.Invoke(storage) ?? Title;
    }

    // A run of links. With a Title it shows as a collapsible heading (the title doubles as the key that remembers
    // whether it is open); without one the links are shown on their own with no heading.
    public record NavGroup(
        string? Title,
        NavAudience Audience,
        IReadOnlyList<NavItem> Items,
        bool OpenByDefault = true);

    // The menu, top to bottom. To add a page, add a NavItem to the group it belongs in; to change the order,
    // move the line (or move a whole group). Nothing else needs to change.
    public static class NavMenuItems
    {
        public const string HelpUrl = "https://github.com/NomisNostab/Topo-Blazor/wiki";

        public static readonly IReadOnlyList<NavGroup> Groups = new List<NavGroup>
        {
            new(null, NavAudience.Everyone, new NavItem[]
            {
                new("Home", "", "oi-home", Match: NavLinkMatch.All),
            }),
            new(null, NavAudience.SignedOut, new NavItem[]
            {
                new("Login", "login", "oi-lock-unlocked"),
            }),
            new("Basecamp", NavAudience.Leader, new NavItem[]
            {
                new("SIA", "sia", "oi-clipboard", SubItems: new NavItem[] { new("SIA Progress", "siaprogress", "oi-task") }),
                new("OAS", "oas", "oi-compass", SubItems: new NavItem[] { new("OAS Progress", "oasprogress", "oi-task") }),
                new("Milestone", "milestone", "oi-flag", SubItems: new NavItem[] { new("Milestone Progress", "milestoneprogress", "oi-task") }),
                new("Additional Awards", "awards", "oi-shield"),
                new("Logbook", "logbook", "oi-book"),
                new("Personal Progress", "progress/progress", "oi-person"),
            }),
            new("Approvals", NavAudience.Leader, new NavItem[]
            {
                new("Approvals", "approvals/approvals", "oi-thumb-up"),
                new("Pending Approvals", "approvals/pending", "oi-list"),
                new("Mass awarding", "approvals/massaward", "oi-star"),
            }),
            new("Members", NavAudience.Leader, new NavItem[]
            {
                new("Members", "members", "oi-people"),
                new("Patrol Planner", "patrolplanner", "oi-random"),
            }),
            new("Programming", NavAudience.Leader, new NavItem[]
            {
                new("Program", "program", "oi-calendar"),
                new("Sign In Sheet", "signinsheet", "oi-pencil"),
                new("Program Ideas", "programideas", "oi-lightbulb", SubItems: new NavItem[] { new("Proposed Activities", "programideas/proposed", "oi-comment-square") }),
                new("Program Builder", "programbuilder", "oi-wrench"),
                new("Attendance", "attendance", "oi-bar-chart"),
            }),
            new("Group Life", NavAudience.Leader, new NavItem[]
            {
                new("Wallchart", "wallchart", "oi-bar-chart"),
                new("Ready to Submit", "readytosubmit", "oi-share"),
                new("Section/Group Report", "sectionreport", "oi-pie-chart"),
                new("Unit History", "scoutinghistory/unit", "oi-clock", SubItems: new NavItem[] { new("Scouting History", "scoutinghistory/member", "oi-person") }),
                new("Badge Order", "badgeorder", "oi-cart"),
            }),
            new(null, NavAudience.Youth, new NavItem[]
            {
                new("Logbook", "logbook", "oi-book"),
                new("Additional Awards", "awards", "oi-shield"),
                new("Personal Progress", "progress/progress", "oi-person"),
                new("SIA Progress", "siaprogress", "oi-task"),
                new("Scouting History", "scoutinghistory/member", "oi-person"),
            }),
            new("Settings", NavAudience.SignedIn, new NavItem[]
            {
                new("Settings", "settings", "oi-cog", NavAudience.Leader),
                new("Logout", "logout", "oi-lock-locked"),
                new("Enable Logging", "logging", "oi-document", NavAudience.Leader,
                    TitleFor: storage => storage.LogToQueue ? "Stop Logging" : "Enable Logging"),
            }, OpenByDefault: false),
        };

        // The tabs to show across the top of a page that has sub pages: the page and its sub pages, in the menu's order, when the path (such as
        // "oas" or "programideas/proposed") is one of them. Empty for any other page. Only links the viewer is shown count, so a Scout on SIA
        // Progress (a top level link for them) gets no tabs.
        public static IReadOnlyList<NavItem> TabsFor(string path, NavAudience viewer)
        {
            var wanted = (path ?? "").Split('?', '#')[0].Trim('/');
            foreach (var group in Groups.Where(g => IsShownTo(g.Audience, viewer)))
            {
                foreach (var item in group.Items.Where(i => IsShownTo(i.Audience, viewer) && i.SubItems is { Count: > 0 }))
                {
                    var tabs = new[] { item }.Concat(item.SubItems!.Where(s => IsShownTo(s.Audience, viewer))).ToList();
                    if (tabs.Any(t => string.Equals(t.Href.Trim('/'), wanted, StringComparison.OrdinalIgnoreCase)))
                    {
                        return tabs;
                    }
                }
            }
            return Array.Empty<NavItem>();
        }

        // The audience the current user belongs to
        public static NavAudience AudienceFor(StorageService storage) =>
            !storage.IsAuthenticated ? NavAudience.SignedOut
            : storage.IsYouthMember ? NavAudience.Youth
            : NavAudience.Leader;

        public static bool IsShownTo(NavAudience entry, NavAudience viewer) => (entry & viewer) != 0;
    }
}
