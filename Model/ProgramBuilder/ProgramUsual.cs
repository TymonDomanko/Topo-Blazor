using System.Text.RegularExpressions;
using Topo.Model.Program;

namespace Topo.Model.ProgramBuilder
{
    // What a unit's calendar says about when it usually meets; a blank time is one there was not enough in the calendar to say
    public class ProgramUsual
    {
        public DayOfWeek? Day { get; set; }
        public string NightStart { get; set; } = string.Empty;
        public string NightEnd { get; set; } = string.Empty;
        public string CampStart { get; set; } = string.Empty;
        public string CampEnd { get; set; } = string.Empty;
        public string OtherStart { get; set; } = string.Empty;
        public string OtherEnd { get; set; } = string.Empty;

        // The section night and the usual times from the activities of the unit or group (not a patrol or a member's project): the times that most
        // section nights start and end at, the times that camps (anything over more than one day) start on the first day and end on the last, and
        // the times other single day activities (not meetings) start and end at. Of two times as common, the one used more recently wins. A section night needs
        // two to agree, as it does for finding the day.
        public static ProgramUsual Find(IEnumerable<Event> events)
        {
            var found = new ProgramUsual();
            var all = events
                .Where(e => !e.status.Contains("cancel", StringComparison.OrdinalIgnoreCase))
                .Where(e => e.invitee_type != "patrol" && e.invitee_type != "member")
                .Select(e => (Start: Local(e.start_datetime), End: Local(e.end_datetime), e.title))
                .ToList();
            found.Day = ProgramPlanner.FindSectionNight(events);

            var camps = all.Where(e => e.End.Date > e.Start.Date).ToList();
            found.CampStart = Usual(camps.Select(e => (e.Start.Date, Time(e.Start))), 1);
            found.CampEnd = Usual(camps.Select(e => (e.Start.Date, Time(e.End))), 1);

            var single = all.Where(e => e.End.Date <= e.Start.Date && e.End.TimeOfDay > e.Start.TimeOfDay && !Regex.IsMatch(e.title, "unit council", RegexOptions.IgnoreCase)).ToList();
            if (found.Day != null)
            {
                var nights = single.Where(e => e.Start.DayOfWeek == found.Day && !Regex.IsMatch(e.title, "camp|hike", RegexOptions.IgnoreCase)).ToList();
                found.NightStart = Usual(nights.Select(e => (e.Start.Date, Time(e.Start))), 2);
                found.NightEnd = Usual(nights.Select(e => (e.Start.Date, Time(e.End))), 2);
            }

            // leaders' meetings are in the evening and say nothing about when the unit's activities are
            var others = single.Where(e => e.Start.DayOfWeek != found.Day && !Regex.IsMatch(e.title, "meeting", RegexOptions.IgnoreCase)).ToList();
            found.OtherStart = Usual(others.Select(e => (e.Start.Date, Time(e.Start))), 1);
            found.OtherEnd = Usual(others.Select(e => (e.Start.Date, Time(e.End))), 1);
            return found;
        }

        private static string Usual(IEnumerable<(DateTime Date, string Time)> times, int least)
        {
            var best = times.GroupBy(t => t.Time).Select(g => (Time: g.Key, Count: g.Count(), Last: g.Max(t => t.Date)))
                .OrderByDescending(g => g.Count).ThenByDescending(g => g.Last).FirstOrDefault();
            return best.Count >= least ? best.Time : "";
        }

        private static string Time(DateTime value) => value.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);

        private static DateTime Local(DateTime value) => value.Kind == DateTimeKind.Utc ? value.ToLocalTime() : value;
    }
}
