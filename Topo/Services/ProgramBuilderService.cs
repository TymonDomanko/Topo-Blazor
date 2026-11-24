using Topo.Model;
using Topo.Model.Members;
using Topo.Model.Program;
using Topo.Model.ProgramBuilder;
using Topo.Model.UnitReport;

namespace Topo.Services
{
    public interface IProgramBuilderService
    {
        // The unit's patrols and the leaders who can be put in charge
        public Task<(List<string> Patrols, List<TerrainLeader> Leaders)> GetPeople(string unitId);
        // The day of the week the section meets and the times it usually starts and ends, from the last few months of the unit's calendar; null when
        // there is no calendar for the unit
        public Task<ProgramUsual?> FindUsual(string unitId);
        // The activities Terrain has on the unit's calendar between the dates, in full; null when there is no calendar named after the unit
        public Task<List<GetEventResultModel>?> GetTerrainEvents(string unitId, DateTime from, DateTime to, Action<string>? progress = null, CancellationToken cancellationToken = default);
        // The activities the unit's youth have proposed in Terrain, to be offered as ideas; null when Terrain could not be read
        public Task<List<ProposedActivityModel>?> GetProposals(string unitId);
        // What adding the program to the unit's calendar in Terrain would do, from what Terrain has now; nothing is sent
        // Cancels an activity in Terrain; what Terrain answered
        public Task<TerrainWriteResult> DeleteEvent(string eventId);
        public Task<ProgramPushPlan> PreparePush(ProgramDraft draft, TerrainContext context, Action<string>? progress = null, CancellationToken cancellationToken = default);
        // Adds what the plan says, one at a time, then looks for each in Terrain and keeps its id on the program's row
        public Task<ProgramPushResult> Push(ProgramPushPlan plan, ProgramDraft draft, TerrainContext context, Action<string>? progress = null, CancellationToken cancellationToken = default);
    }

    public class ProgramBuilderService : IProgramBuilderService
    {
        // How far back to look to find the section night
        private const int FindDays = 150;

        private readonly StorageService _storageService;
        private readonly IMembersService _membersService;
        private readonly ITerrainAPIService _terrainAPIService;
        private readonly IProgramService _programService;
        private readonly IUnitReportService _unitReportService;

        public ProgramBuilderService(StorageService storageService, IMembersService membersService, ITerrainAPIService terrainAPIService, IProgramService programService, IUnitReportService unitReportService)
        {
            _storageService = storageService;
            _membersService = membersService;
            _terrainAPIService = terrainAPIService;
            _programService = programService;
            _unitReportService = unitReportService;
        }

        public async Task<(List<string> Patrols, List<TerrainLeader> Leaders)> GetPeople(string unitId)
        {
            var members = await _membersService.GetMembersAsync(unitId);
            var leaders = UnitReportBuilder.LeadersOf(members)
                .Select(m => new TerrainLeader { Id = m.id, FirstName = m.first_name.Trim(), LastName = m.last_name.Trim(), MemberNumber = m.member_number, PatrolName = m.patrol_name })
                .OrderBy(l => l.FirstName, StringComparer.OrdinalIgnoreCase).ToList();
            return (ProgramPlanner.PatrolsOf(members), leaders);
        }

        public async Task<ProgramUsual?> FindUsual(string unitId)
        {
            using var calendarLock = await _programService.LockCalendarsAsync();
            var calendar = await CalendarFor(unitId);
            if (calendar == null)
            {
                return null;
            }

            await _programService.SetCalendar(new[] { calendar });
            try
            {
                var events = await _terrainAPIService.GetEventsAsync(UserId(), DateTime.Today.AddDays(-FindDays), DateTime.Today);
                return ProgramUsual.Find(events?.results ?? new Event[0]);
            }
            finally
            {
                await _programService.ResetCalendar();
            }
        }

