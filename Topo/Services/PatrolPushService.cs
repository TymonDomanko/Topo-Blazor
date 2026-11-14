using Topo.Model.Members;
using Topo.Model.PatrolPlanner;

namespace Topo.Services
{
    public interface IPatrolPushService
    {
        // Reads Terrain again and works out the least that has to be written for it to match the board. Writes nothing.
        Task<PatrolPushPlan> PrepareAsync(string unitId, IEnumerable<PatrolPlannerMember> board, IEnumerable<string> patrolsRemovedFromBoard);

        // Makes the writes the plan lists, after the leader has confirmed it
        Task<PatrolPushResult> PushAsync(PatrolPushPlan plan, Action<string>? progress = null);
    }

    public class PatrolPushService : IPatrolPushService
    {
        private readonly ITerrainAPIService _terrainAPIService;
        private readonly StorageService _storageService;

        public PatrolPushService(ITerrainAPIService terrainAPIService, StorageService storageService)
        {
            _terrainAPIService = terrainAPIService;
            _storageService = storageService;
        }

        public async Task<PatrolPushPlan> PrepareAsync(string unitId, IEnumerable<PatrolPlannerMember> board, IEnumerable<string> patrolsRemovedFromBoard)
        {
            var members = await _terrainAPIService.GetMembersAsync(unitId);
            var patrols = await _terrainAPIService.GetUnitPatrolsAsync(unitId);
            if (members == null || members.results == null || patrols == null)
            {
                // Without Terrain's current state there is no safe way to tell what needs changing
                return new PatrolPushPlan { UnitId = unitId, Error = "Terrain could not be read, so nothing was checked or changed. Try again in a moment." };
            }
            return PatrolPushPlanner.Build(unitId, _storageService.GroupId ?? "", board, members.results, patrols, patrolsRemovedFromBoard);
        }

        public async Task<PatrolPushResult> PushAsync(PatrolPushPlan plan, Action<string>? progress = null)
        {
            var result = new PatrolPushResult();
            var patrolIds = new Dictionary<string, string>(StringComparer.Ordinal);
            var cannotCreate = new Dictionary<string, string>(StringComparer.Ordinal);
            var refused = false;

            // New patrols first, so there is somewhere to move people to
            foreach (var patrolName in plan.PatrolsToCreate)
            {
                progress?.Invoke($"Creating {patrolName}");
                var (created, patrolId) = await _terrainAPIService.CreatePatrolAsync(plan.GroupId, plan.UnitId, patrolName);
                if (created.Success && !string.IsNullOrEmpty(patrolId))
                {
                    patrolIds[patrolName] = patrolId;
                    result.CreatedPatrols.Add(patrolName);
                }
                else
                {
                    cannotCreate[patrolName] = created.Message;
                    refused |= created.NotAllowed;
                    if (refused)
                        break;
                }
            }

            foreach (var member in plan.Members)
            {
                var memberResult = new PatrolPushMemberResult { MemberName = member.MemberName };
                result.Members.Add(memberResult);

                if (refused)
                {
                    memberResult.Outcome = PatrolPushOutcome.NotAttempted;
                    memberResult.Message = "not attempted: Terrain refused an earlier change";
                    continue;
                }

                // Don't take someone out of their patrol if the patrol they are going to couldn't be made
                var missing = member.Steps.FirstOrDefault(s => s.Kind == PatrolPushStepKind.AddToPatrol && string.IsNullOrEmpty(s.PatrolId)
                    && !patrolIds.ContainsKey(s.PatrolName));
                if (missing != null)
                {
                    memberResult.Outcome = PatrolPushOutcome.NotAttempted;
                    memberResult.Message = $"not changed: {missing.PatrolName} could not be created ({(cannotCreate.TryGetValue(missing.PatrolName, out var why) ? why : "it was not created")})";
                    continue;
                }

                progress?.Invoke(member.MemberName);
                var done = 0;
                foreach (var step in member.Steps)
                {
                    var written = await WriteStepAsync(plan, member, step, patrolIds);
                    if (!written.Success)
                    {
                        memberResult.Outcome = PatrolPushOutcome.Failed;
                        memberResult.Message = $"{Describe(step)} failed: {written.Message}"
                            + (done > 0 ? $"; {done} of {member.Steps.Count} changes had already been made" : "");
                        refused |= written.NotAllowed;
                        break;
                    }
                    done++;
                }
                if (memberResult.Outcome != PatrolPushOutcome.Failed)
                    memberResult.Outcome = PatrolPushOutcome.Done;
            }

            await CheckAndRetryAsync(plan, result, refused, progress);
            return result;
        }

