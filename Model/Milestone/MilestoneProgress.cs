using System.Globalization;
using System.Text.Json;
using Topo.Model.Members;
using Topo.Model.SiaProgress;

namespace Topo.Model.Milestone
{
    public class MilestoneAreaCounts
    {
        public int Community { get; set; }
        public int Outdoors { get; set; }
        public int Creative { get; set; }
        public int PersonalGrowth { get; set; }
        public int Total => Community + Outdoors + Creative + PersonalGrowth;
    }

    // One event Terrain counted towards a milestone
    public class MilestoneEventCredit
    {
        // Terrain's name for the credit, "participant", "assistant" or "leader"
        public string Credit { get; set; } = string.Empty;
        public string Area { get; set; } = string.Empty;
        public string EventId { get; set; } = string.Empty;
        public string EventName { get; set; } = string.Empty;
        public DateTime Date { get; set; }
    }

    // A Scout's milestone record as Terrain holds it, with the answers they saved on the milestone review
    public class MilestoneProgressRecord
    {
        public string Id { get; set; } = string.Empty;
        public string MemberId { get; set; } = string.Empty;
        public string Section { get; set; } = string.Empty;
        public int Stage { get; set; }
        public string Status { get; set; } = string.Empty;
        // Terrain's own verdict: "complete" once the participates and leads asked for are in
        public string RequirementStatus { get; set; } = string.Empty;
        // The review form the answers were written on, "milestone/scout/review/1" and its version; blank on a record Terrain imported
        public string Template { get; set; } = string.Empty;
        public int Version { get; set; }
        public DateTime StatusUpdated { get; set; }
        // By the form's input ids; a selector's chosen options are joined with commas
        public Dictionary<string, string> Answers { get; set; } = new Dictionary<string, string>();
        public MilestoneAreaCounts Participant { get; set; } = new MilestoneAreaCounts();
        public MilestoneAreaCounts Assistant { get; set; } = new MilestoneAreaCounts();
        public MilestoneAreaCounts Leader { get; set; } = new MilestoneAreaCounts();
        public List<MilestoneEventCredit> Events { get; set; } = new List<MilestoneEventCredit>();

        // The review form this record's answers are on, as Terrain's templates are named. Blank when it has none.
        public string FormKey => Template == "" || Version <= 0 ? "" : $"{Template}|{Version}";
    }

    // What a milestone asks for: Participates in each Challenge Area, and Leads across any
    public static class MilestoneTargets
    {
        public static int Participates(int stage) => stage switch { 1 => 6, 2 => 5, 3 => 4, _ => 0 };

        public static int Leads(int stage) => stage switch { 1 => 2, 2 => 4, 3 => 6, _ => 0 };
    }

    public class MilestoneAreaProgress
    {
        public string Area { get; set; } = string.Empty;
        public int Count { get; set; }
        public int Target { get; set; }
        public bool Met => Count >= Target;
    }

    public class MilestoneEventLine
    {
        public DateTime Date { get; set; }
        public string Event { get; set; } = string.Empty;
        public string Area { get; set; } = string.Empty;
        // "Participated" or "Led"
        public string Credit { get; set; } = string.Empty;
    }

    public class MilestoneReviewAnswer
    {
        public string Question { get; set; } = string.Empty;
        public string Answer { get; set; } = string.Empty;
    }

    // One Scout's page: where they are up to for a milestone, the events that counted, and what they wrote on the review
    public class MilestoneScoutPage
    {
        public string MemberId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Patrol { get; set; } = string.Empty;
        // The milestone the page is for, 1 to 3
        public int Stage { get; set; }
        // In words: Ready, In progress, Review drafted, In review, Approved, Awarded
        public string Status { get; set; } = string.Empty;
        // The Participates and Leads are in and the milestone is not awarded yet
        public bool Ready { get; set; }
        public List<MilestoneAreaProgress> Areas { get; set; } = new List<MilestoneAreaProgress>();
        public MilestoneAreaProgress Leads { get; set; } = new MilestoneAreaProgress { Area = "Leads" };
        public List<MilestoneEventLine> Events { get; set; } = new List<MilestoneEventLine>();
        // The questions on the review form, as they were worded when it was written, with the answers given
        public List<MilestoneReviewAnswer> Review { get; set; } = new List<MilestoneReviewAnswer>();
        // What the Scout wrote about the events they led or helped with
        public List<MilestoneReviewAnswer> EventReviews { get; set; } = new List<MilestoneReviewAnswer>();
    }

