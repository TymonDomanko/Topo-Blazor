using Topo.Model.AdditionalAwards;
using Topo.Model.BadgeStore;
using Topo.Model.Members;

namespace Topo.Services
{
    public class AwardCheckResult
    {
        // What to write: each missing level and how it will be recorded, and levels already flagged that a higher level changes
        public List<PlannedAward> Planned { get; set; } = new List<PlannedAward>();
        public bool StoreExists { get; set; }
        public int MembersChecked { get; set; }
        // The background run was stopped because a page needed Terrain; nothing was written and it will try again later
        public bool Stopped { get; set; }
        // Members whose logbook could not be read, so nothing is said about them
        public List<string> MembersNotRead { get; set; } = new List<string>();
        // What the store already holds for this unit, so the page can say so
        public int AlreadyFlagged { get; set; }
        public int AlreadyPresented { get; set; }
    }

    public class AwardWriteSummary
    {
        public int Done { get; set; }
        public int Skipped { get; set; }
        public List<string> Failures { get; set; } = new List<string>();
        public bool NoStoreEvent { get; set; }
        // A page needed Terrain, so the rest was left for the next run
        public bool Stopped { get; set; }
    }

    public interface IAdditionalAwardCheckService
    {
        // Reads each Scout's logbook in the units (one profile at a time) and works out which Camper and Walkabout awards they have earned
        // that nothing records. A Scout in more than one unit is checked once.
        // With pacing (the background run) it waits for a gap before each Scout and stops at once if a page needs Terrain.
        Task<AwardCheckResult> CheckLogbooksAsync(IReadOnlyList<string> unitIds, Action<string>? progress, ICheckPacing? pacing = null);
        // The Camper and Walkabout awards the store holds for the unit's members, to be presented or presented
        Task<List<AwardRow>> StoredAsync(string unitId);
        // Writes the planned awards to the store (the caller has already confirmed)
        Task<AwardWriteSummary> FlagAsync(IEnumerable<PlannedAward> planned, ICheckPacing? pacing = null);
        // Records awards in the store as presented on a day
        Task<AwardWriteSummary> PresentAsync(IEnumerable<AwardRow> rows, DateTime presented);
        // Creates the group's store event (the caller has already confirmed)
        Task<BadgeStoreWriteResult> CreateStoreAsync(ICheckPacing? pacing = null);
    }

    // The Camper and Walkabout awards: Terrain counts the nights and km but never records the award, so Topo works out from the
    // logbook when each was earned, flags it as to be presented in the badge store, and records it as presented.
    public class AdditionalAwardCheckService : IAdditionalAwardCheckService
    {
        private readonly StorageService _storageService;
        private readonly ITerrainAPIService _terrainAPIService;
        private readonly IMembersService _membersService;
        private readonly IBadgeStoreService _badgeStoreService;

        public AdditionalAwardCheckService(StorageService storageService, ITerrainAPIService terrainAPIService, IMembersService membersService, IBadgeStoreService badgeStoreService)
        {
            _storageService = storageService;
            _terrainAPIService = terrainAPIService;
            _membersService = membersService;
            _badgeStoreService = badgeStoreService;
        }

        private string UserId => _storageService.GetProfilesResult?.profiles?.FirstOrDefault()?.member?.id ?? "";
        private string GroupId => _storageService.GroupId ?? "";

        // What a background run has read, kept for the rest of this visit so a run that is stopped carries on where it left off
        private readonly Dictionary<string, (MemberTotals totals, List<LogbookAwardEntry>? entries, DateTime readAt)> _readCache = new();
        private static readonly TimeSpan CacheFor = TimeSpan.FromHours(6);

