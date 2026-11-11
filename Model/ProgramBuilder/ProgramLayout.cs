using System.Globalization;

namespace Topo.Model.ProgramBuilder
{
    // One line of the printed program: a section night or an activity, or the break between two stretches of them
    public class ProgramDocRow
    {
        // The month, on the first row of each run of rows in it; blank on the rest
        public string Month { get; set; } = string.Empty;
        // How many rows the month's cell covers, on the row that has it
        public int MonthSpan { get; set; }
        // The day of the month, or the days of a camp or hike, such as "28", "27-1" or "Fri 6"
        public string DateText { get; set; } = string.Empty;
        public string Activity { get; set; } = string.Empty;
        public string Game { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string ChallengeArea { get; set; } = string.Empty;
        public string DutyPatrol { get; set; } = string.Empty;
        public string Leader { get; set; } = string.Empty;
        public bool IsBreak { get; set; }
        // What the break is called, such as "Term Break"
        public string BreakLabel { get; set; } = string.Empty;
        public DateTime Date { get; set; }
        public DateTime? EndDate { get; set; }
        public ProgramRowKind Kind { get; set; }
    }

    // The program as the page shows it and the Word, pdf and spreadsheet print it
    public class ProgramDocument
    {
        public string Title { get; set; } = string.Empty;
        public string UnitName { get; set; } = string.Empty;
        public string Section { get; set; } = string.Empty;
        public List<ProgramDocRow> Rows { get; set; } = new List<ProgramDocRow>();
        // The columns shown after the date and activity, in order: "game", "location", "challenge", "duty" and "leader"
        public List<string> Columns { get; set; } = new List<string>();
        // How many activities there are in each challenge area; empty when not shown or when there are no activities
        public List<ProgramAreaCount> ChallengeAreas { get; set; } = new List<ProgramAreaCount>();
        public List<ProgramLocation> Locations { get; set; } = new List<ProgramLocation>();
        public List<ProgramContact> Contacts { get; set; } = new List<ProgramContact>();
    }

    public class ProgramAreaCount
    {
        public string Area { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    public static class ProgramLayout
    {
        // The challenge areas as named in Terrain, in the order they are listed
        private static readonly (string Key, string Label)[] Areas = { ("outdoors", "Outdoors"), ("community", "Community"), ("personal_growth", "Personal Growth"), ("creative", "Creative") };

        public static string AreaLabel(string key) => Areas.FirstOrDefault(a => string.Equals(a.Key, key, StringComparison.OrdinalIgnoreCase)).Label ?? "";

        public static ProgramDocument Build(ProgramDraft draft)
        {
            var entries = new List<ProgramRow>();
            entries.AddRange(ProgramPlanner.Nights(draft).Where(n => !n.Skipped));
            entries.AddRange(draft.Extras.Where(e => e.Kind != ProgramRowKind.SectionNight));
            var ordered = entries.OrderBy(e => e.Date).ThenBy(e => e.Kind == ProgramRowKind.SectionNight ? 0 : 1).ToList();

            var rows = new List<ProgramDocRow>();
            var breaks = draft.Breaks.OrderBy(b => b.From).ToList();
            var nextBreak = 0;
            foreach (var entry in ordered)
            {
                // a break goes in before the first row that is on or after its first day, so what is in the break comes after it
                while (nextBreak < breaks.Count && entry.Date.Date >= breaks[nextBreak].From.Date)
                {
                    if (rows.Count > 0)
                        rows.Add(new ProgramDocRow { IsBreak = true, BreakLabel = breaks[nextBreak].Label, Date = breaks[nextBreak].From });
                    nextBreak++;
                }
                rows.Add(new ProgramDocRow
                {
                    Date = entry.Date, EndDate = entry.EndDate, Kind = entry.Kind,
                    DateText = DateText(entry, draft.MeetingDay),
                    Activity = entry.Activity,
                    Game = entry.Game,
                    Location = Or(entry.Location, draft.DefaultLocation),
                    ChallengeArea = AreaLabel(Or(entry.ChallengeArea, draft.DefaultChallengeArea)),
                    DutyPatrol = entry.Kind == ProgramRowKind.SectionNight ? ProgramPlanner.ShortPatrol(entry.Duty) : "",
                    Leader = Or(entry.Leader, draft.DefaultLeader)
                });
            }

            // a month's name goes on the first of its rows, which it covers down to the next break or month
            for (var i = 0; i < rows.Count; i++)
            {
                if (rows[i].IsBreak)
                    continue;
                var month = rows[i].Date.ToString("MMMM", CultureInfo.InvariantCulture);
                var previous = i > 0 && !rows[i - 1].IsBreak ? rows[i - 1].Date.ToString("MMMM", CultureInfo.InvariantCulture) : null;
                if (month == previous)
                    continue;
                rows[i].Month = month;
                rows[i].MonthSpan = 1;
                for (var j = i + 1; j < rows.Count && !rows[j].IsBreak && rows[j].Date.ToString("MMMM", CultureInfo.InvariantCulture) == month; j++)
                    rows[i].MonthSpan++;
            }

            var used = rows.Where(r => !r.IsBreak).Select(r => r.Location).Where(l => l != "").ToHashSet(StringComparer.OrdinalIgnoreCase);
            var locations = draft.Locations.Where(l => l.Name.Trim() != "").ToList();
            return new ProgramDocument
            {
                Title = draft.Title, UnitName = draft.UnitName, Section = draft.Section, Rows = rows,
                Columns = Columns(draft),
                ChallengeAreas = AreaCounts(rows),
                // only the places the program goes to, and every contact
                Locations = draft.ShowLocations ? locations.Where(l => used.Contains(l.Name.Trim())).ToList() : new List<ProgramLocation>(),
                Contacts = draft.ShowContacts ? draft.Contacts.Where(c => c.Name.Trim() != "").ToList() : new List<ProgramContact>()
            };
        }

        private static List<string> Columns(ProgramDraft draft)
        {
            var columns = new List<string>();
            if (draft.ShowGame) columns.Add("game");
            if (draft.ShowLocation) columns.Add("location");
            if (draft.ShowChallengeArea) columns.Add("challenge");
            if (draft.ShowDutyPatrol) columns.Add("duty");
            if (draft.ShowLeader) columns.Add("leader");
            return columns;
        }

        // The activities in each challenge area, those with none set last; an area with none is left out
        private static List<ProgramAreaCount> AreaCounts(List<ProgramDocRow> rows)
        {
            var activities = rows.Where(r => !r.IsBreak && r.Activity.Trim() != "").ToList();
            var counts = Areas.Select(a => new ProgramAreaCount { Area = a.Label, Count = activities.Count(r => r.ChallengeArea == a.Label) }).ToList();
            counts.Add(new ProgramAreaCount { Area = "Not set", Count = activities.Count(r => r.ChallengeArea == "") });
            return activities.Count == 0 ? new List<ProgramAreaCount>() : counts.Where(c => c.Count > 0).ToList();
        }

        // The day, or the days of a camp or hike ("20-22", "27-1" over a month end); a day that is not the section night has its weekday
        // too ("Fri 6"), so it is not mistaken for one
        public static string DateText(ProgramRow row, DayOfWeek sectionNight)
        {
            if (row.EndDate != null && row.EndDate.Value.Date > row.Date.Date)
                return $"{row.Date.Day}-{row.EndDate.Value.Day}";
            return row.Date.DayOfWeek == sectionNight
                ? row.Date.Day.ToString()
                : $"{row.Date.ToString("ddd", CultureInfo.InvariantCulture)} {row.Date.Day}";
        }

        private static string Or(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? (fallback ?? "") : value;
    }
}
