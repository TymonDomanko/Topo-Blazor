namespace Topo.Model.Help
{
    // Which page of the Topo wiki documents each page of Topo. A page, or sub page, that has help has its own wiki page, so the help icon at
    // the top right of a page opens the wiki page for exactly that page. To document a new page: add its address here, then write the wiki
    // page.
    public static class HelpPages
    {
        // Where the help pages live. For now this is the wiki on TymonDomanko's fork, because the upstream wiki only takes edits from its
        // maintainer; point it back at https://github.com/NomisNostab/Topo-Blazor/wiki if the pages move there. The menu's own Help
        // link (NavMenuItems.HelpUrl) is separate and still opens the upstream wiki's front page.
        public const string WikiUrl = "https://github.com/TymonDomanko/Topo-Blazor/wiki";

        // The page's address within Topo (no leading slash, no query) and the wiki page that documents it. A wiki page's name is its web
        // address, so it uses hyphens for spaces and has no slash. Some pages are listed under more than one address, because the same
        // page is reached at different addresses in different versions of Topo's menu. The sign in page, Logout, Backup and Restore and
        // the pages that are not part of Topo (the template's counter and fetchdata) have no help page.
        private static readonly Dictionary<string, string> Pages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [""] = "Home-Page",
            ["index"] = "Home-Page",

            // Badgework
            ["oas"] = "OAS",
            ["oasprogress"] = "OAS-Progress",
            ["sia"] = "SIA",
            ["siaprogress"] = "SIA-Progress",
            ["milestone"] = "Milestone",
            ["milestoneprogress"] = "Milestone-Progress",
            ["awards"] = "Additional-Awards",
            ["readytosubmit"] = "Ready-to-Submit",

            // Reports
            ["members"] = "Members",
            ["program"] = "Program",
            ["logbook"] = "Logbook",
            ["wallchart"] = "Wallchart",
            ["attendance"] = "Attendance",
            ["approvals/approvals"] = "Approvals",
            ["approvals/pending"] = "Pending-Approvals",
            ["approvals/massaward"] = "Mass-Awarding",
            ["progress/progress"] = "Personal-Progress",
            ["scoutinghistory/member"] = "Scouting-History-Member",
            ["scoutinghistory/unit"] = "Scouting-History-Unit",
            ["sectionreport"] = "Section-Group-Report",
            ["badgeorder"] = "Badge-Order",

            // Tools
            ["patrolplanner"] = "Patrol-Planner",
            ["members/patrolplanner"] = "Patrol-Planner",
            ["signinsheet"] = "Sign-In-Sheet",
            ["programideas"] = "Program-Ideas",
            ["program/ideas"] = "Program-Ideas",
            ["programideas/proposed"] = "Proposed-Activities",
            ["program/proposed"] = "Proposed-Activities",
            ["programbuilder"] = "Program-Builder",

            // Settings
            ["settings"] = "Settings",
            ["logging"] = "Debug-Logs",
        };

        // Pages whose address ends in an id (a member's progress), by the part before the id
        private static readonly (string Prefix, string Page)[] Prefixes =
        {
            ("progress/details/", "Personal-Progress-Details"),
        };

        // Youth members have help of their own for the pages in their menu
        private static readonly Dictionary<string, string> YouthPages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [""] = "Youth-Home",
            ["index"] = "Youth-Home",
            ["logbook"] = "Youth-Logbook",
            ["awards"] = "Youth-Additional-Awards",
            ["progress/progress"] = "Youth-Personal-Progress",
            ["siaprogress"] = "Youth-SIA-Progress",
            ["scoutinghistory/member"] = "Youth-Scouting-History",
        };

        private static readonly (string Prefix, string Page)[] YouthPrefixes =
        {
            ("progress/details/", "Youth-Personal-Progress-Details"),
        };

        // The wiki page for a page's address within Topo ("members", "/members?x=1" and "Progress/Details/abc" all work), or null when the
        // page has none. A youth member gets the youth help where there is some, and otherwise the same page as anyone else.
        public static string? PageFor(string? path, bool youthMember = false)
        {
            var key = Normalise(path);
            if (youthMember && Find(key, YouthPages, YouthPrefixes) is { } youthPage)
            {
                return youthPage;
            }
            return Find(key, Pages, Prefixes);
        }

        public static string? UrlFor(string? path, bool youthMember = false) =>
            PageFor(path, youthMember) is { } page ? WikiUrl + "/" + page : null;

        // Every wiki page that should exist, once each: the checklist for writing the documentation
        public static IReadOnlyList<string> AllWikiPages =>
            Pages.Values.Concat(Prefixes.Select(p => p.Page)).Concat(YouthPages.Values).Concat(YouthPrefixes.Select(p => p.Page))
                .Distinct(StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal).ToList();

        private static string? Find(string key, Dictionary<string, string> pages, (string Prefix, string Page)[] prefixes)
        {
            if (pages.TryGetValue(key, out var page))
            {
                return page;
            }
            foreach (var (prefix, prefixPage) in prefixes)
            {
                if (key.Length > prefix.Length && key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return prefixPage;
                }
            }
            return null;
        }

        private static string Normalise(string? path)
        {
            var key = path ?? "";
            var cut = key.IndexOfAny(new[] { '?', '#' });
            if (cut >= 0)
            {
                key = key.Substring(0, cut);
            }
            return key.Trim().Trim('/');
        }
    }
}
