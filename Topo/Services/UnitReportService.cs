using Topo.Model.Members;
using Topo.Model.UnitReport;

namespace Topo.Services
{
    public interface IUnitReportService
    {
        public Task<UnitReportModel> GetUnitReport(string unitId, DateTime from, DateTime to, string[] calendarIds, Action<string>? progress = null, CancellationToken cancellationToken = default);
        // Every event in the range from the chosen calendars, in full (attendance, logbook data) with its scope
        public Task<List<UnitEventInput>> GetEventsForCalendars(DateTime from, DateTime to, string[] calendarIds, CancellationToken cancellationToken = default, Action<string>? progress = null);
    }

    public class UnitReportService : IUnitReportService
    {
        private readonly StorageService _storageService;
        private readonly ITerrainAPIService _terrainAPIService;
        private readonly IMembersService _membersService;
        private readonly IProgramService _programService;

        public UnitReportService(StorageService storageService, ITerrainAPIService terrainAPIService, IMembersService membersService, IProgramService programService)
        {
            _storageService = storageService;
            _terrainAPIService = terrainAPIService;
            _membersService = membersService;
            _programService = programService;
        }

        public async Task<UnitReportModel> GetUnitReport(string unitId, DateTime from, DateTime to, string[] calendarIds, Action<string>? progress = null, CancellationToken cancellationToken = default)
        {
            var profile = _storageService.GetProfilesResult?.profiles?.FirstOrDefault(p => p.unit?.id == unitId);
            var input = new UnitReportInput
            {
                UnitName = profile?.unit?.name ?? "",
                Section = profile?.unit?.section ?? "",
                From = from,
                To = to,
                AsOf = DateTime.Now
            };

            progress?.Invoke("members");
            input.CurrentMembers = await _membersService.GetMembersAsync(unitId);

            progress?.Invoke("events");
            input.Events = await GetEventsForCalendars(from, to, calendarIds, cancellationToken, progress);

            // Badges come from each current youth member's records
            var youth = input.CurrentMembers.Where(m => !UnitReportBuilder.IsLeader(m)).ToList();
            // the Reading line names each Scout the short way, as the other reports do
            var shortNames = SignInSheetLayout.GetDisplayNames(youth, false);
            for (var i = 0; i < youth.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Invoke($"{(shortNames.TryGetValue(youth[i].id, out var shortName) ? shortName : youth[i].first_name)} ({i + 1} of {youth.Count})");
                input.BadgesByMember[youth[i].id] = new UnitMemberBadges
                {
                    Milestones = await _terrainAPIService.GetMilestoneResultsForMember(youth[i].id),
                    Oas = await _terrainAPIService.GetOASResultsForMember(youth[i].id),
                    Sia = await _terrainAPIService.GetSIAResultsForMember(youth[i].id),
                    Peak = await _terrainAPIService.GetPeakAwardResultsForMember(youth[i].id)
                };
            }

            progress?.Invoke("additional awards");
            input.AdditionalAwards = await _terrainAPIService.GetUnitAdditionalAwardAchievements(unitId);
            input.AwardNames = await AdditionalAwardNames.Get(_terrainAPIService);

            return UnitReportBuilder.Build(input);
        }

        // Terrain only shows the calendars the user has switched on, so they are switched to the chosen ones and back
        public async Task<List<UnitEventInput>> GetEventsForCalendars(DateTime from, DateTime to, string[] calendarIds, CancellationToken cancellationToken = default, Action<string>? progress = null)
        {
            await _programService.SetCalendar(calendarIds);
            try
            {
                return await GetEventDetails(from, to, cancellationToken, progress);
            }
            finally
            {
                await _programService.ResetCalendar();
            }
        }

        // Every event in the range, in full (attendance, logbook data) with its scope. Asked for in 90 day windows
        // so that "all time" is many small requests rather than one very large one.
        private async Task<List<UnitEventInput>> GetEventDetails(DateTime from, DateTime to, CancellationToken cancellationToken, Action<string>? progress)
        {
            var userId = _storageService.GetProfilesResult?.profiles?.FirstOrDefault()?.member?.id ?? "";
            var events = new List<UnitEventInput>();
            var seen = new HashSet<string>();
            var windows = Math.Max(1, (int)Math.Ceiling(((to.Date - from.Date).TotalDays + 1) / 90));
            var window = 0;
            for (var windowStart = from.Date; windowStart <= to.Date; windowStart = windowStart.AddDays(90))
            {
                window++;
                progress?.Invoke($"events, period {window} of {windows}");
                var windowEnd = windowStart.AddDays(89) < to.Date ? windowStart.AddDays(89) : to.Date;
                var result = await _terrainAPIService.GetEventsAsync(userId, windowStart.AddDays(-1), windowEnd.AddDays(1));
                var summaries = result?.results ?? new Topo.Model.Program.Event[0];
                var done = 0;
                foreach (var summary in summaries)
                {
                    done++;
                    if (!seen.Add(summary.id))
                        continue;
                    cancellationToken.ThrowIfCancellationRequested();
                    progress?.Invoke($"events, period {window} of {windows}: event {done} of {summaries.Length}");
                    var detail = await _terrainAPIService.GetEventAsync(summary.id);
                    if (detail != null)
                        events.Add(new UnitEventInput { Scope = summary.invitee_type, Event = detail });
                }
            }
            return events;
        }
    }
}
