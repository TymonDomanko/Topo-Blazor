namespace Topo.Model.OAS
{
    // A branch a skill has taken at a level, which the leader can choose to include or leave out
    public class OasBranchChoice
    {
        // The skill, as the stage list names it ("Paddling")
        public string Stream { get; set; } = string.Empty;
        // Terrain's name for the branch ("canoeing")
        public string Branch { get; set; } = string.Empty;
        // What it is called ("Canoeing")
        public string Title { get; set; } = string.Empty;
        public string Key => OasWorksheetSelection.BranchKey(Stream, Branch);
    }

    // The stages the worksheets are made for, and the ones left out because every Scout in the unit has already been awarded them
    public class OasWorksheetSelectionResult
    {
        public List<OASStageListModel> Included { get; set; } = new List<OASStageListModel>();
        public List<OASStageListModel> LeftOut { get; set; } = new List<OASStageListModel>();
    }

    // Which stages of the Outdoor Adventure Skills a worksheet is wanted for, from what the leader picks: a skill (or the three core ones),
    // a level, whether to include the levels below it, and which of the branches at that level.
    public static class OasWorksheetSelection
    {
        // The choice that stands for Bushcraft, Bushwalking and Camping together
        public const string Core = "core";

        public static readonly string[] CoreStreams = { "Bushcraft", "Bushwalking", "Camping" };

        // The level that means "one above the highest stage anyone in the unit has been awarded in that skill", worked out for each skill
        public const int OneAboveHighest = -1;

        public static string BranchKey(string stream, string branch) => stream.ToLowerInvariant() + "|" + branch.ToLowerInvariant();

        public static string StageKey(string stream, string branch, int stage) => BranchKey(stream, branch) + "|" + stage;

        // The skills a choice covers, as the stage list names them: the three core skills for Core, otherwise the one skill
        public static List<string> StreamsFor(string skill) =>
            string.IsNullOrEmpty(skill) ? new List<string>()
            : skill == Core ? CoreStreams.ToList()
            : new List<string> { skill };

        // The level to make a skill's worksheets for: the level chosen, or for OneAboveHighest the stage above the highest anyone in the unit
        // has been awarded in that skill (the first level if nobody has any, and never above the top level). 0 when no level is chosen.
        public static int LevelFor(string stream, int level, IReadOnlyDictionary<string, int>? highestInUnit, int topLevel)
        {
            if (level != OneAboveHighest)
                return Math.Max(level, 0);
            var highest = highestInUnit != null && highestInUnit.TryGetValue(stream.ToLowerInvariant(), out var found) ? found : 0;
            return Math.Min(highest + 1, Math.Max(topLevel, 1));
        }

        // The highest stage anyone of the members has been awarded (or approved) in each skill, in any branch, by the skill's lowercase name
        public static Dictionary<string, int> HighestAwarded(IEnumerable<string> memberIds, IEnumerable<Result> records)
        {
            var members = memberIds.ToHashSet();
            return records
                .Where(r => (r.status == "awarded" || r.status == "approved") && members.Contains(r.member_id))
                .GroupBy(r => r.achievement_meta.stream.ToLowerInvariant())
                .ToDictionary(g => g.Key, g => g.Max(r => r.achievement_meta.stage));
        }

        // The highest level any skill has, so the level list covers them all
        public static int HighestLevel(IEnumerable<OASStageListModel> stages) => stages.Select(s => s.Stage).DefaultIfEmpty(0).Max();

        // The branches the skills have taken at a level, for the leader to pick from. A skill that has not branched by then (or never
        // does) has only one, so there is nothing to choose and it is not listed.
        public static List<OasBranchChoice> BranchesAt(IEnumerable<OASStageListModel> stages, IEnumerable<string> streams, int level, IReadOnlyDictionary<string, int>? highestInUnit = null)
        {
            var all = stages.ToList();
            var top = HighestLevel(all);
            var choices = new List<OasBranchChoice>();
            foreach (var stream in streams)
            {
                var streamLevel = LevelFor(stream, level, highestInUnit, top);
                var atLevel = StageNodes(all, stream).Where(s => s.Stage == streamLevel).ToList();
                if (atLevel.Select(s => s.Branch.ToLowerInvariant()).Distinct().Count() > 1)
                    choices.AddRange(atLevel.Select(s => new OasBranchChoice { Stream = s.Stream, Branch = s.Branch, Title = s.StageTitle }));
            }
            return choices;
        }

        // The stages to make worksheets for.
        //  - At the chosen level (or, for OneAboveHighest, the level above the highest anyone in the unit has in that skill, which can differ
        //    from skill to skill): each skill's stage, for every branch it has taken (a skill with several branches there contributes
        //    only the ticked ones, chosenBranches by BranchKey).
        //  - With includeBelow, also the levels below it, along the way each chosen branch was reached (Life Saving 4 to 6 for Swift
        //    Water Rescue 7), except levels everyone in the unit has been awarded (everyoneHas, by StageKey), which are returned as left out.
        //    The chosen level itself is always included.
        // In the order of the skills, then the lowest level first.
        public static OasWorksheetSelectionResult Select(IEnumerable<OASStageListModel> stages, IEnumerable<string> streams, int level, bool includeBelow,
            ISet<string> chosenBranches, ISet<string> everyoneHas, IReadOnlyDictionary<string, int>? highestInUnit = null)
        {
            var all = stages.ToList();
            var top = HighestLevel(all);
            var result = new OasWorksheetSelectionResult();
            if (level < 1 && level != OneAboveHighest)
                return result;

            foreach (var stream in streams)
            {
                var streamLevel = LevelFor(stream, level, highestInUnit, top);
                var nodes = StageNodes(all, stream);
                var atLevel = nodes.Where(s => s.Stage == streamLevel).ToList();
                var onlyOneBranch = atLevel.Select(s => s.Branch.ToLowerInvariant()).Distinct().Count() <= 1;
                var chosen = atLevel.Where(s => onlyOneBranch || chosenBranches.Contains(BranchKey(s.Stream, s.Branch))).ToList();

                var wanted = new List<(OASStageListModel stage, bool required)>(chosen.Select(s => (s, true)));
                if (includeBelow)
                    foreach (var node in chosen)
                    {
                        var lineage = Lineage(nodes, node.Branch);
                        wanted.AddRange(nodes.Where(s => s.Stage < streamLevel && lineage.Contains(s.Branch.ToLowerInvariant())).Select(s => (s, false)));
                    }

                foreach (var (stage, required) in wanted
                    .GroupBy(w => w.stage.TemplateLink)
                    .Select(g => g.First())
                    .OrderBy(w => w.stage.Stage).ThenBy(w => all.IndexOf(w.stage)))
                {
                    if (!required && everyoneHas.Contains(StageKey(stage.Stream, stage.Branch, stage.Stage)))
                        result.LeftOut.Add(stage);
                    else
                        result.Included.Add(stage);
                }
            }
            return result;
        }

        // The stages every one of the members has been awarded (or approved) in a skill and branch, as StageKeys. Nothing, if there are no members.
        public static HashSet<string> AwardedToEveryone(IEnumerable<string> memberIds, IEnumerable<Result> records)
        {
            var members = memberIds.Distinct().ToList();
            if (members.Count == 0)
                return new HashSet<string>();
            var done = records
                .Where(r => r.status == "awarded" || r.status == "approved")
                .GroupBy(r => r.member_id)
                .ToDictionary(g => g.Key, g => g.Select(r => StageKey(r.achievement_meta.stream, r.achievement_meta.branch, r.achievement_meta.stage)).ToHashSet());
            HashSet<string>? everyone = null;
            foreach (var member in members)
            {
                var mine = done.TryGetValue(member, out var set) ? set : new HashSet<string>();
                if (everyone == null)
                    everyone = new HashSet<string>(mine);
                else
                    everyone.IntersectWith(mine);
            }
            return everyone ?? new HashSet<string>();
        }

        private static List<OASStageListModel> StageNodes(List<OASStageListModel> all, string stream) =>
            all.Where(s => string.Equals(s.Stream, stream, StringComparison.OrdinalIgnoreCase)).ToList();

        // A branch and the branches it grew out of, back to the first stages of the skill (lowercase)
        private static HashSet<string> Lineage(List<OASStageListModel> nodes, string branch)
        {
            var lineage = new HashSet<string>();
            var current = branch.ToLowerInvariant();
            while (current != "" && lineage.Add(current))
            {
                var first = nodes.Where(s => string.Equals(s.Branch, current, StringComparison.OrdinalIgnoreCase)).OrderBy(s => s.Stage).FirstOrDefault();
                current = first?.ParentBranch.ToLowerInvariant() ?? "";
            }
            return lineage;
        }
    }
}
