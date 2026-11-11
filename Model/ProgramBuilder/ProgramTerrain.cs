using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Topo.Model.Program;

namespace Topo.Model.ProgramBuilder
{
    // A leader of the unit, who can be put down as an organiser of an activity in Terrain
    public class TerrainLeader
    {
        public string Id { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string MemberNumber { get; set; } = string.Empty;
        public string PatrolName { get; set; } = string.Empty;
    }

    public class TerrainContext
    {
        public string UnitId { get; set; } = string.Empty;
        public string UnitName { get; set; } = string.Empty;
        public List<TerrainLeader> Leaders { get; set; } = new List<TerrainLeader>();
        // The time zone the program's times are in, for working out the times Terrain stores (UTC); the local one by default
        public Func<DateTime, TimeSpan> UtcOffset { get; set; } = local => TimeZoneInfo.Local.GetUtcOffset(local);
    }

    // An activity as it would be entered in Terrain
    public class TerrainEvent
    {
        public ProgramRow Row { get; set; } = new ProgramRow();
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string ChallengeArea { get; set; } = string.Empty;
        public string Leader { get; set; } = string.Empty;
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
    }

    // What has to be done in Terrain to make it match the program
    public class ProgramChange
    {
        // "Add", "Change" or "Not in the program"
        public string Action { get; set; } = string.Empty;
        public DateTime Date { get; set; }
        public DateTime? EndDate { get; set; }
        public string Activity { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string Leader { get; set; } = string.Empty;
        // What is different from what Terrain has, or what to do
        public string Details { get; set; } = string.Empty;
    }

    // Puts the program beside what is already in Terrain: lists what to add or change there, and writes an activity for Terrain's import for
    // each one to add. Terrain's import takes one activity to a file, in the same form it exports one.
    public static class ProgramTerrain
    {
        public static List<TerrainEvent> Events(ProgramDraft draft)
        {
            var rows = ProgramPlanner.Nights(draft).Where(n => !n.Skipped).Concat(draft.Extras).Where(r => r.Activity.Trim() != "").OrderBy(r => r.Date);
            return rows.Select(row =>
            {
                var camp = row.Kind == ProgramRowKind.Camp;
                var night = row.Kind == ProgramRowKind.SectionNight;
                var startTime = First(row.StartTime, night ? draft.NightStart : camp ? draft.CampStart : draft.OtherStart);
                var endTime = First(row.EndTime, night ? draft.NightEnd : camp ? draft.CampEnd : draft.OtherEnd);
                var lastDay = (row.EndDate ?? row.Date).Date;
                return new TerrainEvent
                {
                    Row = row, Title = row.Activity.Trim(), Description = row.Game.Trim() == "" ? "" : $"Game: {row.Game.Trim()}",
                    Location = First(row.Location, draft.DefaultLocation), ChallengeArea = First(row.ChallengeArea, draft.DefaultChallengeArea),
                    Leader = First(row.Leader, draft.DefaultLeader),
                    Start = row.Date.Date + Time(startTime), End = lastDay + Time(endTime)
                };
            }).ToList();
        }

