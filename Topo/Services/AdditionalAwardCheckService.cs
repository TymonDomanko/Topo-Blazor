using Blazored.LocalStorage;
using Topo.Model.AdditionalAwards;
using Topo.Model.Members;

namespace Topo.Services
{
    public class AwardScanResult
    {
        public int MembersChecked { get; set; }
        // Levels to hand over, and levels earned that a higher one covers, as of this scan
        public int ToBePresented { get; set; }
        public int NotHighest { get; set; }
        // The background run was stopped because a page needed Terrain; it will try again later
        public bool Stopped { get; set; }
        // Members whose logbook could not be read, so nothing is said about them
        public List<string> MembersNotRead { get; set; } = new List<string>();
    }

    public class AwardWriteSummary
    {
        // Levels written to Terrain, and levels Terrain already had a record of (left alone)
        public int Done { get; set; }
        public int Skipped { get; set; }
        public List<string> Failures { get; set; } = new List<string>();
    }

    // What the last logbook check found, kept between visits
    public class AwardScanCache
    {
        public DateTime ScannedAtUtc { get; set; }
        public List<MissingAward> Missing { get; set; } = new List<MissingAward>();
    }

    public interface IAdditionalAwardCheckService
    {
        // Reads each Scout's logbook in the units (one profile at a time) and works out which Camper and Walkabout awards they have earned that
        // Terrain has no record of, with the day each was earned. Nothing is written. A Scout in more than one unit is checked once.
        // With pacing (the background run) it waits for a gap before each Scout and stops at once if a page needs Terrain.
        Task<AwardScanResult> ScanAsync(IReadOnlyList<string> unitIds, Action<string>? progress, ICheckPacing? pacing = null);
        // What to hand over for the unit's Scouts: the last scan's findings, against Terrain's records as they are now (so one that has been
        // recorded since is no longer listed). Empty until a scan has run.
        Task<List<AwardRow>> PendingAsync(string unitId);
        // Records the awards in Terrain as awarded on a day, and the lower levels each covers. Terrain cannot remove a record, so a level it
        // already has a record of is skipped, and the caller has already confirmed.
        Task<AwardWriteSummary> PresentAsync(IEnumerable<AwardRow> rows, DateTime presented);
    }

    // The Camper and Walkabout awards: Terrain counts the nights and km but never records the award, so Topo works out from the logbook
    // when each was earned, lists it as to be presented, and records it in Terrain when a leader presents it.
    public class AdditionalAwardCheckService : IAdditionalAwardCheckService
    {
        private readonly StorageService _storageService;
        private readonly ITerrainAPIService _terrainAPIService;
        private readonly IMembersService _membersService;
        private readonly ILocalStorageService _localStorage;

        public AdditionalAwardCheckService(StorageService storageService, ITerrainAPIService terrainAPIService, IMembersService membersService, ILocalStorageService localStorage)
        {
            _storageService = storageService;
            _terrainAPIService = terrainAPIService;
            _membersService = membersService;
            _localStorage = localStorage;
        }

        private string CacheKey => $"TopoAwardScan:{_storageService.GroupId}";
        private AwardScanCache? _cache;
        private string _cacheGroup = "";

        // What a background run has read, kept for the rest of this visit so a run that is stopped carries on where it left off
        private readonly Dictionary<string, (MemberTotals totals, List<LogbookAwardEntry>? entries, DateTime readAt)> _readCache = new();
        private static readonly TimeSpan CacheFor = TimeSpan.FromHours(6);

        private async Task<AwardScanCache> LoadCacheAsync()
        {
            var group = _storageService.GroupId ?? "";
            if (_cache == null || _cacheGroup != group)
            {
                _cache = await _localStorage.GetItemAsync<AwardScanCache>(CacheKey) ?? new AwardScanCache();
                _cacheGroup = group;
            }
            return _cache;
        }

