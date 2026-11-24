using Topo.Model.Members;

namespace Topo.Model.OAS
{
    // What a Scout is doing about the next stage of a skill, beyond the stages already awarded
    public enum OasWorking
    {
        // Nothing started
        None,
        // Started (at least one requirement ticked off) and not sent for review
        InProgress,
        // Sent for review and waiting for a leader
        InReview,
        // Approved by a leader and waiting to be presented
        Approved
    }

    // One of the nine Outdoor Adventure Skills
    public class OasStream
    {
        // Terrain's name for the stream, as in a record's achievement_meta.stream
        public string Key { get; set; } = string.Empty;
        public string Heading { get; set; } = string.Empty;
        // A hex colour for the awarded stage, by the group the skill is in
        public string Colour { get; set; } = string.Empty;
    }

    // A Scout of the unit, as the summary needs them. Pass them in the order the unit is listed (Unit Leader, then by patrol,
    // each patrol's Patrol Leader and Assistant Patrol Leader first).
    public class OasSummaryMember
    {
        public string MemberId { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Patrol { get; set; } = string.Empty;
    }

    // One Scout's standing in one skill
    public class OasSummaryCell
    {
        // The highest stage awarded, 0 when there is none
        public int Awarded { get; set; }
        // The stage being worked on, above the awarded one, and where it is up to; 0 and None when there is nothing
        public int Working { get; set; }
        public OasWorking State { get; set; }
    }

    public class OasSummaryRow
    {
        public string Name { get; set; } = string.Empty;
        // One for each of OasSummaryLayout.Streams, in that order
        public List<OasSummaryCell> Streams { get; set; } = new List<OasSummaryCell>();
    }

    // The whole unit, or one patrol when the report is broken up by patrol
    public class OasSummarySheet
    {
        // Blank for the whole unit
        public string Patrol { get; set; } = string.Empty;
        public List<OasSummaryRow> Rows { get; set; } = new List<OasSummaryRow>();
    }

    // Everything the OAS summary report shows: laid out once here, drawn by the page's preview and by the pdf and spreadsheet
    public class OasSummaryReport
    {
        public string UnitName { get; set; } = string.Empty;
        public string Section { get; set; } = string.Empty;
        public DateTime GeneratedAt { get; set; }
        public List<OasSummarySheet> Sheets { get; set; } = new List<OasSummarySheet>();
    }

    public static class OasSummaryLayout
    {
        private const string Green = "#8fbc8b";
        private const string Salmon = "#ffa07a";
        private const string Blue = "#add8e6";

        // The shades of the stage being worked on: started, waiting for a leader, and waiting to be presented
        public const string InProgressColour = "#fff6bf";
        public const string InReviewColour = "#ffc266";
        public const string ApprovedColour = "#9cf250";

        // The skills in the order, groups and shades the Wallchart gives them
        public static readonly OasStream[] Streams =
        {
            new OasStream { Key = "bushcraft", Heading = "Bushcraft", Colour = Green },
            new OasStream { Key = "bushwalking", Heading = "Bushwalking", Colour = Green },
            new OasStream { Key = "camping", Heading = "Camping", Colour = Green },
            new OasStream { Key = "alpine", Heading = "Alpine", Colour = Salmon },
            new OasStream { Key = "cycling", Heading = "Cycling", Colour = Salmon },
            new OasStream { Key = "vertical", Heading = "Vertical", Colour = Salmon },
            new OasStream { Key = "aquatics", Heading = "Aquatics", Colour = Blue },
            new OasStream { Key = "boating", Heading = "Boating", Colour = Blue },
            new OasStream { Key = "paddling", Heading = "Paddling", Colour = Blue }
        };

        // The headings over the skills, and how many skills each covers
        public static readonly (string Heading, int Span)[] Groups = { ("CORE", 3), ("LAND", 3), ("WATER", 3) };

        public static string ColourOf(OasWorking state) => state switch
        {
            OasWorking.InProgress => InProgressColour,
            OasWorking.InReview => InReviewColour,
            OasWorking.Approved => ApprovedColour,
            _ => string.Empty
        };