        // What to do in Terrain: an activity with nothing in Terrain on its day is added, one that is there with something different is
        // changed, and what Terrain has in the program's dates that the program has not is flagged. A row filled in from Terrain and not
        // altered since needs nothing.
        public static List<ProgramChange> Changes(ProgramDraft draft, IEnumerable<GetEventResultModel> inTerrain)
        {
            var existing = inTerrain
                .Where(e => !e.status.Contains("cancel", StringComparison.OrdinalIgnoreCase))
                .Where(e => Local(e.start_datetime).Date >= draft.Start.Date && Local(e.start_datetime).Date <= draft.End.Date)
                .ToList();
            var matched = new HashSet<string>();
            var changes = new List<ProgramChange>();

            foreach (var planned in Events(draft))
            {
                var found = FindIn(existing, planned, matched);
                if (found == null)
                {
                    changes.Add(Change("Add", planned, "Not in Terrain"));
                    continue;
                }
                matched.Add(found.id);
                var differences = new List<string>();
                if (Local(found.start_datetime).Date != planned.Row.Date.Date)
                    differences.Add($"Date: {Local(found.start_datetime):ddd d MMM} to {planned.Row.Date:ddd d MMM}");
                if (!string.Equals(found.title.Trim(), planned.Title, StringComparison.OrdinalIgnoreCase))
                    differences.Add($"Activity: {found.title.Trim()} to {planned.Title}");
                if (planned.Location != "" && !string.Equals(found.location.Trim(), planned.Location, StringComparison.OrdinalIgnoreCase))
                    differences.Add($"Location: {(found.location.Trim() == "" ? "none" : found.location.Trim())} to {planned.Location}");
                if (planned.Leader != "" && !LeaderMatches(found, planned.Leader))
                    differences.Add($"Leader in charge: {(OrganiserNames(found) == "" ? "none" : OrganiserNames(found))} to {planned.Leader}");
                if (differences.Count > 0)
                    changes.Add(Change("Change", planned, string.Join("; ", differences)));
            }

            foreach (var left in existing.Where(e => !matched.Contains(e.id)).OrderBy(e => e.start_datetime))
                changes.Add(new ProgramChange
                {
                    Action = "Not in the program", Date = Local(left.start_datetime).Date, EndDate = Local(left.end_datetime).Date > Local(left.start_datetime).Date ? Local(left.end_datetime).Date : null,
                    Activity = left.title.Trim(), Location = left.location.Trim(), Leader = OrganiserNames(left),
                    Details = "In Terrain on a day the program has nothing for; cancel it there if it is not running"
                });
            return changes.OrderBy(c => c.Date).ThenBy(c => c.Action).ToList();
        }

        // The ones to put in Terrain: those the program has that Terrain does not, and that were not filled in from it
        public static List<TerrainEvent> ToAdd(ProgramDraft draft, IEnumerable<GetEventResultModel>? inTerrain)
        {
            var events = Events(draft).Where(e => e.Row.TerrainEventId == "").ToList();
            if (inTerrain == null)
                return events;
            var adds = Changes(draft, inTerrain).Where(c => c.Action == "Add").Select(c => (c.Date, c.Activity)).ToHashSet();
            return events.Where(e => adds.Contains((e.Row.Date.Date, e.Title))).ToList();
        }

        public static string FileName(TerrainEvent e) =>
            $"ScoutsTerrain-activity-{e.Row.Date:yyyy-MM-dd}-{Regex.Replace(e.Title, "[^A-Za-z0-9]+", "-").Trim('-')}.json";

        // The activity in the form Terrain's own export has, with what the program does not know left empty
        public static string EventJson(TerrainEvent e, TerrainContext context)
        {
            var leader = context.Leaders.FirstOrDefault(l => string.Equals(l.FirstName, e.Leader, StringComparison.OrdinalIgnoreCase)
                || (e.Leader != "" && e.Leader.Split('/', ',').Any(part => string.Equals(l.FirstName, part.Trim(), StringComparison.OrdinalIgnoreCase))));
            object? organiser = leader == null ? null : new { id = leader.Id, first_name = leader.FirstName, last_name = leader.LastName, member_number = leader.MemberNumber, patrol_name = leader.PatrolName };
            var invitee = new { invitee_id = context.UnitId, invitee_type = "unit", invitee_name = context.UnitName, id = context.UnitId };
            string Utc(DateTime local) => new DateTimeOffset(local, context.UtcOffset(local)).ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'+00:00'");

            var payload = new Dictionary<string, object?>
            {
                ["title"] = e.Title, ["description"] = e.Description, ["justification"] = "", ["leader"] = "", ["location"] = e.Location,
                ["challenge_area"] = e.ChallengeArea,
                ["startDate"] = e.Start.ToString("yyyy-MM-dd"), ["endDate"] = e.End.ToString("yyyy-MM-dd"),
                ["startTime"] = e.Start.ToString("HH:mm"), ["endTime"] = e.End.ToString("HH:mm"),
                ["start_datetime"] = Utc(e.Start), ["end_datetime"] = Utc(e.End),
                ["review"] = new { scout_method_elements = new string[0] },
                ["organisers"] = organiser == null ? new object[0] : new[] { organiser },
                ["uploads"] = new object[0], ["schedule_items"] = new object[0],
                ["invitees"] = new[] { invitee }, ["invited_member_ids"] = new string[0],
                ["attendance"] = new { leader_members = new object[0], assistant_members = new object[0], participant_members = new object[0], attendee_members = new object[0] },
                ["equipment_notes"] = "", ["additional_notes"] = "",
                ["achievement_pathway_oas_data"] = new { award_rule = "individual", verifier = new { name = "", contact = "", type = "member" }, groups = new object[0] },
                ["achievement_pathway_logbook_data"] = new
                {
                    distance_travelled = 0, distance_walkabout = 0, achievement_meta = new { stream = "", branch = "" }, categories = new object[0],
                    details = new { activity_time_length = "", activity_grade = "" }, title = ""
                },
                ["id"] = "", ["status"] = "",
                ["organiser"] = organiser ?? new { id = "", first_name = "", last_name = "", member_number = "", patrol_name = "" },
                ["owner_type"] = "unit", ["owner_id"] = context.UnitId
            };
            return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        }

