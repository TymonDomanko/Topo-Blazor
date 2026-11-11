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
        // The day of the week the section meets, from the last few months of the unit's calendar; null when there is no calendar for the unit or too few events
        public Task<DayOfWeek?> FindSectionNight(string unitId);
        // The activities Terrain has on the unit's calendar between the dates, in full; null when there is no calendar named after the unit
        public Task<List<GetEventResultModel>?> GetTerrainEvents(string unitId, DateTime from, DateTime to, Action<string>? progress = null, CancellationToken cancellationToken = default);
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

        public async Task<DayOfWeek?> FindSectionNight(string unitId)
        {
            var calendar = await CalendarFor(unitId);
            if (calendar == null)
                return null;

            await _programService.SetCalendar(new[] { calendar });
            try
            {
                var events = await _terrainAPIService.GetEventsAsync(UserId(), DateTime.Today.AddDays(-FindDays), DateTime.Today);
                return ProgramPlanner.FindSectionNight(events?.results ?? new Event[0]);
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
                return null;

            var events = await _unitReportService.GetEventsForCalendars(from, to, new[] { calendar }, cancellationToken, progress);
            // what is for the whole unit or group, not a patrol or a member's project
            return events.Where(e => e.Scope != "patrol" && e.Scope != "member").Select(e => e.Event).ToList();
        }

        // The calendar named after the unit
        private async Task<string?> CalendarFor(string unitId)
        {
            var calendars = await _programService.GetCalendars();
            var unitName = _storageService.GetProfilesResult?.profiles?.FirstOrDefault(p => p.unit?.id == unitId)?.unit?.name;
            return CalendarMatcher.FindForUnit(calendars, unitName);
        }

        private string UserId() => _storageService.GetProfilesResult?.profiles?.FirstOrDefault()?.member?.id ?? "";
    }
}
