using Topo.Model.Members;

namespace Topo.Model.PatrolPlanner
{
    public class UnitPatrolModel
    {
        public string id { get; set; } = string.Empty;
        public string unit_id { get; set; } = string.Empty;
        public string name { get; set; } = string.Empty;
    }

    public class GetUnitPatrolsResultModel
    {
        public UnitPatrolModel[] results { get; set; } = new UnitPatrolModel[0];
    }

    // What Terrain answered to a write. Terrain answers a successful write with 204 and no body; a refused one with a 4xx.
    // StatusCode 0 means the request never got an answer (offline, blocked).
    public class TerrainWriteResult
    {
        public int StatusCode { get; set; }
        public string Body { get; set; } = string.Empty;
        public bool Success => StatusCode >= 200 && StatusCode < 300;
        public bool NotAllowed => StatusCode == 401 || StatusCode == 403;

        public string Message => StatusCode == 0 ? "Terrain could not be reached"
            : NotAllowed ? "Terrain refused it (you may not have permission to change this unit's patrols)"
            : $"Terrain answered {StatusCode}";
    }

    public enum PatrolPushStepKind
    {
        RemoveFromPatrol,
        AddToPatrol,
        SetPatrolDuty,
        SetUnitDuty
    }

    // One call to Terrain for one member
    public class PatrolPushStep
    {
        public PatrolPushStepKind Kind { get; set; }
        public string PatrolName { get; set; } = string.Empty;
        // Known for patrols that exist in Terrain; empty for one this push creates, which is looked up by name when it runs
        public string PatrolId { get; set; } = string.Empty;
        // Terrain's own words: member, patrol_leader, assistant_patrol_leader for a patrol; member, unit_leader for the unit
        public string Duty { get; set; } = string.Empty;
        public bool UnitCouncil { get; set; }
    }

    // Everything to do for one member, in the order to do it
    public class PatrolPushMember
    {
        public string MemberId { get; set; } = string.Empty;
        public string MemberName { get; set; } = string.Empty;
        // What the leader reads in the confirmation, for example "Lyrebird (Blue) → Magpie (Orange) as APL"
        public string Summary { get; set; } = string.Empty;
        public List<PatrolPushStep> Steps { get; set; } = new List<PatrolPushStep>();
    }

    // A change the plan won't make, and why
    public class PatrolPushSkipped
    {
        public string MemberName { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
    }

    public class PatrolPushPlan
    {
        public string UnitId { get; set; } = string.Empty;
        public string GroupId { get; set; } = string.Empty;
        // Patrols that don't exist in Terrain yet and have someone moving into them
        public List<string> PatrolsToCreate { get; set; } = new List<string>();
        public List<PatrolPushMember> Members { get; set; } = new List<PatrolPushMember>();
        public List<PatrolPushSkipped> Skipped { get; set; } = new List<PatrolPushSkipped>();
        // Things on the board that the push leaves alone, so the leader isn't surprised
        public List<string> Notes { get; set; } = new List<string>();
        // Set when Terrain couldn't be read, so there is nothing safe to push
        public string Error { get; set; } = string.Empty;

        public bool HasWork => string.IsNullOrEmpty(Error) && Members.Any(m => m.Steps.Count > 0);
        public int WriteCount => PatrolsToCreate.Count + Members.Sum(m => m.Steps.Count);
    }

    public enum PatrolPushOutcome
    {
        Done,
        NotAttempted,
        Failed
    }