        // A write Terrain accepted hasn't necessarily stuck, so Terrain is read again and each member compared with how the
        // board wanted them. What didn't stick is written once more (worked out afresh from what Terrain shows now, so nothing
        // is written twice), then checked again; anything still wrong is reported as failed rather than left looking done.
        private async Task CheckAndRetryAsync(PatrolPushPlan plan, PatrolPushResult result, bool refused, Action<string>? progress)
        {
            var attempted = plan.Members.Select((member, i) => (member, outcome: result.Members[i]))
                .Where(x => x.outcome.Outcome != PatrolPushOutcome.NotAttempted).ToList();
            if (attempted.Count == 0)
                return;

            var retried = new HashSet<string>();
            for (var attempt = 1; attempt <= 2; attempt++)
            {
                progress?.Invoke(attempt == 1 ? "Checking that Terrain has the changes" : "Checking again");
                var members = await _terrainAPIService.GetMembersAsync(plan.UnitId);
                var patrols = await _terrainAPIService.GetUnitPatrolsAsync(plan.UnitId);
                if (members?.results == null || patrols == null)
                {
                    foreach (var (_, outcome) in attempted.Where(x => x.outcome.Outcome == PatrolPushOutcome.Done))
                        outcome.Message = "sent, but Terrain could not be read to check it";
                    return;
                }

                var now = members.results.GroupBy(m => m.id).ToDictionary(g => g.Key, g => g.First());
                var patrolIds = PatrolPushPlanner.PatrolIds(patrols);
                var unmet = new List<(PatrolPushMember member, PatrolPushMemberResult outcome, PatrolPushPlanner.MemberSteps steps, Member? current)>();
                foreach (var (member, outcome) in attempted)
                {
                    now.TryGetValue(member.MemberId, out var current);
                    var steps = current == null ? null : PatrolPushPlanner.StepsFor(current, member.WantPatrolName, member.WantBoardDuty, member.WantUnitCouncil, patrolIds);
                    if (steps != null && steps.Steps.Count == 0)
                    {
                        outcome.Outcome = PatrolPushOutcome.Done;
                        outcome.Message = retried.Contains(member.MemberId) ? "went through on the second try" : "";
                    }
                    else
                    {
                        unmet.Add((member, outcome, steps ?? new PatrolPushPlanner.MemberSteps(), current));
                    }
                }

                if (unmet.Count == 0)
                    return;

                if (attempt == 2 || refused)
                {
                    foreach (var (member, outcome, _, current) in unmet)
                    {
                        outcome.Outcome = PatrolPushOutcome.Failed;
                        var reason = outcome.Message;
                        outcome.Message = current == null
                            ? "isn't in this unit in Terrain, so the change could not be checked"
                            : !string.IsNullOrEmpty(reason)
                                ? $"{reason}; Terrain shows {PatrolPushPlanner.DescribeState(current)}"
                                : $"Terrain still shows {PatrolPushPlanner.DescribeState(current)}, not {PatrolPushPlanner.Describe(member.WantPatrolName, member.WantBoardDuty, member.WantUnitCouncil)}";
                    }
                    return;
                }

                // Try again, only for what Terrain doesn't show yet. A patrol that still doesn't exist isn't made a second time here.
                progress?.Invoke("Trying again");
                foreach (var (member, outcome, steps, current) in unmet)
                {
                    if (current == null || steps.Steps.Count == 0 || steps.PatrolsToCreate.Count > 0)
                        continue;
                    retried.Add(member.MemberId);
                    foreach (var step in steps.Steps)
                    {
                        var written = await WriteStepAsync(plan, member, step, new Dictionary<string, string>());
                        refused |= written.NotAllowed;
                        if (!written.Success)
                        {
                            outcome.Message = $"{Describe(step)} failed on the second try: {written.Message}";
                            break;
                        }
                    }
                }
            }
        }

        private Task<TerrainWriteResult> WriteStepAsync(PatrolPushPlan plan, PatrolPushMember member, PatrolPushStep step, Dictionary<string, string> createdPatrolIds)
        {
            var patrolId = string.IsNullOrEmpty(step.PatrolId) ? createdPatrolIds.GetValueOrDefault(step.PatrolName, "") : step.PatrolId;
            return step.Kind switch
            {
                PatrolPushStepKind.RemoveFromPatrol => _terrainAPIService.DeletePatrolMemberAsync(plan.GroupId, patrolId, member.MemberId),
                PatrolPushStepKind.AddToPatrol or PatrolPushStepKind.SetPatrolDuty => _terrainAPIService.PutPatrolMemberAsync(plan.GroupId, patrolId, member.MemberId, step.Duty),
                _ => _terrainAPIService.PutUnitMemberAsync(plan.GroupId, plan.UnitId, member.MemberId, step.Duty, step.UnitCouncil)
            };
        }

        private static string Describe(PatrolPushStep step) => step.Kind switch
        {
            PatrolPushStepKind.RemoveFromPatrol => $"leaving {step.PatrolName}",
            PatrolPushStepKind.AddToPatrol => $"joining {step.PatrolName}",
            PatrolPushStepKind.SetPatrolDuty => $"changing duty in {step.PatrolName}",
            _ => "changing unit duty or council"
        };
    }
}