        // The files for Terrain's import, a zip with an activity to a file
        public static byte[] Zip(IEnumerable<TerrainEvent> events, TerrainContext context)
        {
            using var stream = new MemoryStream();
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                var used = new HashSet<string>();
                foreach (var e in events)
                {
                    var name = FileName(e);
                    for (var n = 2; !used.Add(name); n++)
                        name = FileName(e).Replace(".json", $"-{n}.json");
                    using var writer = new StreamWriter(zip.CreateEntry(name, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
                    writer.Write(EventJson(e, context));
                }
            }
            return stream.ToArray();
        }

        // The changes as a spreadsheet of text, for the same use as the Patrol Planner's changes to apply in Terrain
        public static string Csv(IEnumerable<ProgramChange> changes)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Action,Date,End Date,Activity,Location,Leader in Charge,Details");
            foreach (var c in changes)
                sb.AppendLine(string.Join(",", Field(c.Action), c.Date.ToString("yyyy-MM-dd"), c.EndDate?.ToString("yyyy-MM-dd") ?? "", Field(c.Activity), Field(c.Location), Field(c.Leader), Field(c.Details)));
            return sb.ToString();
        }

        private static string Field(string value) => value.Contains(',') || value.Contains('"') || value.Contains('\n') ? $"\"{value.Replace("\"", "\"\"")}\"" : value;

        private static ProgramChange Change(string action, TerrainEvent planned, string details) => new ProgramChange
        {
            Action = action, Date = planned.Row.Date.Date, EndDate = planned.Row.EndDate != null && planned.Row.EndDate.Value.Date > planned.Row.Date.Date ? planned.Row.EndDate.Value.Date : null,
            Activity = planned.Title, Location = planned.Location, Leader = planned.Leader, Details = details
        };

        // The event Terrain has for a planned one: the one it was filled in from, else one starting that day, preferring the same title
        private static GetEventResultModel? FindIn(List<GetEventResultModel> existing, TerrainEvent planned, HashSet<string> matched)
        {
            if (planned.Row.TerrainEventId != "")
            {
                var byId = existing.FirstOrDefault(e => e.id == planned.Row.TerrainEventId);
                if (byId != null)
                    return byId;
            }
            var sameDay = existing.Where(e => !matched.Contains(e.id) && Local(e.start_datetime).Date == planned.Row.Date.Date).ToList();
            return sameDay.FirstOrDefault(e => string.Equals(e.title.Trim(), planned.Title, StringComparison.OrdinalIgnoreCase)) ?? sameDay.FirstOrDefault();
        }

        private static bool LeaderMatches(GetEventResultModel found, string leader)
        {
            var names = leader.Split('/', ',').Select(n => n.Trim()).Where(n => n != "").ToList();
            var organisers = found.organisers.Concat(new[] { found.organiser }).Select(o => (o.first_name ?? "").Trim()).Where(n => n != "").ToList();
            return names.All(n => organisers.Any(o => string.Equals(o, n, StringComparison.OrdinalIgnoreCase)));
        }

        private static string OrganiserNames(GetEventResultModel found) =>
            string.Join("/", found.organisers.Select(o => o.first_name).Concat(found.organisers.Length == 0 ? new[] { found.organiser.first_name } : new string[0]).Where(n => !string.IsNullOrWhiteSpace(n)));

        private static DateTime Local(DateTime value) => value.Kind == DateTimeKind.Utc ? value.ToLocalTime() : value;

        private static string First(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? (fallback ?? "").Trim() : value.Trim();

        private static TimeSpan Time(string value) => TimeSpan.TryParse(value, out var time) ? time : TimeSpan.Zero;
    }
}
