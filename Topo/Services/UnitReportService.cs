using Topo.Model;
using Topo.Model.Milestone;
using Topo.Model.Progress;
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

            // Badges come from each current youth member's records. Read each kind once for the whole unit instead of once per youth:
            // every record carries its member_id, so ToLookup hands back one member's records (none gives an empty list).
            // The records include earlier sections, which the builder filters out.
            var youth = input.CurrentMembers.Where(m => !UnitReportBuilder.IsLeader(m)).ToList();
            // A unit with no youth (an adults-only team) has no badge records to read
            if (youth.Count > 0)
            {
                progress?.Invoke("badges");
                var milestonesByMember = (await _terrainAPIService.GetUnitAchievementsOfType<GetMilestoneResultModel>(unitId, "milestone")).results.ToLookup(r => r.member_id);
                var oasByMember = (await _terrainAPIService.GetUnitAchievementsOfType<Topo.Model.OAS.GetUnitAchievementsResultsModel>(unitId, "outdoor_adventure_skill")).results.ToLookup(r => r.member_id);
                var siaByMember = (await _terrainAPIService.GetUnitAchievementsOfType<Topo.Model.SIA.GetSIAResultsModel>(unitId, "special_interest_area")).results.ToLookup(r => r.member_id);
                var peakByMember = (await _terrainAPIService.GetUnitAchievementsOfType<GetPeakAwardsResultsModel>(unitId, "peak_award")).results.ToLookup(r => r.member_id);
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var member in youth)
                {
                    input.BadgesByMember[member.id] = new UnitMemberBadges
                    {
                        Milestones = new GetMilestoneResultModel { results = milestonesByMember[member.id].ToArray() },
                        Oas = new Topo.Model.OAS.GetUnitAchievementsResultsModel { results = oasByMember[member.id].ToArray() },
                        Sia = new Topo.Model.SIA.GetSIAResultsModel { results = siaByMember[member.id].ToArray() },
                        Peak = new GetPeakAwardsResultsModel { results = peakByMember[member.id].ToArray() }
                    };
                }
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
        //
        // The windows don't depend on each other, so their lists are asked for together. Then each event is a request of its
        // own (the list has no attendance), and those are read a few at a time, in the order they were listed, instead of
        // one after another. An event that shows up in two windows is read once.
        private async Task<List<UnitEventInput>> GetEventDetails(DateTime from, DateTime to, CancellationToken cancellationToken, Action<string>? progress)
        {
            var userId = _storageService.GetProfilesResult?.profiles?.FirstOrDefault()?.member?.id ?? "";
            var windows = new List<(DateTime start, DateTime end)>();
            for (var windowStart = from.Date; windowStart <= to.Date; windowStart = windowStart.AddDays(90))
                windows.Add((windowStart, windowStart.AddDays(89) < to.Date ? windowStart.AddDays(89) : to.Date));

            progress?.Invoke($"events, {windows.Count} period{(windows.Count == 1 ? "" : "s")}");
            var lists = await Task.WhenAll(windows.Select(w => _terrainAPIService.GetEventsAsync(userId, w.start.AddDays(-1), w.end.AddDays(1))));
            cancellationToken.ThrowIfCancellationRequested();

            var seen = new HashSet<string>();
            var summaries = lists.SelectMany(l => l?.results ?? new Topo.Model.Program.Event[0]).Where(e => seen.Add(e.id)).ToList();

            var done = 0;
            var details = await ReadsAtOnce.SelectAsync(summaries, async summary =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var detail = await _terrainAPIService.GetEventAsync(summary.id);
                progress?.Invoke($"events: {Interlocked.Increment(ref done)} of {summaries.Count}");
                return detail;
            });

            return summaries.Zip(details, (summary, detail) => (summary, detail))
                .Where(x => x.detail != null)
                .Select(x => new UnitEventInput { Scope = x.summary.invitee_type, Event = x.detail! })
                .ToList();
        }
    }
}
