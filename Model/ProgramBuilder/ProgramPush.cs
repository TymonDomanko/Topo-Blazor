using System.Text.Json;
using System.Text.Json.Nodes;
using Topo.Model.Program;

namespace Topo.Model.ProgramBuilder
{
    // What Terrain answered to a write. StatusCode 0 means the request never got an answer (offline, blocked).
    public class TerrainWriteResult
    {
        public int StatusCode { get; set; }
        public string Body { get; set; } = string.Empty;
        public bool Success => StatusCode >= 200 && StatusCode < 300;
        public bool NotAllowed => StatusCode == 401 || StatusCode == 403;

        public string Message => StatusCode == 0 ? "Terrain gave no answer the browser would pass on (it could not be reached, or it answered with an error that the browser blocks); nothing was added"
            : NotAllowed ? "Terrain refused it (you may not have permission to add activities to this unit's calendar)"
            : $"Terrain answered {StatusCode}" + (Body.Trim() == "" ? "" : ": " + (Body.Trim().Length > 200 ? Body.Trim().Substring(0, 200) + "..." : Body.Trim()));
    }

    public enum ProgramPushOutcome
    {
        // Not sent: the push stopped before it
        NotTried,
        // Terrain took it, but it was not seen in Terrain's calendar when looked for
        Sent,
        // Terrain took it and it is there, with its id kept
        Created,
        // An activity already in Terrain, changed there, and seen to be as the program has it
        Updated,
        Failed
    }

    public enum ProgramPushKind
    {
        // A new activity on the unit's calendar
        Add,
        // An activity already in Terrain, changed to be as the program has it
        Update
    }

    // One activity that would be added to the unit's calendar in Terrain
    public class ProgramPushItem
    {
        public ProgramPushKind Kind { get; set; } = ProgramPushKind.Add;
        // For an update: the event in Terrain, and what is different
        public string EventId { get; set; } = string.Empty;
        public string Differences { get; set; } = string.Empty;
        // For an update: what is sent, as text, as it was made from what Terrain has
        public string BodyJson { get; set; } = string.Empty;
        public TerrainEvent Event { get; set; } = new TerrainEvent();
        // The activity in words, for the leader to read before it is sent
        public string Summary { get; set; } = string.Empty;
        // What is sent to Terrain
        public Dictionary<string, object?> Body { get; set; } = new Dictionary<string, object?>();
        public ProgramPushOutcome Outcome { get; set; } = ProgramPushOutcome.NotTried;
        public string Message { get; set; } = string.Empty;
    }

    // What a push would do, worked out from what Terrain has now; nothing has been sent
    public class ProgramPushPlan
    {
        public List<ProgramPushItem> Items { get; set; } = new List<ProgramPushItem>();
        // Activities in Terrain that differ from the program, to be changed to match it
        public List<ProgramPushItem> Updates { get; set; } = new List<ProgramPushItem>();
        // What is left alone, and why
        public List<(string What, string Reason)> Skipped { get; set; } = new List<(string What, string Reason)>();
        public List<string> Notes { get; set; } = new List<string>();
        // The ids of the events in Terrain now, so what is new afterwards can be told from them
        public HashSet<string> ExistingIds { get; set; } = new HashSet<string>();
        public string Error { get; set; } = string.Empty;
        public bool HasWork => Error == "" && (Items.Count > 0 || Updates.Count > 0);
    }

    public class ProgramPushResult
    {
        public List<ProgramPushItem> Items { get; set; } = new List<ProgramPushItem>();
        // Why it stopped early, if it did
        public string Stopped { get; set; } = string.Empty;
        public int SentCount => Items.Count(i => i.Outcome is ProgramPushOutcome.Sent or ProgramPushOutcome.Created or ProgramPushOutcome.Updated);
        public bool AllSent => Items.Count > 0 && SentCount == Items.Count;
    }