        // The stage being worked on as it is shown: a plain number when started, with R when in review and A when approved, so the
        // difference shows on a black and white print too
        public static string WorkingText(OasSummaryCell cell) => cell.State switch
        {
            OasWorking.InProgress => cell.Working.ToString(),
            OasWorking.InReview => cell.Working + " R",
            OasWorking.Approved => cell.Working + " A",
            _ => string.Empty
        };

        public const string Legend = "Awarded: the highest stage awarded. Working on: the next stage the Scout has started; a plain number is in progress, R is waiting for review and A is approved and waiting to be presented.";
    }

    public static class OasSummaryBuilder
    {
        // One row for each Scout: for each skill the highest stage awarded and the stage being worked on.
        //  - Awarded counts the stages awarded in any section, as the Wallchart does, since a Scout's stages go with them.
        //  - Working on is only from records of the current section, for a stage above the awarded one. Of the stages there is
        //    something live for, the lowest is shown; of two records for the same stage, the one furthest along.
        //  - A stage that has been opened but has nothing ticked off is not started, so it is not shown.
        // Without breakByPatrol the Scouts are in name order; with it, in the order the patrols and the Scouts are given.
        public static List<OasSummarySheet> Build(IEnumerable<OasSummaryMember> members, IEnumerable<Result> records, string section, bool breakByPatrol, bool useFullNames)
        {
            var people = members.ToList();
            var byMember = records.ToLookup(r => r.member_id);
            var names = MemberNames.GetDisplayNames(people, m => m.FirstName, m => m.LastName, useFullNames);

            OasSummaryRow RowOf(OasSummaryMember member)
            {
                var mine = byMember[member.MemberId].ToList();
                return new OasSummaryRow
                {
                    Name = names[member],
                    Streams = OasSummaryLayout.Streams.Select(stream => CellOf(mine.Where(r => string.Equals(r.achievement_meta.stream, stream.Key, StringComparison.OrdinalIgnoreCase)).ToList(), section)).ToList()
                };
            }

            if (!breakByPatrol)
                return new List<OasSummarySheet>
                {
                    new OasSummarySheet { Rows = people.Select(RowOf).OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList() }
                };

            // patrols in the order they first appear, which is the order the unit is listed in
            return people
                .GroupBy(m => m.Patrol ?? "")
                .Select(patrol => new OasSummarySheet { Patrol = patrol.Key, Rows = patrol.Select(RowOf).ToList() })
                .ToList();
        }

        private static OasSummaryCell CellOf(List<Result> streamRecords, string section)
        {
            var awarded = streamRecords.Where(r => r.status == "awarded").Select(r => r.achievement_meta.stage).DefaultIfEmpty(0).Max();
            var live = streamRecords
                .Where(r => r.achievement_meta.stage > awarded && string.Equals(r.section, section, StringComparison.OrdinalIgnoreCase))
                .Select(r => (stage: r.achievement_meta.stage, state: StateOf(r)))
                .Where(s => s.state != OasWorking.None)
                .GroupBy(s => s.stage)
                .OrderBy(g => g.Key)
                .FirstOrDefault();
            return live == null
                ? new OasSummaryCell { Awarded = awarded }
                : new OasSummaryCell { Awarded = awarded, Working = live.Key, State = live.Max(s => s.state) };
        }

        // How far along a record is. The states are in order, so the furthest along of several records is the largest.
        private static OasWorking StateOf(Result record) => record.status switch
        {
            "approved" => OasWorking.Approved,
            "pending_review" => OasWorking.InReview,
            "draft_review" or "in_progress" => HasStarted(record) ? OasWorking.InProgress : OasWorking.None,
            _ => OasWorking.None
        };

        // A requirement ticked off has a verified date; the Scout's logbook tick is stored as a plain "true"
        private static bool HasStarted(Result record) =>
            record.answers.Any(a => (a.Key.EndsWith("_verifiedDate", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(a.Value)) || a.Value == "true");
    }
}
