namespace Topo.Model.ProgramBuilder
{
    // The parts of the Scout Method that an activity in Terrain says it uses. Any number can be ticked, from none to all.
    public static class ProgramScoutMethod
    {
        // As Terrain names them (read from the activities in Terrain), with the words it shows
        public static readonly (string Key, string Label)[] Elements =
        {
            ("promise_and_law", "Promise and Law"), ("learn_by_doing", "Learn by Doing"), ("patrol_system", "Patrol System"),
            ("symbolic_framework", "Symbolic Framework"), ("community_involvement", "Community Involvement"), ("personal_progression", "Personal Progression"),
            ("youth_leading_adult_supporting", "Youth Leading, Adult Supporting"), ("nature_and_outdoors", "Nature and Outdoors")
        };

        public static string Label(string key) => Elements.FirstOrDefault(e => e.Key == key).Label ?? key;

        // The elements with one switched on or off, in the order Terrain lists them (any it does not list after those)
        public static List<string> Toggle(IEnumerable<string> current, string key)
        {
            var set = current.ToList();
            if (!set.Remove(key))
                set.Add(key);
            var order = Elements.Select(e => e.Key).ToList();
            return set.OrderBy(k => order.IndexOf(k) < 0 ? int.MaxValue : order.IndexOf(k)).ToList();
        }

        public static bool Same(IEnumerable<string> a, IEnumerable<string> b) => a.ToHashSet().SetEquals(b);

        // "none", the one named, or how many
        public static string Summary(IEnumerable<string> keys)
        {
            var list = keys.ToList();
            return list.Count == 0 ? "none set" : list.Count == 1 ? Label(list[0]) : $"{list.Count} elements";
        }
    }
}