    public class MilestoneProgressReport
    {
        public string UnitName { get; set; } = string.Empty;
        public string Section { get; set; } = string.Empty;
        // 1 to 3, or MilestoneProgressBuilder.AllMilestones for all three
        public int Stage { get; set; }
        public DateTime GeneratedAt { get; set; }
        public bool OnlyReady { get; set; }
        // In order of milestone, then in the order the unit is listed
        public List<MilestoneScoutPage> Pages { get; set; } = new List<MilestoneScoutPage>();
        // How many Scouts' milestones there are to choose from (a Scout with all three counts three), so the page can say how many were left out
        public int ScoutsWithTheMilestone { get; set; }
    }

    public class MilestoneProgressMember
    {
        public string MemberId { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Patrol { get; set; } = string.Empty;
    }

    public class MilestoneProgressInput
    {
        public string UnitName { get; set; } = string.Empty;
        public string Section { get; set; } = string.Empty;
        // 1 to 3, or MilestoneProgressBuilder.AllMilestones
        public int Stage { get; set; }
        // Only the Scouts whose Participates and Leads are in and whose milestone is not awarded yet
        public bool OnlyReady { get; set; } = true;
        public bool UseFullNames { get; set; }
        // The unit's youth, in the order the unit is listed
        public List<MilestoneProgressMember> Members { get; set; } = new List<MilestoneProgressMember>();
        public List<MilestoneProgressRecord> Records { get; set; } = new List<MilestoneProgressRecord>();
        // The review forms the records' answers are on, by MilestoneProgressRecord.FormKey; they are the same shape as the SIA ones,
        // the questions in order and the wording of the options a Scout picks from
        public Dictionary<string, SiaTemplate> Forms { get; set; } = new Dictionary<string, SiaTemplate>();
        public DateTime GeneratedAt { get; set; }
    }

    public static class MilestoneProgressBuilder
    {
        // The choice of every milestone: a page for each Scout for each of the three
        public const int AllMilestones = 0;

        public static readonly int[] Milestones = { 1, 2, 3 };

        // The one record to use for a Scout's milestone: one that is awarded beats one that is not, then the latest change
        public static MilestoneProgressRecord? RecordFor(IEnumerable<MilestoneProgressRecord> records, string memberId, string section, int stage) =>
            records
                .Where(r => r.MemberId == memberId && r.Stage == stage && string.Equals(r.Section, section, StringComparison.OrdinalIgnoreCase) && r.Status != "not_required")
                .OrderByDescending(r => IsAwarded(r))
                .ThenByDescending(r => r.StatusUpdated)
                .FirstOrDefault();

        public static bool IsAwarded(MilestoneProgressRecord record) => record.Status == "awarded" || record.Status == "approved";

        // The Participates and Leads asked for are in: Terrain's own verdict, or by the counts when it gave none
        public static bool RequirementMet(MilestoneProgressRecord record)
        {
            if (record.RequirementStatus != "")
                return record.RequirementStatus == "complete";
            var target = MilestoneTargets.Participates(record.Stage);
            var p = record.Participant;
            return new[] { p.Community, p.Outdoors, p.Creative, p.PersonalGrowth }.All(c => c >= target) && Leads(record) >= MilestoneTargets.Leads(record.Stage);
        }

        // Assists count as Leads now, so both are added up
        public static int Leads(MilestoneProgressRecord record) => record.Leader.Total + record.Assistant.Total;

        public static bool IsReady(MilestoneProgressRecord record) => RequirementMet(record) && !IsAwarded(record);

        public static MilestoneProgressReport Build(MilestoneProgressInput input)
        {
            var report = new MilestoneProgressReport
            {
                UnitName = input.UnitName,
                Section = input.Section,
                Stage = input.Stage,
                GeneratedAt = input.GeneratedAt,
                OnlyReady = input.OnlyReady
            };
            var names = MemberNames.GetDisplayNames(input.Members, m => m.FirstName, m => m.LastName, input.UseFullNames);
            // one milestone, or all three in turn so the pages for Milestone 1 come first
            foreach (var stage in input.Stage == AllMilestones ? Milestones : new[] { input.Stage })
                foreach (var member in input.Members)
                {
                    var record = RecordFor(input.Records, member.MemberId, input.Section, stage);
                    if (record == null)
                        continue;
                    report.ScoutsWithTheMilestone++;
                    if (input.OnlyReady && !IsReady(record))
                        continue;
                    report.Pages.Add(PageFor(member, names[member], record, input));
                }
            return report;
        }

