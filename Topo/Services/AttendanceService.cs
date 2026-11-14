using Topo.Model.Attendance;
using Topo.Model.Members;
using Topo.Model.Progress;
using Topo.Model.UnitReport;

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
        // How far back to look for when someone not seen in the period was last seen, in windows of three months, and the events read for each
        // window (for the session, since they do not change when the period does)
        private const int LookBackWindows = 4;
        private const int LookBackWindowDays = 90;
        private readonly Dictionary<string, List<UnitEventInput>> _earlier = new Dictionary<string, List<UnitEventInput>>();

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

            var input = new AttendanceInput
            {
                UnitName = profile?.unit?.name ?? "", Section = section, From = from, To = to, AsOf = DateTime.Now,
                Members = members, Events = events, InvestedOn = invested
            };
            var data = AttendanceBuilder.Analyse(input);

            // anyone not seen in the period: look further back, a window at a time, until all have been found or the windows run out
            var end = from.Date.AddDays(-1);
            for (var window = 0; window < LookBackWindows && data.NotSeenInPeriod().Any(m => data.NotSeen(m).LastSeen == null); window++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var start = end.AddDays(-(LookBackWindowDays - 1));
                var cacheKey = $"{string.Join(",", calendarIds.OrderBy(c => c))}|{start:yyyyMMdd}";
                if (!_earlier.TryGetValue(cacheKey, out var earlier))
                {
                    earlier = await _unitReportService.GetEventsForCalendars(start, end, calendarIds, cancellationToken, step => progress?.Invoke($"further back, {step}"));
                    _earlier[cacheKey] = earlier;
                }
                input.EarlierEvents.AddRange(earlier);
                data = AttendanceBuilder.Analyse(input);
                end = start.AddDays(-1);
            }
            return data;
        }
    }
}