    // Adds the program's activities to the unit's calendar in Terrain: the ones the program has that Terrain does not. They are sent as planned
    // activities for the unit, one at a time, and only after the leader has seen the list. Changes to activities already in Terrain are left to be
    // made there.
    public static class ProgramPush
    {
        public static ProgramPushPlan Build(ProgramDraft draft, IEnumerable<GetEventResultModel> inTerrain, TerrainContext context, DateTime now)
        {
            var existing = inTerrain.ToList();
            var plan = new ProgramPushPlan { ExistingIds = existing.Select(e => e.id).ToHashSet() };
            foreach (var planned in ProgramTerrain.ToAdd(draft, existing))
            {
                var label = $"{planned.Row.Date:ddd d MMM} {planned.Title}";
                // Terrain records an activity that has already happened as concluded, with its attendance, which is for the leaders to do there
                if (planned.Start < now)
                {
                    plan.Skipped.Add((label, "is in the past, and an activity that has happened is recorded in Terrain after it, with who came"));
                    continue;
                }
                // Terrain wants at least one part of the Scout Method for an activity, and which ones is for the leader to say
                if (planned.ScoutMethod.Count == 0)
                {
                    plan.Skipped.Add((label, "has no part of the Scout Method chosen; choose at least one for it"));
                    continue;
                }
                var organisers = Organisers(planned, context);
                if (organisers.Unmatched.Count > 0)
                    plan.Notes.Add($"{label}: {string.Join(", ", organisers.Unmatched)} {(organisers.Unmatched.Count == 1 ? "is" : "are")} not a leader in the unit, so {(organisers.Ids.Count == 0 ? "no organiser is set" : "only the others are set as organisers")}.");
                if (planned.ChallengeArea == "")
                    plan.Notes.Add($"{label}: no challenge area is set, so it is sent as Not applicable.");
                plan.Items.Add(new ProgramPushItem { Event = planned, Summary = Summary(planned, organisers.Names), Body = Body(planned, context, organisers.Ids) });
            }
            // an activity that is in Terrain already but differs is a change to it: one that is still to come and has not been done is updated, and the
            // rest are left for Terrain
            foreach (var change in ProgramTerrain.Changes(draft, existing).Where(c => c.Action == "Change"))
            {
                var label = $"{change.Date:ddd d MMM} {change.Activity}";
                var found = existing.FirstOrDefault(e => e.id == change.EventId);
                var planned = change.Planned!;
                if (found == null || planned.Start < now)
                    plan.Skipped.Add((label, $"is in Terrain already but differs ({change.Details}), and is in the past; changes to what has happened are made in Terrain"));
                else if (!string.Equals(found.status, "planned", StringComparison.OrdinalIgnoreCase))
                    plan.Skipped.Add((label, $"is in Terrain already but differs ({change.Details}), and is {found.status}, not planned; changes to it are made in Terrain"));
                else if (planned.ScoutMethod.Count == 0 && found.review?.scout_method_elements?.Length == 0)
                    plan.Skipped.Add((label, $"differs in Terrain ({change.Details}), and has no part of the Scout Method chosen; choose at least one for it"));
                else
                    plan.Updates.Add(new ProgramPushItem { Kind = ProgramPushKind.Update, EventId = found.id, Differences = change.Details, Event = planned, Summary = $"{label}: {change.Details}" });
            }
            return plan;
        }

        // The activity as Terrain's create call takes it: planned, for the unit, with its leaders as organisers by member id. This is the request
        // Terrain's own site sends to plan an activity (captured when one was planned there, which it answered with 204), with the choices that
        // site would have the leader make left as it offers them: no stage or branch for a log book. The Scout Method elements are the activity's own.
        public static Dictionary<string, object?> Body(TerrainEvent e, TerrainContext context, IEnumerable<string>? organiserIds = null)
        {
            string Utc(DateTime local) => new DateTimeOffset(local, context.UtcOffset(local)).ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'.000+00:00'");
            var verifier = new { name = context.UserName, contact = "", type = "member" };
            return new Dictionary<string, object?>
            {
                ["title"] = e.Title, ["description"] = e.Description, ["justification"] = "",
                ["organisers"] = (organiserIds ?? Organisers(e, context).Ids).ToArray(),
                ["challenge_area"] = e.ChallengeArea == "" ? "not_applicable" : e.ChallengeArea,
                ["start_datetime"] = Utc(e.Start), ["end_datetime"] = Utc(e.End),
                ["event_type"] = new { type = "unit", id = context.UnitId },
                ["attendance"] = new { leader_member_ids = new string[0], assistant_member_ids = new string[0], attendee_member_ids = new string[0], participant_member_ids = new string[0] },
                ["schedule_items"] = new object[0],
                ["achievement_pathway_oas_data"] = new { award_rule = "individual", verifier, groups = new object[0] },
                ["achievement_pathway_logbook_data"] = new
                {
                    distance_travelled = 0, distance_walkabout = 0, achievement_meta = new { stream = "", branch = "" }, categories = new object[0],
                    details = new { activity_time_length = "", activity_grade = "" }, verifier
                },
                ["review"] = new
                {
                    general_tags = new string[0],
                    scout_method_elements = e.ScoutMethod.ToArray(),
                    scout_spices_elements = new string[0]
                },
                ["uploads"] = new object[0], ["equipment_notes"] = "", ["additional_notes"] = "",
                ["location"] = e.Location, ["iana_timezone"] = context.TimeZoneId, ["status"] = "planned"
            };
        }