    public class PatrolPushMemberResult
    {
        public string MemberName { get; set; } = string.Empty;
        public PatrolPushOutcome Outcome { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class PatrolPushResult
    {
        public List<PatrolPushMemberResult> Members { get; set; } = new List<PatrolPushMemberResult>();
        public List<string> CreatedPatrols { get; set; } = new List<string>();
        public int DoneCount => Members.Count(m => m.Outcome == PatrolPushOutcome.Done);
        public bool AllDone => Members.All(m => m.Outcome == PatrolPushOutcome.Done);
    }

    // Works out the least that has to be written to Terrain to make it match the board.
    //
    // Terrain is read again first, and a member is only changed if Terrain still shows what the board was loaded with. If it
    // doesn't (someone changed it in Terrain meanwhile) that member is left alone and reported, rather than overwritten.
    //
    // For a member who moves patrol the old patrol membership is removed BEFORE the new one is added. Terrain lets a member sit
    // in two patrols at once without showing it, so if the second call failed the other way round they would be left in two
    // patrols; this way the worst a failure leaves is no patrol, which shows up on the board.
    public static class PatrolPushPlanner
    {
        public const string MemberDuty = "member";
        public const string PatrolLeaderDuty = "patrol_leader";
        public const string AssistantPatrolLeaderDuty = "assistant_patrol_leader";
        public const string UnitLeaderDuty = "unit_leader";

        public static PatrolPushPlan Build(string unitId, string fallbackGroupId, IEnumerable<PatrolPlannerMember> board,
            IEnumerable<Member> terrainMembers, IEnumerable<UnitPatrolModel> terrainPatrols, IEnumerable<string> patrolsRemovedFromBoard)
        {
            var plan = new PatrolPushPlan { UnitId = unitId };
            var now = terrainMembers.GroupBy(m => m.id).ToDictionary(g => g.Key, g => g.First());
            var patrolIds = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var patrol in terrainPatrols)
                patrolIds.TryAdd(patrol.name, patrol.id);

            // the unit's own group is the one in the path of the write
            plan.GroupId = terrainMembers.Select(m => m.unit?.group_id).FirstOrDefault(g => !string.IsNullOrEmpty(g)) ?? fallbackGroupId;

            foreach (var member in board.Where(m => m.IsChanged).OrderBy(m => m.FirstName).ThenBy(m => m.LastName))
            {
                var name = $"{member.FirstName} {member.LastName}";
                if (!now.TryGetValue(member.Id, out var current))
                {
                    plan.Skipped.Add(new PatrolPushSkipped { MemberName = name, Reason = "isn't in this unit in Terrain any more" });
                    continue;
                }

                var currentPatrolName = current.patrol?.name ?? "";
                var currentPatrolId = current.patrol?.id ?? "";
                var currentPatrolDuty = current.patrol?.duty ?? "";
                var currentUnitDuty = current.unit?.duty ?? "";
                var currentCouncil = current.unit?.unit_council ?? false;

                // Terrain no longer shows what the board was loaded with
                if (currentPatrolName != member.OriginalPatrolName
                    || BoardDuty(currentUnitDuty, currentPatrolDuty) != member.OriginalPatrolDuty
                    || currentCouncil != member.OriginalUnitCouncil)
                {
                    plan.Skipped.Add(new PatrolPushSkipped
                    {
                        MemberName = name,
                        Reason = $"has been changed in Terrain since the planner was loaded ({Describe(currentPatrolName, BoardDuty(currentUnitDuty, currentPatrolDuty), currentCouncil)}); refresh and plan again"
                    });
                    continue;
                }

                var wantUnitDuty = member.PatrolDuty == "UL" ? UnitLeaderDuty : currentUnitDuty == UnitLeaderDuty ? MemberDuty : currentUnitDuty;
                var movingPatrol = member.PatrolName != currentPatrolName;
                var wantPatrolDuty = member.PatrolDuty switch
                {
                    "PL" => PatrolLeaderDuty,
                    "APL" => AssistantPatrolLeaderDuty,
                    // a Unit Leader's patrol duty isn't shown on the board, so it is left as it is unless they change patrol
                    "UL" => movingPatrol || string.IsNullOrEmpty(currentPatrolDuty) ? MemberDuty : currentPatrolDuty,
                    _ => MemberDuty
                };

                var memberPlan = new PatrolPushMember { MemberId = member.Id, MemberName = name };
                var phrases = new List<string>();

                if (movingPatrol)
                {
                    if (!string.IsNullOrEmpty(currentPatrolName) && !string.IsNullOrEmpty(currentPatrolId))
                    {
                        memberPlan.Steps.Add(new PatrolPushStep { Kind = PatrolPushStepKind.RemoveFromPatrol, PatrolName = currentPatrolName, PatrolId = currentPatrolId });
                    }
                    if (!string.IsNullOrEmpty(member.PatrolName))
                    {
                        var known = patrolIds.TryGetValue(member.PatrolName, out var patrolId);
                        if (!known && !plan.PatrolsToCreate.Contains(member.PatrolName))
                            plan.PatrolsToCreate.Add(member.PatrolName);
                        memberPlan.Steps.Add(new PatrolPushStep { Kind = PatrolPushStepKind.AddToPatrol, PatrolName = member.PatrolName, PatrolId = known ? patrolId! : "", Duty = wantPatrolDuty });
                    }
                    phrases.Add(string.IsNullOrEmpty(currentPatrolName) ? $"join {member.PatrolName}"
                        : string.IsNullOrEmpty(member.PatrolName) ? $"leave {currentPatrolName}"
                        : $"{currentPatrolName} → {member.PatrolName}");
                    if (!string.IsNullOrEmpty(member.PatrolName) && wantPatrolDuty != MemberDuty)
                        phrases.Add($"as {DutyText(wantPatrolDuty)}");
                }
                else if (!string.IsNullOrEmpty(member.PatrolName) && wantPatrolDuty != currentPatrolDuty)
                {
                    memberPlan.Steps.Add(new PatrolPushStep { Kind = PatrolPushStepKind.SetPatrolDuty, PatrolName = member.PatrolName, PatrolId = currentPatrolId, Duty = wantPatrolDuty });
                    phrases.Add($"{member.PatrolName}: {DutyText(currentPatrolDuty)} → {DutyText(wantPatrolDuty)}");
                }

                if (wantUnitDuty != currentUnitDuty || member.UnitCouncil != currentCouncil)
                {
                    memberPlan.Steps.Add(new PatrolPushStep { Kind = PatrolPushStepKind.SetUnitDuty, Duty = wantUnitDuty, UnitCouncil = member.UnitCouncil });
                    if (wantUnitDuty != currentUnitDuty)
                        phrases.Add(wantUnitDuty == UnitLeaderDuty ? "becomes Unit Leader" : "no longer Unit Leader");
                    if (member.UnitCouncil != currentCouncil)
                        phrases.Add(member.UnitCouncil ? "joins Unit Council" : "leaves Unit Council");
                }

                if (memberPlan.Steps.Count == 0)
                    continue;
                memberPlan.Summary = string.Join(", ", phrases);
                plan.Members.Add(memberPlan);
            }

            // The board can hold patrols Terrain hasn't got. Only ones someone is moving into are created; an empty one isn't needed yet.
            foreach (var removed in patrolsRemovedFromBoard.Where(p => patrolIds.ContainsKey(p)))
                plan.Notes.Add($"{removed} was removed on the board, but is not deleted in Terrain (its members are moved out; the empty patrol stays).");
            if (plan.PatrolsToCreate.Count > 0)
                plan.Notes.Add($"Created in Terrain first: {string.Join(", ", plan.PatrolsToCreate)}.");
            return plan;
        }

        // The words the board uses for a Terrain unit duty and patrol duty: UL, PL, APL, or blank for an ordinary member
        public static string BoardDuty(string unitDuty, string patrolDuty)
        {
            if (unitDuty == UnitLeaderDuty)
                return "UL";
            if (patrolDuty == AssistantPatrolLeaderDuty)
                return "APL";
            if (patrolDuty == PatrolLeaderDuty)
                return "PL";
            return "";
        }

        private static string DutyText(string patrolDuty) => patrolDuty switch
        {
            PatrolLeaderDuty => "PL",
            AssistantPatrolLeaderDuty => "APL",
            _ => "Member"
        };

        private static string Describe(string patrolName, string boardDuty, bool council)
        {
            var duty = string.IsNullOrEmpty(boardDuty) ? "Member" : boardDuty;
            return $"{(string.IsNullOrEmpty(patrolName) ? "no patrol" : patrolName)}, {duty}{(council ? ", Unit Council" : "")}";
        }
    }
}