        public async Task<AwardCheckResult> CheckLogbooksAsync(IReadOnlyList<string> unitIds, Action<string>? progress, ICheckPacing? pacing = null)
        {
            var result = new AwardCheckResult();
            var tiers = await TiersAsync();

            // The Scouts of every unit asked for, each once, with the first unit they were found in
            var youth = new List<MemberListModel>();
            var unitOf = new Dictionary<string, string>();
            var terrainDates = new Dictionary<string, DateTime>();
            foreach (var unitId in unitIds)
            {
                var unitName = _storageService.Units.TryGetValue(unitId, out var name) ? name : "";
                foreach (var member in (await _membersService.GetMembersAsync(unitId)).Where(m => m.isAdultLeader == 0))
                    if (unitOf.TryAdd(member.id, unitName))
                        youth.Add(member);
                progress?.Invoke($"Terrain's award records for {unitName}");
                foreach (var record in (await _terrainAPIService.GetUnitAdditionalAwardAchievements(unitId)).results)
                    terrainDates[record.member_id + "|" + record.achievement_meta.additional_award_id] = TerrainRecordDay(record);
            }

            progress?.Invoke("the badge records");
            // Reading the records switches the group calendar for a moment, so in the background it waits for a gap too
            if (pacing != null)
                await pacing.WaitForGapAsync();
            var snapshot = await _badgeStoreService.SnapshotAsync(UserId, GroupId, await TrustedAsync());
            result.StoreExists = snapshot.StoreExists;
            var forUnits = AwardCheckRows.FromStore(snapshot.Contents, youth.ToDictionary(m => m.id, Name), tiers.ToDictionary(t => t.Id, t => t.Name));
            result.AlreadyFlagged = forUnits.Count(r => r.Status == AwardRowStatus.ToBePresented);
            result.AlreadyPresented = forUnits.Count(r => r.Status == AwardRowStatus.Presented);

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
                            && (cached.entries != null || AwardCheckRows.Missing(new[] { cached.totals }, tiers, terrainDates.Keys.ToHashSet(), snapshot.Contents).Count == 0))
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
                            // The entries are only read for a Scout who has something to flag
                            List<LogbookAwardEntry>? entries = null;
                            var needs = AwardCheckRows.Missing(new[] { memberTotals }, tiers, terrainDates.Keys.ToHashSet(), snapshot.Contents);
                            if (needs.Count > 0)
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

            var missing = AwardCheckRows.Missing(totals, tiers, terrainDates.Keys.ToHashSet(), snapshot.Contents);
            var earnedDates = new Dictionary<string, Dictionary<string, DateTime?>>();
            foreach (var group in missing.GroupBy(m => m.member.MemberId))
            {
                var member = group.First().member;
                earnedDates[group.Key] = CamperWalkaboutRules.EarnedDates(
                    CamperWalkaboutRules.Reached(tiers, member.Nights, member.Kilometres).Where(t => group.Any(g => g.tier.Id == t.Id)),
                    entriesByMember.TryGetValue(group.Key, out var entries) ? entries : new List<LogbookAwardEntry>(),
                    member.Nights, member.Kilometres);
            }
            var rows = AwardCheckRows.ForFlagging(missing, earnedDates);
            foreach (var row in rows)
                row.Unit = unitOf.TryGetValue(row.MemberId, out var unit) ? unit : "";

            // What each is recorded as depends on the higher levels: presented already, or still to be presented
            result.Planned = AwardFlagPlan.Build(rows, tiers, terrainDates, snapshot.Contents, DateTime.Today);
            var members = youth.ToDictionary(m => m.id);
            foreach (var planned in result.Planned.Where(p => p.Row.MemberName.Length == 0))
                if (members.TryGetValue(planned.Row.MemberId, out var member))
                {
                    planned.Row.MemberName = Name(member);
                    planned.Row.SortName = $"{member.last_name} {member.first_name}".Trim();
                    planned.Row.Unit = unitOf.TryGetValue(member.id, out var unit) ? unit : "";
                }
            return result;
        }

        public async Task<List<AwardRow>> StoredAsync(string unitId)
        {
            var tiers = await TiersAsync();
            var members = (await _membersService.GetMembersAsync(unitId)).Where(m => m.isAdultLeader == 0).ToList();
            var contents = await _badgeStoreService.ReadAsync(UserId, GroupId, await TrustedAsync());
            var awardNames = tiers.ToDictionary(t => t.Id, t => t.Name);
            var rows = AwardCheckRows.FromStore(contents, members.ToDictionary(m => m.id, Name), awardNames,
                members.ToDictionary(m => m.id, m => $"{m.last_name} {m.first_name}".Trim()));
            var byId = members.ToDictionary(m => m.id);
            foreach (var row in rows)
            {
                if (byId.TryGetValue(row.MemberId, out var member))
                {
                    row.FirstName = member.first_name;
                    row.LastName = member.last_name;
                }
                if (row.SupersededBy.Length > 0)
                    row.SupersededByName = awardNames.TryGetValue(row.SupersededBy, out var covered) ? covered : row.SupersededBy;
            }
            return rows;
        }