        // The request that changes an activity already in Terrain: the whole of the activity as Terrain has it (read as it came), with what the program
        // says in place of it, as Terrain's own site sends an edit. Nothing Terrain has that the program does not know about is lost. Null, and why,
        // when the activity has something this cannot carry over safely.
        public static string? UpdateBody(string rawEvent, TerrainEvent planned, TerrainContext context, out string reason)
        {
            reason = "";
            var raw = JsonNode.Parse(rawEvent) as JsonObject;
            if (raw == null)
            {
                reason = "could not be read from Terrain";
                return null;
            }
            if (raw["uploads"] is JsonArray uploads && uploads.Count > 0)
            {
                reason = "has files attached in Terrain, so it is changed there";
                return null;
            }
            if (raw["schedule_items"] is JsonArray schedule && schedule.Count > 0)
            {
                reason = "has a timetable in Terrain, so it is changed there";
                return null;
            }

            string Text(string name) => raw[name] is JsonValue v && v.TryGetValue<string>(out var text) ? text : "";
            string[] Ids(JsonNode? members) => (members as JsonArray)?.Select(m => m?["id"] is JsonValue v && v.TryGetValue<string>(out var id) ? id : "").Where(i => i != "").ToArray() ?? new string[0];
            JsonArray Strings(IEnumerable<string> values) => new JsonArray(values.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray());
            string Utc(DateTime local) => new DateTimeOffset(local, context.UtcOffset(local)).ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'.000+00:00'");

            var description = Text("description");
            var wanted = Organisers(planned, context);
            // the leaders from the program when every one of them is in the unit; otherwise the ones Terrain has
            var organisers = planned.Leader != "" && wanted.Unmatched.Count == 0 && wanted.Ids.Count > 0 ? wanted.Ids.ToArray() : Ids(raw["organisers"]);

            var verifier = new JsonObject { ["name"] = context.UserName, ["contact"] = "", ["type"] = "member" };
            var oas = raw["achievement_pathway_oas_data"]?.DeepClone() as JsonObject
                ?? new JsonObject { ["award_rule"] = "individual", ["verifier"] = verifier.DeepClone(), ["groups"] = new JsonArray() };
            var logbook = raw["achievement_pathway_logbook_data"]?.DeepClone() as JsonObject ?? new JsonObject();
            logbook["verifier"] ??= oas["verifier"]?.DeepClone() ?? verifier.DeepClone();
            logbook["title"] ??= "";

            var review = raw["review"]?.DeepClone() as JsonObject ?? new JsonObject();
            review["general_tags"] ??= new JsonArray();
            review["scout_spices_elements"] ??= new JsonArray();
            if (planned.ScoutMethod.Count > 0)
                review["scout_method_elements"] = Strings(planned.ScoutMethod);
            review["scout_method_elements"] ??= new JsonArray();

            var attendance = raw["attendance"] as JsonObject;
            var body = new JsonObject
            {
                ["title"] = planned.Title,
                ["description"] = ProgramDescription.Merge(description, planned.Game != "" ? planned.Game : ProgramDescription.Game(description), planned.DutyPatrol != "" ? planned.DutyPatrol : ProgramDescription.Duty(description)),
                ["justification"] = Text("justification"),
                ["organisers"] = Strings(organisers),
                ["challenge_area"] = planned.ChallengeArea != "" ? planned.ChallengeArea : Text("challenge_area"),
                ["start_datetime"] = Utc(planned.Start), ["end_datetime"] = Utc(planned.End),
                ["event_type"] = new JsonObject { ["type"] = Text("owner_type") != "" ? Text("owner_type") : "unit", ["id"] = Text("owner_id") != "" ? Text("owner_id") : context.UnitId },
                ["attendance"] = new JsonObject
                {
                    ["leader_member_ids"] = Strings(Ids(attendance?["leader_members"])), ["assistant_member_ids"] = Strings(Ids(attendance?["assistant_members"])),
                    ["attendee_member_ids"] = Strings(Ids(attendance?["attendee_members"])), ["participant_member_ids"] = Strings(Ids(attendance?["participant_members"]))
                },
                ["schedule_items"] = new JsonArray(),
                ["achievement_pathway_oas_data"] = oas, ["achievement_pathway_logbook_data"] = logbook, ["review"] = review,
                ["uploads"] = new JsonArray(), ["equipment_notes"] = Text("equipment_notes"), ["additional_notes"] = Text("additional_notes"),
                ["location"] = planned.Location != "" ? planned.Location : Text("location"), ["iana_timezone"] = context.TimeZoneId,
                ["status"] = Text("status") != "" ? Text("status") : "planned"
            };
            return body.ToJsonString();
        }

