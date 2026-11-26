namespace Topo.Model.AdditionalAwards
{
    // A column of the awards table: one award, or a blank one for anything Terrain gave no name for
    public class AwardsTableColumn
    {
        public string AwardId { get; set; } = string.Empty;
        // Blank when no award of that id was named
        public string Heading { get; set; } = string.Empty;
    }

    public class AwardsTableCell
    {
        // The date awarded, or presented once it has been
        public DateTime? Date { get; set; }
        // A hex colour, or null to leave the cell unshaded
        public string? Colour { get; set; }
    }

    public class AwardsTableRow
    {
        public string MemberId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int KMsHiked { get; set; }
        public int NightsCamped { get; set; }
        // Shaded red when the Scout has hiked or camped enough for the next award of that kind and it has not been given
        public string? KMsColour { get; set; }
        public string? NightsColour { get; set; }
        // One for each column, in the order of AwardsTable.Columns
        public List<AwardsTableCell> Cells { get; set; } = new List<AwardsTableCell>();
    }

    public class AwardsTable
    {
        // Blank for the whole unit
        public string Patrol { get; set; } = string.Empty;
        public List<AwardsTableColumn> Columns { get; set; } = new List<AwardsTableColumn>();
        public List<AwardsTableRow> Rows { get; set; } = new List<AwardsTableRow>();

        public const string NotAwarded = "#ff0000";
        public const string Presented = "#90ee90";
        public const string Awarded = "#ffa500";

        // The awards that get a column, in the order of the awards
        public static List<string> DistinctAwards(IEnumerable<AdditionalAwardListModel> awards) =>
            awards.OrderBy(a => a.AwardSortIndex).Select(a => a.AwardId).Distinct().ToList();

        // How the Additional Awards report is laid out, shared by the page's preview and the pdf and spreadsheet
        public static AwardsTable Build(AdditionalAwardsReportDataModel data) => Build(data, data.SortedAdditionalAwardsList, "");

        // A table for the whole unit, or for each patrol when the report is broken up by patrol. Each has the same awards across the
        // top, so the sheets can be compared.
        public static List<AwardsTable> BuildSheets(AdditionalAwardsReportDataModel data, bool breakByPatrol)
        {
            var sheets = data.SortedAdditionalAwardsList
                .GroupBy(a => breakByPatrol ? a.MemberPatrol ?? "" : "")
                .OrderBy(g => g.Key)
                .Select(g => Build(data, g.ToList(), g.Key))
                .ToList();
            return sheets.Count > 0 ? sheets : new List<AwardsTable> { Build(data) };
        }

        private static AwardsTable Build(AdditionalAwardsReportDataModel data, List<AdditionalAwardListModel> awards, string patrol)
        {
            var distinctAwards = data.DistinctAwards;
            var table = new AwardsTable { Patrol = patrol };
            foreach (var id in distinctAwards)
                table.Columns.Add(new AwardsTableColumn { AwardId = id, Heading = data.AwardSpecificationsList.FirstOrDefault(a => a.id == id)?.name ?? "" });

            foreach (var member in awards.GroupBy(a => a.MemberName))
            {
                var first = member.First();
                var row = new AwardsTableRow
                {
                    MemberId = first.MemberId,
                    Name = member.Key,
                    KMsHiked = first.KMsHiked,
                    NightsCamped = first.NightsCamped,
                    Cells = distinctAwards.Select(_ => new AwardsTableCell()).ToList()
                };
                foreach (var award in member)
                {
                    var cell = row.Cells[distinctAwards.IndexOf(award.AwardId)];
                    if (award.AwardDate.HasValue)
                    {
                        cell.Date = award.AwardDate.Value;
                        cell.Colour = Awarded;
                        if (award.PresentedDate.HasValue)
                        {
                            cell.Date = award.PresentedDate.Value;
                            cell.Colour = Presented;
                        }
                    }
                    else if (award.AwardId.StartsWith("walkabout") && award.KMsHiked >= award.AwardUnits)
                    {
                        cell.Colour = NotAwarded;
                        row.KMsColour = NotAwarded;
                    }
                    else if (award.AwardId.StartsWith("camper") && award.NightsCamped >= award.AwardUnits)
                    {
                        cell.Colour = NotAwarded;
                        row.NightsColour = NotAwarded;
                    }
                }
                table.Rows.Add(row);
            }

            // The rows that carry a Scout's totals and no award have no award id. They need no column of their own, so it is left out
            // unless something was recorded in it.
            var blank = table.Columns.FindIndex(c => c.AwardId == "");
            if (blank >= 0 && table.Rows.All(r => r.Cells[blank].Date == null && r.Cells[blank].Colour == null))
            {
                table.Columns.RemoveAt(blank);
                foreach (var row in table.Rows)
                    row.Cells.RemoveAt(blank);
            }
            return table;
        }
    }
}
