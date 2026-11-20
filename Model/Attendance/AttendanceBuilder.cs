using Topo.Model.History;
using Topo.Model.Members;
using Topo.Model.Program;
using Topo.Model.Progress;
using Topo.Model.UnitReport;

namespace Topo.Model.Attendance
{
    // The kinds of event the attendance report counts separately. Terrain does not record an activity type, so each is worked out
    // from the event itself.
    public enum AttendanceKind
    {
        // On the unit's regular meeting night
        ScoutNight,
        // A single day event on any other night or day
        Other,
        // Runs over one night or more
        Camp,
        // Has a hike in its logbook data, and is not on the meeting night
        Hike,
        // The title says it is a unit council
        UnitCouncil
    }

    public class AttendanceEvent
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public DateTime Start { get; set; }
        public string ChallengeArea { get; set; } = string.Empty;
        public AttendanceKind Kind { get; set; }
        // Who was recorded as attending: member number, or the id for someone without one
        public HashSet<string> Attended { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public class AttendanceInput
    {
        public string UnitName { get; set; } = string.Empty;
        public string Section { get; set; } = string.Empty;
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public DateTime AsOf { get; set; } = DateTime.Now;
        public List<MemberListModel> Members { get; set; } = new List<MemberListModel>();
        public List<UnitEventInput> Events { get; set; } = new List<UnitEventInput>();
        // When each youth was invested into the section (member id), where Terrain has it
        public Dictionary<string, DateTime> InvestedOn { get; set; } = new Dictionary<string, DateTime>();
    }

    // What was found for a unit and period, before it is laid out: who came to which events
    public class AttendanceData
    {
        public string UnitName { get; set; } = string.Empty;
        public string Section { get; set; } = string.Empty;
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public DateTime AsAt { get; set; }
        // The youth, including the unit leader, patrol leaders and assistant patrol leaders
        public List<MemberListModel> Youth { get; set; } = new List<MemberListModel>();
        // The events that have happened in the period, oldest first
        public List<AttendanceEvent> Events { get; set; } = new List<AttendanceEvent>();
        // Events nobody was recorded at, left out because they would count as everyone missing
        public int EventsLeftOut { get; set; }
        public DayOfWeek? MeetingDay { get; set; }
        public Dictionary<string, DateTime> InvestedOn { get; set; } = new Dictionary<string, DateTime>();

        public static string Key(MemberListModel member) => AttendanceBuilder.PersonKey(member.member_number, member.id);

        // When a youth joined the section, so the events before then are not held against them: the earlier of their investiture and the
        // first event they came to. Null when there is neither, and then every event counts.
        public DateTime? JoinedOn(MemberListModel member)
        {
            var key = Key(member);
            var first = Events.Where(e => e.Attended.Contains(key)).Select(e => (DateTime?)e.Start.Date).OrderBy(d => d).FirstOrDefault();
            DateTime? invested = InvestedOn.TryGetValue(member.id, out var date) ? date : null;
            return invested == null ? first : first == null ? invested : (first < invested ? first : invested);
        }

        // The events a youth could have come to: those since they joined the section
        public List<AttendanceEvent> PossibleFor(MemberListModel member, Func<AttendanceEvent, bool>? counts = null)
        {
            var joined = JoinedOn(member);
            return Events.Where(e => (joined == null || e.Start.Date >= joined.Value) && (counts == null || counts(e))).ToList();
        }
    }

    public static class AttendanceBuilder
    {
        public static AttendanceData Analyse(AttendanceInput input)
        {
            var data = new AttendanceData
            {
                UnitName = input.UnitName, Section = input.Section, From = input.From, To = input.To, AsAt = input.AsOf, InvestedOn = input.InvestedOn,
                Youth = input.Members.Where(m => !UnitReportBuilder.IsLeader(m)).ToList()
            };

            var seen = new HashSet<string>();
            var counted = new List<(GetEventResultModel Event, HashSet<string> Attended)>();
            foreach (var item in input.Events)
            {
                var evt = item.Event;
                if (!seen.Add(evt.id))
                    continue;
                // only events that have happened in the period, not cancelled, and for the whole unit
                if (evt.start_datetime.Date < input.From.Date || evt.start_datetime.Date > input.To.Date || evt.start_datetime > input.AsOf)
                    continue;
                if (evt.status.Contains("cancel", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (item.Scope == "patrol" || item.Scope == "member")
                    continue;

                var attended = Attendees(evt);
                if (attended.Count == 0)
                {
                    data.EventsLeftOut++;
                    continue;
                }
                counted.Add((evt, attended));
            }

            // The meeting night is the day most single day events fall on, leaving out hikes and unit councils
            data.MeetingDay = counted
                .Where(c => IsSingleDay(c.Event) && !IsHike(c.Event) && !IsUnitCouncil(c.Event))
                .GroupBy(c => c.Event.start_datetime.DayOfWeek)
                .OrderByDescending(g => g.Count()).ThenBy(g => ((int)g.Key + 6) % 7)
                .Select(g => (DayOfWeek?)g.Key)
                .FirstOrDefault();

            data.Events = counted
                .OrderBy(c => c.Event.start_datetime)
                .Select(c => new AttendanceEvent
                {
                    Id = c.Event.id, Title = c.Event.title, Start = c.Event.start_datetime, ChallengeArea = c.Event.challenge_area,
                    Kind = Classify(c.Event, data.MeetingDay), Attended = c.Attended
                })
                .ToList();
            return data;
        }

        // When a youth was invested into the section: the earliest awarded Introduction to Section for it
        public static DateTime? InvestedOn(GetIntroductionResultsModel introductions, string section)
        {
            var key = (section ?? "").Trim().ToLowerInvariant();
            return introductions.results
                .Where(r => r.status == "awarded" && (r.section ?? "").Trim().ToLowerInvariant() == key)
                .Select(r => (DateTime?)ScoutingHistoryBuilder.AwardedDate(r.date_awarded, r.imported?.date_awarded, r.status_updated))
                .OrderBy(d => d).FirstOrDefault();
        }

        public static AttendanceKind Classify(GetEventResultModel evt, DayOfWeek? meetingDay)
        {
            if (!IsSingleDay(evt))
                return AttendanceKind.Camp;
            if (IsUnitCouncil(evt))
                return AttendanceKind.UnitCouncil;
            // a hike on the meeting night is that week's Scout night
            if (meetingDay != null && evt.start_datetime.DayOfWeek == meetingDay)
                return AttendanceKind.ScoutNight;
            return IsHike(evt) ? AttendanceKind.Hike : AttendanceKind.Other;
        }

        private static bool IsSingleDay(GetEventResultModel evt) => evt.end_datetime.Date <= evt.start_datetime.Date;

        private static bool IsHike(GetEventResultModel evt) =>
            (evt.achievement_pathway_logbook_data?.categories ?? new object[0]).Any(c => string.Equals(c?.ToString(), "walking_hike", StringComparison.OrdinalIgnoreCase));

        private static bool IsUnitCouncil(GetEventResultModel evt) => evt.title.Contains("unit council", StringComparison.OrdinalIgnoreCase);

        // Everyone recorded as attending, once
        private static HashSet<string> Attendees(GetEventResultModel evt)
        {
            var attendance = evt.attendance;
            return (attendance?.attendee_members ?? new Attendance_Members[0])
                .Concat(attendance?.participant_members ?? new Attendance_Members[0])
                .Concat(attendance?.assistant_members ?? new Attendance_Members[0])
                .Concat(attendance?.leader_members ?? new Attendance_Members[0])
                .Select(a => PersonKey(a.member_number, a.id))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        internal static string PersonKey(string memberNumber, string id) => string.IsNullOrEmpty(memberNumber) ? id : memberNumber;
    }
}