        private static MilestoneScoutPage PageFor(MilestoneProgressMember member, string name, MilestoneProgressRecord record, MilestoneProgressInput input)
        {
            var target = MilestoneTargets.Participates(record.Stage);
            var leadTarget = MilestoneTargets.Leads(record.Stage);
            var p = record.Participant;
            var page = new MilestoneScoutPage
            {
                MemberId = member.MemberId,
                Name = name,
                Patrol = member.Patrol,
                Stage = record.Stage,
                Ready = IsReady(record),
                Status = StatusText(record),
                Areas = new List<MilestoneAreaProgress>
                {
                    new MilestoneAreaProgress { Area = "Community", Count = p.Community, Target = target },
                    new MilestoneAreaProgress { Area = "Outdoors", Count = p.Outdoors, Target = target },
                    new MilestoneAreaProgress { Area = "Creative", Count = p.Creative, Target = target },
                    new MilestoneAreaProgress { Area = "Personal growth", Count = p.PersonalGrowth, Target = target }
                },
                Leads = new MilestoneAreaProgress { Area = "Leads", Count = Leads(record), Target = leadTarget },
                Events = record.Events.OrderBy(e => e.Date).ThenBy(e => e.EventName, StringComparer.CurrentCultureIgnoreCase)
                    .Select(e => new MilestoneEventLine { Date = e.Date, Event = e.EventName, Area = AreaName(e.Area), Credit = CreditText(e.Credit) }).ToList()
            };

            input.Forms.TryGetValue(record.FormKey, out var form);
            var eventNames = record.Events.Where(e => e.EventId != "").GroupBy(e => e.EventId).ToDictionary(g => g.Key, g => g.First().EventName);
            var asked = new HashSet<string>();
            // the form's own questions, in the order they are asked and as they were worded for this record, with only what was answered
            foreach (var question in form?.Questions ?? new List<SiaQuestion>())
            {
                asked.Add(question.Id);
                var text = AnswerText(record.Answers, question.Id, form?.Options);
                if (text != "")
                    page.Review.Add(new MilestoneReviewAnswer { Question = question.Label, Answer = text });
            }
            foreach (var answer in record.Answers.OrderBy(a => a.Key, StringComparer.Ordinal))
            {
                var kind = EventReviewKind(answer.Key, out var eventId);
                if (kind != "")
                {
                    if (!string.IsNullOrWhiteSpace(answer.Value))
                        page.EventReviews.Add(new MilestoneReviewAnswer
                        {
                            Question = $"{kind}: {(eventNames.TryGetValue(eventId, out var eventName) ? eventName : "an event")}",
                            Answer = answer.Value.Trim()
                        });
                }
                else if (form == null && !asked.Contains(answer.Key) && !string.IsNullOrWhiteSpace(answer.Value) && !IsHousekeeping(answer.Key))
                {
                    // the form is not known, so the answer is shown under its own name rather than lost
                    page.Review.Add(new MilestoneReviewAnswer { Question = answer.Key.Replace('_', ' '), Answer = answer.Value.Trim() });
                }
            }
            return page;
        }

        // The Scout's note on an event: lead_review_<event id>, assist_review_<event id>, leader_review_<event id>
        private static string EventReviewKind(string key, out string eventId)
        {
            foreach (var (prefix, kind) in new[] { ("lead_review_", "Lead review"), ("assist_review_", "Assist review"), ("leader_review_", "Leader review") })
                if (key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    eventId = key.Substring(prefix.Length);
                    return kind;
                }
            eventId = "";
            return "";
        }

        private static bool IsHousekeeping(string key) =>
            key is "skipped_on" or "skipped_by" or "skip_notes" or "previous_status" or "supporting_files" or "spices_text_link";

        // An answer as text: a selector's option ids become the options' own wording
        private static string AnswerText(Dictionary<string, string> answers, string id, Dictionary<string, string>? options)
        {
            if (!answers.TryGetValue(id, out var raw) || string.IsNullOrWhiteSpace(raw))
                return "";
            return string.Join(", ", raw.Split(',').Select(v => v.Trim()).Where(v => v != "").Select(v => options != null && options.TryGetValue(v, out var title) ? title : v));
        }

        public static string StatusText(MilestoneProgressRecord record) => record.Status switch
        {
            "awarded" => "Awarded",
            "approved" => "Approved, waiting to be presented",
            "pending_review" => "In review",
            "draft_review" => RequirementMet(record) ? "Ready, review drafted" : "Review drafted",
            _ => RequirementMet(record) ? "Ready" : "In progress"
        };

