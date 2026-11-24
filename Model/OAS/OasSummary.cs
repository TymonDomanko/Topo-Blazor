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
        // CORE, LAND or WATER
        public string Group { get; set; } = string.Empty;
        // A hex colour for the awarded stage, by the group the skill is in
        public string Colour { get; set; } = string.Empty;
    }

    // A branch of a skill: from stage 4 most skills fork into branches (Canoeing and Kayaking in Paddling, Sailing and Windsurfing in
    // Boating), some of which carry on in branches of their own (Life Saving into Swift Water Rescue)
    public class OasBranch
    {
        // The skill it belongs to, by its OasStream key
        public string Stream { get; set; } = string.Empty;
        // Terrain's name for the branch, as in a record's achievement_meta.branch
        public string Key { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
    }

    // One column pair (Awarded and Working on) of the table: a whole skill, or a branch of it
    public class OasSummaryColumn
    {
        public string Stream { get; set; } = string.Empty;
        public string StreamHeading { get; set; } = string.Empty;
        public string Group { get; set; } = string.Empty;
        public string Colour { get; set; } = string.Empty;
        // The branch this column is for; blank for a whole skill, which counts every branch
        public string Branch { get; set; } = string.Empty;
        // What the branch is called under the skill's heading; blank when the columns are whole skills
        public string Heading { get; set; } = string.Empty;
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
        // One for each of the report's Columns, in that order
        public List<OasSummaryCell> Cells { get; set; } = new List<OasSummaryCell>();
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
        // The skills, or the branches of the skills, across the table
        public List<OasSummaryColumn> Columns { get; set; } = new List<OasSummaryColumn>();
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
            new OasStream { Key = "bushcraft", Heading = "Bushcraft", Group = "CORE", Colour = Green },
            new OasStream { Key = "bushwalking", Heading = "Bushwalking", Group = "CORE", Colour = Green },
            new OasStream { Key = "camping", Heading = "Camping", Group = "CORE", Colour = Green },
            new OasStream { Key = "alpine", Heading = "Alpine", Group = "LAND", Colour = Salmon },
            new OasStream { Key = "cycling", Heading = "Cycling", Group = "LAND", Colour = Salmon },
            new OasStream { Key = "vertical", Heading = "Vertical", Group = "LAND", Colour = Salmon },
            new OasStream { Key = "aquatics", Heading = "Aquatics", Group = "WATER", Colour = Blue },
            new OasStream { Key = "boating", Heading = "Boating", Group = "WATER", Colour = Blue },
            new OasStream { Key = "paddling", Heading = "Paddling", Group = "WATER", Colour = Blue }
        };

        private static OasSummaryColumn ColumnOf(OasStream stream, string branch, string heading) => new OasSummaryColumn
        {
            Stream = stream.Key, StreamHeading = stream.Heading, Group = stream.Group, Colour = stream.Colour, Branch = branch, Heading = heading
        };

        // A column for each skill, counting every branch of it
        public static List<OasSummaryColumn> SkillColumns() => Streams.Select(s => ColumnOf(s, "", "")).ToList();

        // A column for each branch of each skill. The branches come in the order Terrain lists them (branches), and any branch that
        // turns up in the records but is not listed follows them. The stages every Scout shares before the skill forks are a column
        // of their own, "Main" (for Bushwalking and Camping, which never fork, that is all nine stages).
        public static List<OasSummaryColumn> BranchColumns(IEnumerable<OasBranch> branches, IEnumerable<Result> records)
        {
            var listed = branches.ToList();
            var columns = new List<OasSummaryColumn>();
            foreach (var stream in Streams)
            {
                var keys = new List<(string key, string title)> { (stream.Key, "Main") };
                foreach (var branch in listed.Where(b => Same(b.Stream, stream.Key)))
                    if (!keys.Any(k => Same(k.key, branch.Key)))
                        keys.Add((branch.Key, branch.Title));
                foreach (var seen in records.Where(r => Same(r.achievement_meta.stream, stream.Key)).Select(r => r.achievement_meta.branch).Where(b => b != "").Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(b => b, StringComparer.Ordinal))
                    if (!keys.Any(k => Same(k.key, seen)))
                        keys.Add((seen, Title(seen)));
                columns.AddRange(keys.Select(k => ColumnOf(stream, k.key, k.title)));
            }
            return columns;
        }

        private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        // "sea-kayaking" as "Sea Kayaking", for a branch Terrain's list does not name
        private static string Title(string key) =>
            System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(key.Replace("_", " ").Replace("-", " ").Trim());

        // The headings over the columns: how many columns each group, and each skill, covers (every column is a pair of cells)
        public static List<(string Heading, string Colour, int Span)> GroupSpans(IEnumerable<OasSummaryColumn> columns) =>
            Runs(columns, c => c.Group, c => c.Group, c => "");

        public static List<(string Heading, string Colour, int Span)> StreamSpans(IEnumerable<OasSummaryColumn> columns) =>
            Runs(columns, c => c.Stream, c => c.StreamHeading, c => c.Colour);

        private static List<(string Heading, string Colour, int Span)> Runs(IEnumerable<OasSummaryColumn> columns, Func<OasSummaryColumn, string> key, Func<OasSummaryColumn, string> heading, Func<OasSummaryColumn, string> colour)
        {
            var runs = new List<(string Heading, string Colour, int Span)>();
            string? last = null;
            foreach (var column in columns)
            {
                if (last != null && key(column) == last)
                    runs[^1] = (runs[^1].Heading, runs[^1].Colour, runs[^1].Span + 1);
                else
                    runs.Add((heading(column), colour(column), 1));
                last = key(column);
            }
            return runs;
        }

        // Whether the columns are branches, so there is a row of branch names under the skills'
        public static bool HasBranchHeadings(IEnumerable<OasSummaryColumn> columns) => columns.Any(c => c.Heading != "");

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
        // The table: one row for each Scout, and for each column (a whole skill, or a branch of one) the highest stage awarded and
        // the stage being worked on. The report comes back with its columns and sheets set; the caller adds the unit and the date.
        //  - Awarded counts the stages awarded in any section, as the Wallchart does, since a Scout's stages go with them.
        //  - Working on is only from records of the current section, for a stage above the awarded one. Of the stages there is
        //    something live for, the lowest is shown; of two records for the same stage, the one furthest along.
        //  - A stage that has been opened but has nothing ticked off is not started, so it is not shown.
        //  - A skill's column counts all its branches; a branch's column counts only that branch, so a stage is in the branch it was
        //    done in. With hideEmptyBranches a branch nobody in the unit has started is left out (the "Main" column of each skill
        //    is always kept, so every skill still shows).
        // Without breakByPatrol the Scouts are in name order; with it, in the order the patrols and the Scouts are given.
        public static OasSummaryReport Build(IEnumerable<OasSummaryMember> members, IEnumerable<Result> records, string section, bool breakByPatrol, bool useFullNames,
            IReadOnlyList<OasSummaryColumn> columns, bool hideEmptyBranches = false)
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
                    Cells = columns.Select(column => CellOf(mine.Where(r => InColumn(r, column)).ToList(), section)).ToList()
                };
            }

            var sheets = !breakByPatrol
                ? new List<OasSummarySheet>
                {
                    new OasSummarySheet { Rows = people.Select(RowOf).OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList() }
                }
                // patrols in the order they first appear, which is the order the unit is listed in
                : people
                    .GroupBy(m => m.Patrol ?? "")
                    .Select(patrol => new OasSummarySheet { Patrol = patrol.Key, Rows = patrol.Select(RowOf).ToList() })
                    .ToList();

            var kept = Enumerable.Range(0, columns.Count)
                .Where(i => !hideEmptyBranches || IsMain(columns[i]) || sheets.Any(sheet => sheet.Rows.Any(r => IsStarted(r.Cells[i]))))
                .ToList();
            if (kept.Count != columns.Count)
                foreach (var row in sheets.SelectMany(sheet => sheet.Rows))
                    row.Cells = kept.Select(i => row.Cells[i]).ToList();

            return new OasSummaryReport { Columns = kept.Select(i => columns[i]).ToList(), Sheets = sheets };
        }

        private static bool InColumn(Result record, OasSummaryColumn column) =>
            string.Equals(record.achievement_meta.stream, column.Stream, StringComparison.OrdinalIgnoreCase)
            && (column.Branch == "" || string.Equals(record.achievement_meta.branch, column.Branch, StringComparison.OrdinalIgnoreCase));

        // The column of the stages before a skill forks, or a skill's column when the columns are skills
        private static bool IsMain(OasSummaryColumn column) =>
            column.Branch == "" || string.Equals(column.Branch, column.Stream, StringComparison.OrdinalIgnoreCase);

        private static bool IsStarted(OasSummaryCell cell) => cell.Awarded > 0 || cell.State != OasWorking.None;

        private static OasSummaryCell CellOf(List<Result> columnRecords, string section)
        {
            var awarded = columnRecords.Where(r => r.status == "awarded").Select(r => r.achievement_meta.stage).DefaultIfEmpty(0).Max();
            var live = columnRecords
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