        public async Task<List<GetEventResultModel>?> GetTerrainEvents(string unitId, DateTime from, DateTime to, Action<string>? progress = null, CancellationToken cancellationToken = default)
        {
            var calendar = await CalendarFor(unitId);
            if (calendar == null)
            {
                return null;
            }

            var events = await _unitReportService.GetEventsForCalendars(from, to, new[] { calendar }, cancellationToken, progress);
            // what is for the whole unit or group, not a patrol or a member's project
            return events.Where(e => e.Scope != "patrol" && e.Scope != "member").Select(e => e.Event).ToList();
        }

        public Task<List<ProposedActivityModel>?> GetProposals(string unitId) => _terrainAPIService.GetProposedActivitiesAsync(unitId);

        public Task<TerrainWriteResult> DeleteEvent(string eventId) => _terrainAPIService.DeleteEventAsync(eventId);

        public async Task<ProgramPushPlan> PreparePush(ProgramDraft draft, TerrainContext context, Action<string>? progress = null, CancellationToken cancellationToken = default)
        {
            progress?.Invoke("Reading Terrain");
            var events = await GetTerrainEvents(context.UnitId, draft.Start, draft.End, progress, cancellationToken);
            if (events == null)
            {
                return new ProgramPushPlan { Error = "There is no calendar named after this unit in Terrain, so there is nothing to add the program to." };
            }
            var plan = ProgramPush.Build(draft, events, context, DateTime.Now);

            // what is to be changed in Terrain is read in full, so that the change can be sent as the whole of the activity with only that different
            progress?.Invoke("Reading the activities to change");
            var candidates = plan.Updates.ToList();
            plan.Updates.Clear();
            var raws = await ReadsAtOnce.SelectAsync(candidates, item => _terrainAPIService.GetEventRawAsync(item.EventId));
            for (var i = 0; i < candidates.Count; i++)
            {
                var label = $"{candidates[i].Event.Row.Date:ddd d MMM} {candidates[i].Event.Title}";
                var body = raws[i] == "" ? null : ProgramPush.UpdateBody(raws[i], candidates[i].Event, context, out var reason);
                if (body == null)
                {
                    plan.Skipped.Add((label, "differs in Terrain, but " + (raws[i] == "" ? "could not be read from Terrain, so it was not changed" : ProgramPushReason(raws[i], candidates[i].Event, context))));
                    continue;
                }
                candidates[i].BodyJson = body;
                plan.Updates.Add(candidates[i]);
            }
            return plan;
        }

        // Why an activity could not be made into a change
        private static string ProgramPushReason(string raw, TerrainEvent planned, TerrainContext context)
        {
            ProgramPush.UpdateBody(raw, planned, context, out var reason);
            return reason;
        }

        public async Task<ProgramPushResult> Push(ProgramPushPlan plan, ProgramDraft draft, TerrainContext context, Action<string>? progress = null, CancellationToken cancellationToken = default)
        {
            // the new ones first, then the changes
            var result = await ProgramPush.Run(plan.Items.Concat(plan.Updates), item => item.Kind == ProgramPushKind.Update
                ? _terrainAPIService.UpdateEventAsync(item.EventId, item.BodyJson)
                : _terrainAPIService.CreateUnitEventAsync(context.UnitId, item.Body), progress);
            if (result.SentCount > 0)
            {
                progress?.Invoke("Checking Terrain");
                try
                {
                    var events = await GetTerrainEvents(context.UnitId, draft.Start, draft.End, null, cancellationToken);
                    if (events != null)
                    {
                        ProgramPush.MatchCreated(result, events, plan.ExistingIds, draft);
                        ProgramPush.MatchUpdated(result, draft, events);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // sent is sent; not being able to look for them just leaves them as sent
                }
            }
            return result;
        }

        // The unit's calendar, found among all the user has, whether or not they are switched on in Terrain. Reading it switches it on and the others off,
        // and puts them back as they were afterwards.
        private async Task<string?> CalendarFor(string unitId)
        {
            var calendars = await _programService.GetAllCalendars();
            var unitName = _storageService.GetProfilesResult?.profiles?.FirstOrDefault(p => p.unit?.id == unitId)?.unit?.name;
            return CalendarMatcher.FindForUnit(calendars, unitId, unitName);
        }

        private string UserId() => _storageService.GetProfilesResult?.profiles?.FirstOrDefault()?.member?.id ?? "";
    }
}
