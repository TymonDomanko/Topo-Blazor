using System.Text.RegularExpressions;
using Topo.Model.Members;
using Topo.Model.Program;
using Topo.Model.UnitReport;

namespace Topo.Model.ProgramBuilder
{
    // Works out the rows of a program from what was entered: a row for each section night between the dates, with the duty patrols
    // taking turns, and the camps, hikes and other activities beside them
    public static class ProgramPlanner
    {
        // The day of the week the section meets: the day most of the unit's single day events fall on, leaving out camps, hikes and unit
        // councils. Null when there are too few events to say.
        public static DayOfWeek? FindSectionNight(IEnumerable<Event> events)
        {
            var days = events
                .Where(e => !e.status.Contains("cancel", StringComparison.OrdinalIgnoreCase))
                .Where(e => e.invitee_type != "patrol" && e.invitee_type != "member")
                .Where(e => e.end_datetime.Date <= e.start_datetime.Date)
                .Where(e => !Regex.IsMatch(e.title, "unit council|camp|hike", RegexOptions.IgnoreCase))
                .GroupBy(e => e.start_datetime.DayOfWeek)
                .OrderByDescending(g => g.Count()).ThenBy(g => ((int)g.Key + 6) % 7)
                .ToList();
            return days.Count > 0 && days[0].Count() >= 2 ? days[0].Key : null;
        }

        // Every date from the start to the end that falls on the section night, leaving out the breaks
        public static List<DateTime> SectionNights(DateTime start, DateTime end, DayOfWeek day, IEnumerable<ProgramBreak> breaks)
        {
            var nights = new List<DateTime>();
            var breakList = breaks.ToList();
            for (var date = start.Date; date <= end.Date; date = date.AddDays(1))
            {
                if (date.DayOfWeek == day && !breakList.Any(b => date >= b.From.Date && date <= b.To.Date))
                    nights.Add(date);
            }
            return nights;
        }

        public static string NightKey(DateTime date) => date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        // The program's section nights, oldest first, with what was filled in for each and the patrol on duty. A night that is not running
        // is included, marked as skipped, so it can be put back.
        public static List<ProgramRow> Nights(ProgramDraft draft)
        {
            var patrols = draft.DutyPatrols;
            var turn = Math.Max(0, patrols.FindIndex(p => string.Equals(p, draft.FirstDutyPatrol, StringComparison.OrdinalIgnoreCase)));
            var counted = 0;
            var rows = new List<ProgramRow>();
            foreach (var date in SectionNights(draft.Start, draft.End, draft.MeetingDay, draft.Breaks))
            {
                var row = draft.NightEdits.TryGetValue(NightKey(date), out var edited) ? Copy(edited) : new ProgramRow();
                row.Kind = ProgramRowKind.SectionNight;
                row.Date = date;
                row.EndDate = null;
                row.Duty = "";
                if (!row.Skipped && !row.NoDuty && patrols.Count > 0)
                {
                    // a patrol put on duty by hand does not use up a turn in the cycle
                    if (row.DutyPatrol != "")
                        row.Duty = row.DutyPatrol;
                    else
                    {
                        row.Duty = patrols[(turn + counted) % patrols.Count];
                        counted++;
                    }
                }
                else if (!row.Skipped && !row.NoDuty)
                    row.Duty = row.DutyPatrol;
                rows.Add(row);
            }
            return rows;
        }

        // The patrol label as the printed program has it: the colour of a patrol named like "Magpie (Orange)", otherwise the name
        public static string ShortPatrol(string name)
        {
            var match = Regex.Match(name ?? "", @"^.*\((.+)\)\s*$");
            return match.Success ? match.Groups[1].Value.Trim() : (name ?? "").Trim();
        }

        // The unit's patrols, from its youth, leaving out the adults' patrol
        public static List<string> PatrolsOf(IEnumerable<MemberListModel> members) =>
            members.Where(m => !UnitReportBuilder.IsLeader(m) && !string.IsNullOrWhiteSpace(m.patrol_name))
                .Select(m => m.patrol_name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();

        // The first names of the unit's own leaders
        public static List<string> LeadersOf(IEnumerable<MemberListModel> members) =>
            UnitReportBuilder.LeadersOf(members).Select(m => m.first_name.Trim()).Where(n => n != "").Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

        private static ProgramRow Copy(ProgramRow row) => new ProgramRow
        {
            Id = row.Id, Kind = row.Kind, Date = row.Date, EndDate = row.EndDate, Activity = row.Activity, Game = row.Game, Location = row.Location,
            Leader = row.Leader, DutyPatrol = row.DutyPatrol, NoDuty = row.NoDuty, Skipped = row.Skipped, ChallengeArea = row.ChallengeArea, TerrainEventId = row.TerrainEventId,
            StartTime = row.StartTime, EndTime = row.EndTime
        };
    }
}
