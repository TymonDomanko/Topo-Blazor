using Topo.Model.Attendance;
using Topo.Model.Members;
using Topo.Model.Progress;

namespace Topo.Services
{
    public interface IAttendanceService
    {
        // Who came to the events on the chosen calendars in the period
        public Task<AttendanceData> GetAttendance(string unitId, DateTime from, DateTime to, string[] calendarIds, Action<string>? progress = null, CancellationToken cancellationToken = default);
        // How many of the unit's last Scout nights each youth came to, from the calendar named after the unit; null when there is no such calendar
        public Task<RecentNights?> GetRecentNights(string unitId, int nights, Action<string>? progress = null, CancellationToken cancellationToken = default);
    }

    public class AttendanceService : IAttendanceService
    {
        // How far back to look for the last Scout nights: ten weekly nights with room for term breaks
        private const int RecentDays = 180;

        // When each youth was invested into the section (the date is blank for someone who has not been), kept for the session since it
        // does not change when the period does
        private readonly Dictionary<string, DateTime?> _investedOn = new Dictionary<string, DateTime?>();

        private readonly StorageService _storageService;
        private readonly IMembersService _membersService;
        private readonly ITerrainAPIService _terrainAPIService;
        private readonly IUnitReportService _unitReportService;
        private readonly IProgramService _programService;

        public AttendanceService(StorageService storageService, IMembersService membersService, ITerrainAPIService terrainAPIService, IUnitReportService unitReportService, IProgramService programService)
        {
            _storageService = storageService;
            _membersService = membersService;
            _terrainAPIService = terrainAPIService;
            _unitReportService = unitReportService;
            _programService = programService;
        }

        public async Task<AttendanceData> GetAttendance(string unitId, DateTime from, DateTime to, string[] calendarIds, Action<string>? progress = null, CancellationToken cancellationToken = default)
        {
            var profile = _storageService.GetProfilesResult?.profiles?.FirstOrDefault(p => p.unit?.id == unitId);
            progress?.Invoke("the members");
            var members = await _membersService.GetMembersAsync(unitId);
            cancellationToken.ThrowIfCancellationRequested();
            var events = await _unitReportService.GetEventsForCalendars(from, to, calendarIds, cancellationToken, progress);

            // so that the events before a youth joined the section are not counted against them (nothing to work out when there are no events)
            var section = profile?.unit?.section ?? "";
            var youth = members.Where(m => m.isAdultLeader == 0).ToList();
            var invested = new Dictionary<string, DateTime>();
            // Nothing to work out when there are no events or no youth. Otherwise one read of the unit's introductions gives
            // everyone's, so the unit isn't read once per youth; only those not already known are looked up (every record
            // carries its member_id, and a youth with none gets an empty list).
            if (events.Count > 0 && youth.Count > 0)
            {
                if (youth.Any(y => !_investedOn.ContainsKey($"{section}/{y.id}")))
                {
                    progress?.Invoke("when each Scout was invested");
                    var introsByMember = (await _terrainAPIService.GetUnitAchievementsOfType<GetIntroductionResultsModel>(unitId, "intro_section"))
                        .results.ToLookup(r => r.member_id);
                    cancellationToken.ThrowIfCancellationRequested();
                    foreach (var member in youth)
                        _investedOn[$"{section}/{member.id}"] = AttendanceBuilder.InvestedOn(new GetIntroductionResultsModel { results = introsByMember[member.id].ToArray() }, section);
                }
                foreach (var member in youth)
                    if (_investedOn.TryGetValue($"{section}/{member.id}", out var date) && date != null)
                        invested[member.id] = date.Value;
            }

            return AttendanceBuilder.Analyse(new AttendanceInput
            {
                UnitName = profile?.unit?.name ?? "", Section = section, From = from, To = to, AsOf = DateTime.Now,
                Members = members, Events = events, InvestedOn = invested
            });
        }

        public async Task<RecentNights?> GetRecentNights(string unitId, int nights, Action<string>? progress = null, CancellationToken cancellationToken = default)
        {
            var calendars = await _programService.GetCalendars();
            var unitName = _storageService.GetProfilesResult?.profiles?.FirstOrDefault(p => p.unit?.id == unitId)?.unit?.name;
            var calendar = CalendarMatcher.FindForUnit(calendars, unitName);
            if (calendar == null)
                return null;

            var data = await GetAttendance(unitId, DateTime.Today.AddDays(-RecentDays), DateTime.Today, new[] { calendar }, progress, cancellationToken);
            return AttendanceBuilder.Recent(data, nights);
        }
    }
}