        // The leaders in charge, as the unit's leaders by member id; a name that is not one of them is returned as unmatched
        public static (List<string> Ids, List<string> Names, List<string> Unmatched) Organisers(TerrainEvent e, TerrainContext context)
        {
            var ids = new List<string>();
            var names = new List<string>();
            var unmatched = new List<string>();
            foreach (var name in e.Leader.Split('/', ',').Select(n => n.Trim()).Where(n => n != ""))
            {
                var leader = context.Leaders.FirstOrDefault(l => string.Equals(l.FirstName, name, StringComparison.OrdinalIgnoreCase)
                    || string.Equals($"{l.FirstName} {l.LastName}".Trim(), name, StringComparison.OrdinalIgnoreCase));
                if (leader == null || leader.Id == "")
                    unmatched.Add(name);
                else if (!ids.Contains(leader.Id))
                {
                    ids.Add(leader.Id);
                    names.Add(name);
                }
            }
            return (ids, names, unmatched);
        }

        private static string Summary(TerrainEvent e, List<string> organiserNames)
        {
            var days = e.End.Date > e.Start.Date ? $"{e.Start:ddd d MMM HH:mm} to {e.End:ddd d MMM HH:mm}" : $"{e.Start:ddd d MMM HH:mm}-{e.End:HH:mm}";
            var parts = new List<string> { $"{e.Title}, {days}" };
            if (e.Location != "")
                parts.Add($"at {e.Location}");
            if (organiserNames.Count > 0)
                parts.Add($"led by {string.Join(" and ", organiserNames)}");
            if (e.ChallengeArea != "")
                parts.Add(ProgramLayout.AreaLabel(e.ChallengeArea));
            return string.Join(", ", parts);
        }

        // Sends each in turn, in date order, and stops at the first that Terrain does not take, so a mistake in what is sent is not made again and
        // again, and what was not tried can be pushed again. The send is a function so that what it answers can be tried.
        public static async Task<ProgramPushResult> Run(IEnumerable<ProgramPushItem> items, Func<ProgramPushItem, Task<TerrainWriteResult>> send, Action<string>? progress = null)
        {
            var result = new ProgramPushResult { Items = items.ToList() };
            var number = 0;
            foreach (var item in result.Items)
            {
                number++;
                progress?.Invoke($"{(item.Kind == ProgramPushKind.Update ? "Updating" : "Adding")} {number} of {result.Items.Count}: {item.Event.Title}");
                var answer = await send(item);
                if (answer.Success)
                {
                    item.Outcome = ProgramPushOutcome.Sent;
                    continue;
                }
                item.Outcome = ProgramPushOutcome.Failed;
                item.Message = answer.Message;
                result.Stopped = answer.NotAllowed
                    ? "Terrain would not let you change this unit's calendar, so nothing more was sent."
                    : $"Stopped at {item.Event.Title}: nothing after it was sent.";
                break;
            }
            return result;
        }

        // Finds each activity sent in what Terrain has now (a new event that day with the same title), keeping its id on the program's row so it is
        // not added again. One that is not found is left as sent.
        public static void MatchCreated(ProgramPushResult result, IEnumerable<GetEventResultModel> inTerrain, HashSet<string> existingIds, ProgramDraft? draft = null)
        {
            var fresh = inTerrain.Where(e => !existingIds.Contains(e.id) && !e.status.Contains("cancel", StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var item in result.Items.Where(i => i.Kind == ProgramPushKind.Add && i.Outcome == ProgramPushOutcome.Sent))
            {
                var found = fresh.FirstOrDefault(e => Local(e.start_datetime).Date == item.Event.Row.Date.Date && string.Equals(e.title.Trim(), item.Event.Title, StringComparison.OrdinalIgnoreCase));
                if (found == null)
                {
                    item.Message = "sent, but not seen in Terrain's calendar yet";
                    continue;
                }
                fresh.Remove(found);
                item.Event.Row.TerrainEventId = found.id;
                // a section night's row is a copy made for the plan, so the id is put on what the program keeps for the night as well
                if (draft != null && item.Event.Row.Kind == ProgramRowKind.SectionNight && draft.NightEdits.TryGetValue(ProgramPlanner.NightKey(item.Event.Row.Date), out var kept))
                    kept.TerrainEventId = found.id;
                item.Outcome = ProgramPushOutcome.Created;
            }
        }

        // Looks at Terrain after the updates: one that now matches the program is done, and one that still differs is left as sent
        public static void MatchUpdated(ProgramPushResult result, ProgramDraft draft, IEnumerable<GetEventResultModel> inTerrain)
        {
            var stillDiffering = ProgramTerrain.Changes(draft, inTerrain.ToList()).Where(c => c.Action == "Change").Select(c => c.EventId).ToHashSet();
            foreach (var item in result.Items.Where(i => i.Kind == ProgramPushKind.Update && i.Outcome == ProgramPushOutcome.Sent))
            {
                if (stillDiffering.Contains(item.EventId))
                    item.Message = "sent, but Terrain still shows it differently";
                else
                    item.Outcome = ProgramPushOutcome.Updated;
            }
        }

        private static DateTime Local(DateTime value) => value.Kind == DateTimeKind.Utc ? value.ToLocalTime() : value;
    }
}
