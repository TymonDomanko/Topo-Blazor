using Topo.Model.Attendance;
using Topo.Model.Members;

namespace Topo.Services
{
    public interface IAttendanceService
    {
        // Who came to the events on the chosen calendars in the period
        public Task<AttendanceData> GetAttendance(string unitId, DateTime from, DateTime to, string[] calendarIds, Action<string>? progress = null, CancellationToken cancellationToken = default);
    }

    public class AttendanceService : IAttendanceService
    {
        // When each youth was invested into the section (the date is blank for someone who has not been), kept for the session since it
        // does not change when the period does
        private readonly Dictionary<string, DateTime?> _investedOn = new Dictionary<string, DateTime?>();

        private readonly StorageService _storageService;
        private readonly IMembersService _membersService;
        private readonly ITerrainAPIService _terrainAPIService;
        private readonly IUnitReportService _unitReportService;

        public AttendanceService(StorageService storageService, IMembersService membersService, ITerrainAPIService terrainAPIService, IUnitReportService unitReportService)
        {
            _storageService = storageService;
            _membersService = membersService;
            _terrainAPIService = terrainAPIService;
            _unitReportService = unitReportService;
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
            var names = SignInSheetLayout.GetDisplayNames(youth, false);
            var invested = new Dictionary<string, DateTime>();
            for (var i = 0; i < youth.Count && events.Count > 0; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var cacheKey = $"{section}/{youth[i].id}";
                if (!_investedOn.TryGetValue(cacheKey, out var date))
                {
                    progress?.Invoke($"{(names.TryGetValue(youth[i].id, out var name) ? name : youth[i].first_name)} ({i + 1} of {youth.Count})");
                    date = AttendanceBuilder.InvestedOn(await _terrainAPIService.GetIntroductionToSectionResultsForMember(youth[i].id), section);
                    _investedOn[cacheKey] = date;
                }
                if (date != null)
                    invested[youth[i].id] = date.Value;
            }

            return AttendanceBuilder.Analyse(new AttendanceInput
            {
                UnitName = profile?.unit?.name ?? "", Section = section, From = from, To = to, AsOf = DateTime.Now,
                Members = members, Events = events, InvestedOn = invested
            });
        }
    }
}
