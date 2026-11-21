namespace Topo.Model.ProgramBuilder
{
    // Keeps the leader in charge of each night and activity in step with the list of contacts: when a contact is renamed, so is the leader on
    // everything that leader is in charge of.
    public static class ProgramLeaders
    {
        // A leader in charge is a name, or several joined with "/" when an activity has more than one. Each of the old names becomes the new one.
        public static string Rename(string leaders, IEnumerable<string> oldNames, string newName)
        {
            if (leaders.Trim() == "")
                return leaders;
            var olds = oldNames.Select(n => n.Trim()).Where(n => n != "").ToList();
            var parts = leaders.Split('/').Select(p => p.Trim()).Select(p => olds.Any(o => string.Equals(o, p, StringComparison.OrdinalIgnoreCase)) ? newName : p);
            return string.Join("/", parts.Distinct(StringComparer.OrdinalIgnoreCase));
        }

        // Changes the leader on every night and camp or the like, and the program's usual leader, from the old names to the new. How many were changed.
        public static int Rename(ProgramDraft draft, IEnumerable<string> oldNames, string newName)
        {
            var olds = oldNames.ToList();
            var changed = 0;
            foreach (var row in draft.NightEdits.Values.Concat(draft.Extras))
            {
                var renamed = Rename(row.Leader, olds, newName);
                if (renamed != row.Leader)
                {
                    row.Leader = renamed;
                    changed++;
                }
            }
            draft.DefaultLeader = Rename(draft.DefaultLeader, olds, newName);
            return changed;
        }
    }
}
