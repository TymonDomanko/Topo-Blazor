using Topo.Model.Program;

namespace Topo.Model.Approvals
{
    // An event the leader can pick to say who was there when the awards were presented
    public class AwardEventChoice
    {
        public string Id { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public DateTime Start { get; init; }
        public DateTime End { get; init; }
        public string Scope { get; init; } = string.Empty;

        public string Display => $"{Title} ({Scope}, {Time(Start)} - {Time(End)})";

        // 7:00 pm, the same on every device
        private static string Time(DateTime time) =>
            time.ToString("h:mm", System.Globalization.CultureInfo.InvariantCulture) + (time.Hour < 12 ? " am" : " pm");

        // The unit's events: events for the unit, and events for the whole group. Events for a patrol or a single member
        // (projects) are not the whole unit's, so they are left out. Earliest first.
        public static List<AwardEventChoice> ForUnit(IEnumerable<Event> events, string unitId) =>
            events
                .Where(e => (e.invitee_type == "unit" && e.invitee_id == unitId) || e.invitee_type == "group")
                .OrderBy(e => e.start_datetime)
                .ThenBy(e => e.title, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(e => e.id, StringComparer.Ordinal)
                .Select(e => new AwardEventChoice
                {
                    Id = e.id,
                    Title = e.title,
                    Start = e.start_datetime,
                    End = e.end_datetime,
                    Scope = e.invitee_type == "group" ? "Group" : "Unit"
                })
                .ToList();

        // The ones that start on the given day
        public static List<AwardEventChoice> On(IEnumerable<AwardEventChoice> choices, DateTime date) =>
            choices.Where(c => c.Start.Date == date.Date).ToList();

        // The days that had an event
        public static HashSet<DateTime> Dates(IEnumerable<AwardEventChoice> choices) =>
            choices.Select(c => c.Start.Date).ToHashSet();

        // Who was there: the event's attendees. This is the same list the attendance report counts as attended.
        public static HashSet<string> Attended(GetEventResultModel? ev) =>
            ev?.attendance?.attendee_members == null
                ? new HashSet<string>()
                : new HashSet<string>(ev.attendance.attendee_members.Select(m => m.id).Where(id => id.Length > 0));
    }
}
