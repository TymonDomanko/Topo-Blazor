namespace Topo.Model.Members
{
    // Who a report is for: the Scouts ticked on the page, or everyone when none are
    public static class ReportScope
    {
        public static List<MemberListModel> Chosen(IEnumerable<MemberListModel> youth)
        {
            var all = youth.ToList();
            var ticked = all.Where(m => m.selected).ToList();
            return ticked.Count > 0 ? ticked : all;
        }

        // Everyone's names, by member id, told apart across the whole unit
        public static Dictionary<string, string> AllNames(IEnumerable<MemberListModel> youth, bool useFullNames) =>
            SignInSheetLayout.GetDisplayNames(youth.ToList(), useFullNames);

        // The names of those the report is for, by member id. Names are told apart across the whole unit, so a Scout's name doesn't
        // change as other Scouts are ticked.
        public static Dictionary<string, string> Names(IEnumerable<MemberListModel> youth, bool useFullNames)
        {
            var all = youth.ToList();
            var names = SignInSheetLayout.GetDisplayNames(all, useFullNames);
            var chosen = Chosen(all).Select(m => m.id).ToHashSet();
            return names.Where(n => chosen.Contains(n.Key)).ToDictionary(n => n.Key, n => n.Value);
        }
    }
}