        public async Task<AwardWriteSummary> FlagAsync(IEnumerable<PlannedAward> planned, ICheckPacing? pacing = null)
        {
            var trusted = await TrustedAsync();
            var summary = new AwardWriteSummary();
            foreach (var item in planned)
            {
                // In the background each record waits for a gap between the leader's own requests
                if (pacing != null)
                {
                    try
                    {
                        await pacing.WaitForGapAsync();
                    }
                    catch (OperationCanceledException)
                    {
                        summary.Stopped = true;
                        break;
                    }
                }
                var written = await _badgeStoreService.RecordAwardAsync(UserId, GroupId, item.Row.MemberId, item.Row.AwardId, item.Awarded, item.Presented, trusted, item.SupersededBy);
                Tally(summary, item.Row, written);
                if (written.Outcome == BadgeStoreWriteOutcome.NoStoreEvent)
                    break;
            }
            return summary;
        }

        public async Task<AwardWriteSummary> PresentAsync(IEnumerable<AwardRow> rows, DateTime presented)
        {
            var trusted = await TrustedAsync();
            var summary = new AwardWriteSummary();
            var current = await _badgeStoreService.ReadAsync(UserId, GroupId, trusted);
            foreach (var row in rows)
            {
                var written = await _badgeStoreService.RecordAwardAsync(UserId, GroupId, row.MemberId, row.AwardId, row.Earned ?? presented, presented, trusted);
                Tally(summary, row, written);
                if (written.Outcome == BadgeStoreWriteOutcome.NoStoreEvent)
                    break;
                if (!written.Succeeded)
                    continue;
                // The lower levels this one covered were not the badge handed over, so they are presented the same day
                foreach (var lower in current.Records.Where(r => r.MemberId == row.MemberId && r.SupersededBy == row.AwardId && !r.Presented.HasValue))
                {
                    var covered = await _badgeStoreService.RecordAwardAsync(UserId, GroupId, lower.MemberId, lower.AwardId, lower.Awarded, presented, trusted, lower.SupersededBy);
                    if (!covered.Succeeded)
                        summary.Failures.Add($"{row.MemberName}: {lower.AwardId}: {covered.Message}");
                }
            }
            return summary;
        }

        public async Task<BadgeStoreWriteResult> CreateStoreAsync(ICheckPacing? pacing = null)
        {
            if (pacing != null)
                await pacing.WaitForGapAsync();
            return await _badgeStoreService.CreateStoreEventAsync(UserId, _storageService.MemberName ?? "", GroupId);
        }

        private static void Tally(AwardWriteSummary summary, AwardRow row, BadgeStoreWriteResult written)
        {
            switch (written.Outcome)
            {
                case BadgeStoreWriteOutcome.Written:
                    summary.Done++;
                    break;
                case BadgeStoreWriteOutcome.AlreadyRecorded:
                    summary.Skipped++;
                    break;
                case BadgeStoreWriteOutcome.NoStoreEvent:
                    summary.NoStoreEvent = true;
                    break;
                default:
                    summary.Failures.Add($"{row.MemberName}: {row.AwardName}: {written.Message}");
                    break;
            }
        }

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

        // Only adult leaders' comments count in the store, and at group level that means any unit's leaders
        private async Task<Func<string, bool>> TrustedAsync()
        {
            var leaders = new HashSet<string> { UserId };
            foreach (var unit in _storageService.Units.Keys)
                foreach (var member in await _membersService.GetMembersAsync(unit))
                    if (member.isAdultLeader == 1)
                        leaders.Add(member.id);
            return id => leaders.Contains(id);
        }

        // The day Terrain's own record says the award was given: the imported date, or when the record last changed
        private static DateTime TerrainRecordDay(Topo.Model.AdditionalAwards.Result record) =>
            DateTime.TryParseExact(record.imported?.date_awarded, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var day)
                ? day
                : record.status_updated.ToLocalTime().Date;

        private static string Name(MemberListModel m) => $"{m.first_name} {m.last_name}".Trim();
    }
}