        public static string AreaName(string area) => area.ToLowerInvariant() switch
        {
            "community" => "Community",
            "outdoors" or "outdoor" => "Outdoors",
            "creative" => "Creative",
            "personal_growth" or "personal growth" => "Personal growth",
            _ => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(area.Replace('_', ' ').Trim())
        };

        public static string CreditText(string credit) =>
            credit.Contains("lead", StringComparison.OrdinalIgnoreCase) || credit.Contains("assist", StringComparison.OrdinalIgnoreCase) ? "Led" : "Participated";
    }

    // Reads Terrain's unit-wide milestone records, with their answers, into MilestoneProgressRecords
    public static class MilestoneProgressParser
    {
        public static List<MilestoneProgressRecord> Parse(string json)
        {
            var records = new List<MilestoneProgressRecord>();
            try
            {
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
                    return records;
                foreach (var item in results.EnumerateArray())
                {
                    var record = new MilestoneProgressRecord
                    {
                        Id = Text(item, "id"),
                        MemberId = Text(item, "member_id"),
                        Section = Text(item, "section").Trim().ToLowerInvariant(),
                        Status = Text(item, "status"),
                        RequirementStatus = Text(item, "milestone_requirement_status"),
                        Template = Text(item, "template"),
                        Version = Number(item, "version"),
                        Answers = Answers(item),
                        Stage = item.TryGetProperty("achievement_meta", out var meta) ? Number(meta, "stage") : 0
                    };
                    if (DateTime.TryParse(Text(item, "status_updated"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var updated))
                        record.StatusUpdated = ReportDate.ForReport(updated);
                    if (item.TryGetProperty("event_count", out var counts) && counts.ValueKind == JsonValueKind.Object)
                    {
                        record.Participant = Counts(counts, "participant");
                        record.Assistant = Counts(counts, "assistant");
                        record.Leader = Counts(counts, "leader");
                    }
                    if (item.TryGetProperty("event_log", out var log) && log.ValueKind == JsonValueKind.Array)
                        foreach (var entry in log.EnumerateArray())
                        {
                            var credit = new MilestoneEventCredit
                            {
                                Credit = Text(entry, "credit_type"),
                                Area = Text(entry, "challenge_area"),
                                EventId = Text(entry, "event_id"),
                                EventName = Text(entry, "event_name")
                            };
                            if (DateTime.TryParse(Text(entry, "event_start_datetime"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var start))
                                credit.Date = ReportDate.ForReport(start);
                            record.Events.Add(credit);
                        }
                    records.Add(record);
                }
            }
            catch (JsonException)
            {
            }
            return records;
        }

        private static MilestoneAreaCounts Counts(JsonElement counts, string name)
        {
            var result = new MilestoneAreaCounts();
            if (!counts.TryGetProperty(name, out var area) || area.ValueKind != JsonValueKind.Object)
                return result;
            result.Community = Number(area, "community");
            result.Outdoors = Number(area, "outdoors");
            result.Creative = Number(area, "creative");
            result.PersonalGrowth = Number(area, "personal_growth");
            return result;
        }

        // The answers, each as text: an array (a selector's options) is joined with commas
        private static Dictionary<string, string> Answers(JsonElement item)
        {
            var values = new Dictionary<string, string>();
            if (!item.TryGetProperty("answers", out var answers) || answers.ValueKind != JsonValueKind.Object)
                return values;
            foreach (var property in answers.EnumerateObject())
                switch (property.Value.ValueKind)
                {
                    case JsonValueKind.String:
                        values[property.Name] = property.Value.GetString() ?? "";
                        break;
                    case JsonValueKind.Number:
                    case JsonValueKind.True:
                    case JsonValueKind.False:
                        values[property.Name] = property.Value.GetRawText().ToLowerInvariant();
                        break;
                    case JsonValueKind.Array:
                        values[property.Name] = string.Join(", ", property.Value.EnumerateArray().Where(a => a.ValueKind == JsonValueKind.String).Select(a => a.GetString() ?? ""));
                        break;
                }
            return values;
        }

        private static string Text(JsonElement element, string name) =>
            element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

        // A whole number, however Terrain wrote it (a count can come as 3 or 3.0)
        private static int Number(JsonElement element, string name) =>
            element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) ? (int)Math.Round(number) : 0;
    }
}