        public async Task<AwardScanResult> ScanAsync(IReadOnlyList<string> unitIds, Action<string>? progress, ICheckPacing? pacing = null)
        {
            var result = new AwardScanResult();
            var tiers = await TiersAsync();

            // The Scouts of every unit asked for, each once, with the first unit they were found in
            var youth = new List<MemberListModel>();
            var unitOf = new Dictionary<string, (string id, string name)>();
            var terrainRecords = new HashSet<string>();
            foreach (var unitId in unitIds)
            {
                var unitName = _storageService.Units.TryGetValue(unitId, out var name) ? name : "";
                foreach (var member in (await _membersService.GetMembersAsync(unitId)).Where(m => m.isAdultLeader == 0))
                    if (unitOf.TryAdd(member.id, (unitId, unitName)))
                        youth.Add(member);
                progress?.Invoke($"Terrain's award records for {unitName}");
                foreach (var record in (await _terrainAPIService.GetUnitAdditionalAwardAchievements(unitId)).results)
                    terrainRecords.Add(record.member_id + "|" + record.achievement_meta.additional_award_id);
            }

            // One profile at a time: Terrain keeps only one taken on, and a Scout's logbook can only be read with theirs. In the background
            // each Scout is read in a gap between the leader's own requests, and the profile is handed back at once if a page needs Terrain.
            var totals = new List<MemberTotals>();
            var entriesByMember = new Dictionary<string, List<LogbookAwardEntry>>();
            try
            {
                for (var i = 0; i < youth.Count; i++)
                {
                    var member = youth[i];
                    progress?.Invoke($"{Name(member)} ({i + 1} of {youth.Count})");
                    try
                    {
                        // Read earlier in this visit by a run that was stopped, and still fresh enough
                        if (pacing != null && _readCache.TryGetValue(member.id, out var cached) && DateTime.UtcNow - cached.readAt < CacheFor
                            && (cached.entries != null || AwardCheckRows.Missing(new[] { cached.totals }, tiers, terrainRecords).Count == 0))
                        {
                            totals.Add(cached.totals);
                            if (cached.entries != null)
                                entriesByMember[member.id] = cached.entries;
                            result.MembersChecked++;
                            continue;
                        }

                        if (pacing != null)
                            await pacing.WaitForGapAsync();
                        try
                        {
                            await _terrainAPIService.RevokeAssumedProfiles();
                            await _terrainAPIService.AssumeProfile(member.id);
                            pacing?.ProfileTakenOn();
                            pacing?.ThrowIfPageNeedsTerrain();
                            var metrics = await _terrainAPIService.GetMemberLogbookMetrics(member.id);
                            var memberTotals = new MemberTotals
                            {
                                MemberId = member.id,
                                Name = Name(member),
                                Nights = metrics.results.FirstOrDefault(r => r.name == "total_nights_camped")?.value ?? 0,
                                Kilometres = (metrics.results.FirstOrDefault(r => r.name == "total_distance_hiked")?.value ?? 0) / 1000.0
                            };
                            // The entries are only read for a Scout who has something to hand over
                            List<LogbookAwardEntry>? entries = null;
                            if (AwardCheckRows.Missing(new[] { memberTotals }, tiers, terrainRecords).Count > 0)
                                entries = await ReadEntriesAsync(member.id, pacing);
                            totals.Add(memberTotals);
                            if (entries != null)
                                entriesByMember[member.id] = entries;
                            if (pacing != null)
                                _readCache[member.id] = (memberTotals, entries, DateTime.UtcNow);
                            result.MembersChecked++;
                        }
                        finally
                        {
                            // Handed back straight away, whatever happened, so a page's next request is the leader's own
                            if (pacing != null)
                            {
                                await _terrainAPIService.RevokeAssumedProfiles();
                                pacing.ProfileReturned();
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        result.Stopped = true;
                        break;
                    }
                    catch (Exception)
                    {
                        result.MembersNotRead.Add(Name(member));
                    }
                }
            }
            finally
            {
                await _terrainAPIService.RevokeAssumedProfiles();
            }
            if (result.Stopped)
                return result;

            var members = youth.ToDictionary(m => m.id);
            var missing = new List<MissingAward>();
            foreach (var group in AwardCheckRows.Missing(totals, tiers, terrainRecords).GroupBy(m => m.member.MemberId))
            {
                var member = group.First().member;
                var earned = CamperWalkaboutRules.EarnedDates(
                    CamperWalkaboutRules.Reached(tiers, member.Nights, member.Kilometres).Where(t => group.Any(g => g.tier.Id == t.Id)),
                    entriesByMember.TryGetValue(group.Key, out var entries) ? entries : new List<LogbookAwardEntry>(),
                    member.Nights, member.Kilometres);
                var listed = members[group.Key];
                foreach (var (_, tier) in group)
                    missing.Add(new MissingAward
                    {
                        MemberId = group.Key,
                        MemberName = Name(listed),
                        SortName = $"{listed.last_name} {listed.first_name}".Trim(),
                        FirstName = listed.first_name,
                        LastName = listed.last_name,
                        UnitId = unitOf[group.Key].id,
                        Unit = unitOf[group.Key].name,
                        AwardId = tier.Id,
                        AwardName = tier.Name,
                        Earned = earned.TryGetValue(tier.Id, out var day) ? day : null
                    });
            }

            // Members whose logbook could not be read keep what an earlier scan found for them rather than losing it
            var keep = (await LoadCacheAsync()).Missing.Where(m => result.MembersNotRead.Any(n => n == m.MemberName)).ToList();
            _cache = new AwardScanCache { ScannedAtUtc = DateTime.UtcNow, Missing = missing.Concat(keep).ToList() };
            await _localStorage.SetItemAsync(CacheKey, _cache);

            var rows = AwardCheckRows.Pending(_cache.Missing, tiers, terrainRecords);
            result.ToBePresented = rows.Count(r => r.Status == AwardRowStatus.ToBePresented);
            result.NotHighest = rows.Count(r => r.Status == AwardRowStatus.NotHighest);
            return result;
        }

        public async Task<List<AwardRow>> PendingAsync(string unitId)
        {
            var cache = await LoadCacheAsync();
            var forUnit = cache.Missing.Where(m => m.UnitId == unitId).ToList();
            if (forUnit.Count == 0)
                return new List<AwardRow>();
            var records = await TerrainRecordsAsync(unitId);
            return AwardCheckRows.Pending(forUnit, await TiersAsync(), records);
        }

        public async Task<AwardWriteSummary> PresentAsync(IEnumerable<AwardRow> rows, DateTime presented)
        {
            var summary = new AwardWriteSummary();
            var tiers = await TiersAsync();
            foreach (var unit in rows.GroupBy(r => r.UnitId))
            {
                // Read fresh just before writing: Terrain adds a record every time, so anything it already has is left alone
                var records = await TerrainRecordsAsync(unit.Key);
                var pending = AwardCheckRows.Pending((await LoadCacheAsync()).Missing.Where(m => m.UnitId == unit.Key), tiers, records);
                var items = new List<(string memberId, string awardId, DateTime awarded)>();
                var written = new List<AwardRow>();
                foreach (var row in unit)
                    // The levels this one covers were not the badge handed over, so they are recorded the same day
                    foreach (var level in new[] { row }.Concat(AwardCheckRows.CoveredBy(row, pending)))
                    {
                        if (records.Contains(level.Key) || items.Any(i => i.memberId == level.MemberId && i.awardId == level.AwardId))
                        {
                            summary.Skipped++;
                            continue;
                        }
                        items.Add((level.MemberId, level.AwardId, presented.Date));
                        written.Add(level);
                    }
                if (items.Count == 0)
                    continue;
                var result = await _terrainAPIService.RecordAdditionalAwardsAsync(unit.Key, items);
                if (!result.Success)
                {
                    summary.Failures.Add($"{(_storageService.Units.TryGetValue(unit.Key, out var unitName) ? unitName : "The unit")}: Terrain did not record them (status {result.StatusCode}). {Trim(result.Body)}".Trim());
                    continue;
                }
                summary.Done += items.Count;
                // They are in Terrain now, so no longer found by the last scan
                var cache = await LoadCacheAsync();
                cache.Missing.RemoveAll(m => written.Any(w => w.Key == m.MemberId + "|" + m.AwardId));
                await _localStorage.SetItemAsync(CacheKey, cache);
            }
            return summary;
        }

        private static string Trim(string body) => body.Length > 160 ? body.Substring(0, 160) : body;

        // Terrain's own additional award records for a unit, as member|award
        private async Task<HashSet<string>> TerrainRecordsAsync(string unitId) =>
            (await _terrainAPIService.GetUnitAdditionalAwardAchievements(unitId)).results
                .Select(r => r.member_id + "|" + r.achievement_meta.additional_award_id).ToHashSet();

        // Reads every logbook entry of the Scout whose profile is taken on, reduced to what counts toward the two awards
        private async Task<List<LogbookAwardEntry>> ReadEntriesAsync(string memberId, ICheckPacing? pacing = null)
        {
            var entries = new List<LogbookAwardEntry>();
            var summary = await _terrainAPIService.GetMemberLogbookSummary(memberId);
            foreach (var item in summary?.results ?? new Topo.Model.Logbook.Result[0])
            {
                pacing?.ThrowIfPageNeedsTerrain();
                var detail = await _terrainAPIService.GetMemberLogbookDetail(memberId, item.id);
                if (detail == null)
                    continue;
                var entry = CamperWalkaboutRules.ToEntry(detail.start_date, detail.end_date, detail.categories, detail.distance_walkabout);
                if (entry != null)
                    entries.Add(entry);
            }
            return entries;
        }

        private async Task<List<AwardTier>> TiersAsync()
        {
            var specifications = _storageService.AdditionalAwardSpecifications;
            if (specifications == null || specifications.Count == 0)
            {
                var read = await _terrainAPIService.GetAdditionalAwardSpecifications();
                return CamperWalkaboutRules.Tiers(read.AwardDescriptions.Select(a => (a.id, a.title)));
            }
            return CamperWalkaboutRules.Tiers(specifications.Select(a => (a.id, a.name)));
        }

        private static string Name(MemberListModel m) => $"{m.first_name} {m.last_name}".Trim();
    }
}
