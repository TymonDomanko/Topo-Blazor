using Blazored.LocalStorage;
using Topo.Model;
using Topo.Model.AdditionalAwards;
using Topo.Model.BadgeStore;
using Topo.Model.Members;
using Topo.Model.Milestone;
using Topo.Model.Progress;
using Topo.Model.Wallchart;
using Topo.Model.YearsOfService;

namespace Topo.Services
{
    // Where the group's years of service tracking stands
    public class YearsOfServiceStatus
    {
        // The group has the store event Topo keeps its records in
        public bool StoreFound { get; set; }
        // The day tracking started from; null until a leader starts it
        public DateTime? StartedFrom { get; set; }
        // Why the store could not be read, when it could not
        public string Problem { get; set; } = "";
        public int Recorded { get; set; }
        public int IgnoredRecords { get; set; }
        // When the background check last finished (null if it has not yet), and how many Scouts it found
        public DateTime? ScannedAtUtc { get; set; }
        public int Scouts { get; set; }
    }

    public class YearsOfServiceScanResult
    {
        public bool Skipped { get; set; }
        // Stopped because a page needed Terrain; it will try again shortly
        public bool Stopped { get; set; }
        public int Units { get; set; }
        public int Scouts { get; set; }
        public string Problem { get; set; } = "";
    }

    public interface IYearsOfServiceService
    {
        // Whether this copy of Topo shows years of service: on unless the leader turns it off in Settings. While it is off nothing is read
        // from the store, because reading it briefly switches the group calendar on in Terrain.
        Task<bool> IsEnabledAsync();
        Task SetEnabledAsync(bool enabled);
        // Whether the background check is due: turned on, and none has finished in the last day
        Task<bool> IsScanDueAsync();
        // The background check. Reads the group's store and each unit's records (one unit at a time, in gaps between the leader's own requests
        // when pacing is given) and keeps what it finds; the pages read that, not Terrain.
        Task<YearsOfServiceScanResult> ScanAsync(ICheckPacing? pacing);
        // The years of service recorded in the group's store, read from Terrain's calendar comments now, for a report. Nothing when it is not
        // turned on or the group has no store; when Terrain cannot be read, what the last check kept.
        Task<List<BadgeStoreRecord>> RecordsForReportAsync();
        // Where tracking stands, from the last check (or from Terrain itself when refresh is asked for)
        Task<YearsOfServiceStatus> StatusAsync(bool refresh = false);
        // The years of service to hand over for the unit's youth: the anniversaries passed since tracking started that the store has no record of.
        // Worked out from the last check, with no call to Terrain. Empty until a check has run, and when tracking has not been started.
        Task<List<AwardRow>> PendingAsync(string unitId);
        // The years of service the store held a record of at the last check, for the unit's youth
        Task<List<RecordedAward>> RecordedAsync(string unitId);
        // Records the years of service in the store as awarded and presented on a day, with the lower years each covers. A year already
        // recorded is skipped. The caller has already confirmed.
        Task<AwardWriteSummary> PresentAsync(IEnumerable<AwardRow> rows, DateTime presented);
        // Creates the group's store event. Only a group leader can, and it adds an event everyone in the group can see, so only on a leader's say so.
        Task<BadgeStoreWriteResult> CreateStoreAsync();
        // Starts tracking: anniversaries from this day on are listed
        Task<BadgeStoreWriteResult> StartAsync(DateTime from);
    }

    // Years of service badges, one for each year in each section (Model/YearsOfService). Terrain has nowhere to record them, so the records are
    // kept in the group's badge store (docs/badge-store.md). A background check, run with the Camper and Walkabout one, reads Terrain's own
    // records of when each member's service began and the store; the pages then work out what to hand over from that.
    public class YearsOfServiceService : IYearsOfServiceService
    {
        private readonly StorageService _storageService;
        private readonly ITerrainAPIService _terrainAPIService;
        private readonly IMembersService _membersService;
        private readonly IBadgeStoreService _badgeStoreService;
        private readonly ILocalStorageService _localStorage;

        // The store is read one call at a time: reading it switches the group calendar on and puts it back
        private readonly SemaphoreSlim _storeGate = new SemaphoreSlim(1, 1);
        private YearsOfServiceScanCache? _cache;
        private string _cacheGroup = "";
        private bool? _enabled;
        private string _enabledGroup = "";
        // Counts the writes this copy has made, so a check that was running while one was made can tell its copy of the store is out of date
        private int _writes;
        private HashSet<string> _trusted = new HashSet<string>();

        public YearsOfServiceService(StorageService storageService, ITerrainAPIService terrainAPIService, IMembersService membersService, IBadgeStoreService badgeStoreService, ILocalStorageService localStorage)
        {
            _storageService = storageService;
            _terrainAPIService = terrainAPIService;
            _membersService = membersService;
            _badgeStoreService = badgeStoreService;
            _localStorage = localStorage;
        }

        private string EnabledKey => $"TopoYearsOfService:{GroupId}";
        private string CacheKey => $"TopoYosScan:{GroupId}";

        public async Task<bool> IsEnabledAsync()
        {
            if (string.IsNullOrEmpty(GroupId))
            {
                return false;
            }
            if (_enabled == null || _enabledGroup != GroupId)
            {
                // On unless the leader has turned it off
                _enabled = await _localStorage.GetItemAsync<bool?>(EnabledKey) ?? true;
                _enabledGroup = GroupId;
            }
            return _enabled.Value;
        }

        public async Task SetEnabledAsync(bool enabled)
        {
            _enabled = enabled;
            _enabledGroup = GroupId;
            await _localStorage.SetItemAsync(EnabledKey, enabled);
            // Turning it on starts afresh, so the first check is due at once
            var cache = await LoadCacheAsync();
            if (enabled)
            {
                cache.ScannedAtUtc = null;
                cache.AttemptedAtUtc = null;
                await SaveCacheAsync();
            }
        }

        public async Task<bool> IsScanDueAsync() =>
            await IsEnabledAsync() && YearsOfServiceSchedule.IsDue(await LoadCacheAsync(), true, DateTime.UtcNow);

        public async Task<YearsOfServiceScanResult> ScanAsync(ICheckPacing? pacing)
        {
            var result = new YearsOfServiceScanResult();
            if (!await IsEnabledAsync())
            {
                result.Skipped = true;
                return result;
            }
            var cache = await LoadCacheAsync();
            cache.AttemptedAtUtc = DateTime.UtcNow;
            await SaveCacheAsync();
            try
            {
                await WaitForGapAsync(pacing);
                var writesAtRead = _writes;
                var store = await ReadStoreAsync();

                var members = new List<ServiceMember>();
                foreach (var unitId in _storageService.Units.Keys)
                {
                    if (SectionOf(unitId).Length == 0)
                    {
                        continue;
                    }
                    await WaitForGapAsync(pacing);
                    members.AddRange(await ReadUnitAsync(unitId));
                    result.Units++;
                }

                // A record written from this copy while the check ran is not in what it read, so read the store again rather than lose it
                if (_writes != writesAtRead)
                {
                    await WaitForGapAsync(pacing);
                    store = await ReadStoreAsync();
                }
                cache.Store = store;
                cache.Members = members;
                cache.ScannedAtUtc = DateTime.UtcNow;
                cache.Problem = "";
                result.Scouts = members.Count;
            }
            catch (OperationCanceledException)
            {
                result.Stopped = true;
            }
            catch (Exception ex)
            {
                cache.Problem = ex.Message;
                result.Problem = ex.Message;
            }
            await SaveCacheAsync();
            return result;
        }

        private static async Task WaitForGapAsync(ICheckPacing? pacing)
        {
            if (pacing != null)
            {
                await pacing.WaitForGapAsync();
                pacing.ThrowIfPageNeedsTerrain();
            }
        }

        public async Task<List<BadgeStoreRecord>> RecordsForReportAsync()
        {
            if (!await IsEnabledAsync())
            {
                return new List<BadgeStoreRecord>();
            }
            var cache = await LoadCacheAsync();
            try
            {
                cache.Store = await ReadStoreAsync();
                await SaveCacheAsync();
            }
            catch (Exception)
            {
                // The report goes ahead with what the last check kept
            }
            return cache.Store == null
                ? new List<BadgeStoreRecord>()
                : cache.Store.Records.Where(r => YearsOfServiceAwards.IsYearsOfService(r.AwardId)).ToList();
        }

        public async Task<YearsOfServiceStatus> StatusAsync(bool refresh = false)
        {
            var cache = await LoadCacheAsync();
            if (refresh)
            {
                try
                {
                    cache.Store = await ReadStoreAsync();
                    cache.Problem = "";
                    await SaveCacheAsync();
                }
                catch (Exception ex)
                {
                    return new YearsOfServiceStatus { Problem = ex.Message, ScannedAtUtc = cache.ScannedAtUtc, Scouts = cache.Members.Count };
                }
            }
            var contents = cache.Store;
            return new YearsOfServiceStatus
            {
                StoreFound = contents != null && contents.StoreEvents > 0,
                StartedFrom = contents == null ? null : YearsOfServiceAwards.StartedFrom(contents, GroupId),
                Recorded = contents == null ? 0 : YearsOfServiceAwards.RecordKeys(contents).Count,
                IgnoredRecords = contents == null ? 0 : contents.IgnoredUntrustedAuthor + contents.IgnoredUnreadable + contents.IgnoredNewerVersion,
                Problem = cache.Problem,
                ScannedAtUtc = cache.ScannedAtUtc,
                Scouts = cache.Members.Count
            };
        }

        public async Task<List<AwardRow>> PendingAsync(string unitId)
        {
            if (!await IsEnabledAsync())
            {
                return new List<AwardRow>();
            }
            var cache = await LoadCacheAsync();
            var from = cache.Store == null ? null : YearsOfServiceAwards.StartedFrom(cache.Store, GroupId);
            if (cache.Store == null || cache.Store.StoreEvents == 0 || from == null)
            {
                return new List<AwardRow>();
            }
            return PendingFor(cache.Members, unitId, from.Value, cache.Store);
        }

        public async Task<List<RecordedAward>> RecordedAsync(string unitId)
        {
            if (!await IsEnabledAsync())
            {
                return new List<RecordedAward>();
            }
            var cache = await LoadCacheAsync();
            if (cache.Store == null || cache.Store.StoreEvents == 0)
            {
                return new List<RecordedAward>();
            }
            return YearsOfServiceAwards.Recorded(cache.Store, cache.Members.Where(m => m.UnitId == unitId));
        }

        public async Task<AwardWriteSummary> PresentAsync(IEnumerable<AwardRow> rows, DateTime presented)
        {
            var summary = new AwardWriteSummary();
            var cache = await LoadCacheAsync();
            // Read fresh just before writing, so a year a leader on another copy of Topo has just recorded is not recorded twice
            BadgeStoreContents contents;
            try
            {
                contents = await ReadStoreAsync();
            }
            catch (Exception ex)
            {
                summary.Failures.Add($"The years of service records could not be read. {ex.Message}".Trim());
                return summary;
            }
            cache.Store = contents;
            var from = YearsOfServiceAwards.StartedFrom(contents, GroupId);
            if (contents.StoreEvents == 0 || from == null)
            {
                summary.Failures.Add("Years of service tracking has not been started for the group.");
                return summary;
            }

            foreach (var unit in rows.Where(r => YearsOfServiceAwards.IsYearsOfService(r.AwardId)).GroupBy(r => r.UnitId))
            {
                var pending = PendingFor(cache.Members, unit.Key, from.Value, contents);
                foreach (var row in unit)
                {
                    // The years this one covers were not the badge handed over, so they are recorded the same day
                    foreach (var level in new[] { row }.Concat(AwardCheckRows.CoveredBy(row, pending)))
                    {
                        var awarded = (level.Earned ?? presented).Date;
                        var result = await _badgeStoreService.RecordAwardAsync(GetUser(), GroupId, level.MemberId, level.AwardId, awarded, presented.Date, IsTrusted);
                        if (result.Outcome == BadgeStoreWriteOutcome.AlreadyRecorded)
                        {
                            summary.Skipped++;
                        }
                        else if (result.Succeeded)
                        {
                            summary.Done++;
                            // So the lists are right straight away, before the next check reads the store again
                            Remember(contents, level.MemberId, level.AwardId, awarded, presented.Date);
                            _writes++;
                        }
                        else
                        {
                            summary.Failures.Add($"{level.MemberName}, {level.AwardName}: {result.Message}");
                        }
                    }
                }
            }
            await SaveCacheAsync();
            return summary;
        }

        public async Task<BadgeStoreWriteResult> CreateStoreAsync()
        {
            var result = await _badgeStoreService.CreateStoreEventAsync(GetUser(), GetUserName(), GroupId);
            if (result.Succeeded)
            {
                var cache = await LoadCacheAsync();
                cache.Store ??= new BadgeStoreContents();
                cache.Store.StoreEvents = Math.Max(1, cache.Store.StoreEvents);
                _writes++;
                await SaveCacheAsync();
            }
            return result;
        }

        public async Task<BadgeStoreWriteResult> StartAsync(DateTime from)
        {
            var cache = await LoadCacheAsync();
            BadgeStoreContents current;
            try
            {
                current = await ReadStoreAsync();
            }
            catch (Exception ex)
            {
                return new BadgeStoreWriteResult { Outcome = BadgeStoreWriteOutcome.Failed, Message = $"The records could not be read. {ex.Message}".Trim() };
            }
            cache.Store = current;
            var result = await _badgeStoreService.RecordAwardAsync(GetUser(), GroupId, GroupId, YearsOfServiceAwards.StartAwardId, from.Date, null, IsTrusted);
            if (result.Succeeded)
            {
                Remember(current, GroupId, YearsOfServiceAwards.StartAwardId, from.Date, null);
                _writes++;
            }
            await SaveCacheAsync();
            return result;
        }

        // Puts a record just written into the kept copy of the store (the latest for a member and award wins)
        private static void Remember(BadgeStoreContents contents, string memberId, string awardId, DateTime awarded, DateTime? presented)
        {
            contents.Records.RemoveAll(r => r.MemberId == memberId && r.AwardId == awardId);
            contents.Records.Add(new BadgeStoreRecord { MemberId = memberId, AwardId = awardId, Awarded = awarded, Presented = presented, RecordedAt = DateTime.UtcNow });
        }

        private List<AwardRow> PendingFor(IEnumerable<ServiceMember> members, string unitId, DateTime from, BadgeStoreContents contents)
        {
            var section = SectionOf(unitId);
            if (section.Length == 0)
            {
                return new List<AwardRow>();
            }
            var reached = YearsOfServiceAwards.Reached(members.Where(m => m.UnitId == unitId), section, from, DateTime.Today);
            return YearsOfServiceAwards.Pending(reached, YearsOfServiceAwards.RecordKeys(contents));
        }

        // The group's store from Terrain, one read at a time. Throws when Terrain cannot be read.
        private async Task<BadgeStoreContents> ReadStoreAsync()
        {
            await _storeGate.WaitAsync();
            try
            {
                if (string.IsNullOrEmpty(GroupId) || string.IsNullOrEmpty(GetUser()))
                {
                    throw new InvalidOperationException("Not signed in to a group.");
                }
                _trusted = await TrustedLeaders.AdultLeaderIdsAsync(_storageService, _membersService);
                return await _badgeStoreService.ReadAsync(GetUser(), GroupId, IsTrusted);
            }
            finally
            {
                _storeGate.Release();
            }
        }

        // The store event can be opened by everyone in the group, so only a record written by an adult leader of one of the leader's units counts
        private bool IsTrusted(string memberId) => memberId == GetUser() || _trusted.Contains(memberId);

        // The unit's youth and the day each one's service in the section began (from the whole unit's records, read once)
        private async Task<List<ServiceMember>> ReadUnitAsync(string unitId)
        {
            var section = SectionOf(unitId);
            var unitName = _storageService.Units.TryGetValue(unitId, out var name) ? name : "";
            var milestonesRead = _terrainAPIService.GetUnitAchievementsOfType<GetMilestoneResultModel>(unitId, "milestone");
            var introRead = _terrainAPIService.GetUnitAchievementsOfType<GetIntroductionResultsModel>(unitId, "intro_section");
            await Task.WhenAll(milestonesRead, introRead);
            var milestones = milestonesRead.Result.results.ToLookup(r => r.member_id);
            var intros = introRead.Result.results.ToLookup(r => r.member_id);

            var members = new List<ServiceMember>();
            foreach (var member in (await _membersService.GetMembersAsync(unitId)).Where(m => m.isAdultLeader == 0))
            {
                var firstEvent = YearsOfServiceCalculator.FirstEvent(milestones[member.id].Where(r => r.section == section));
                var intro = AchievementSelection.LatestAwardedIntro(intros[member.id], section);
                var introDate = intro == null ? (DateTime?)null : AwardDate.Resolve(intro.date_awarded, intro.imported?.date_awarded, intro.status_updated);
                members.Add(new ServiceMember
                {
                    MemberId = member.id,
                    MemberName = $"{member.first_name} {member.last_name}".Trim(),
                    SortName = $"{member.last_name} {member.first_name}".Trim(),
                    FirstName = member.first_name,
                    LastName = member.last_name,
                    UnitId = unitId,
                    Unit = unitName,
                    Start = firstEvent ?? introDate
                });
            }
            return members;
        }

        private async Task<YearsOfServiceScanCache> LoadCacheAsync()
        {
            var group = GroupId;
            if (_cache == null || _cacheGroup != group)
            {
                _cache = string.IsNullOrEmpty(group) ? new YearsOfServiceScanCache() : await _localStorage.GetItemAsync<YearsOfServiceScanCache>(CacheKey) ?? new YearsOfServiceScanCache();
                _cacheGroup = group;
            }
            return _cache;
        }

        private async Task SaveCacheAsync()
        {
            if (_cache != null && !string.IsNullOrEmpty(GroupId))
            {
                await _localStorage.SetItemAsync(CacheKey, _cache);
            }
        }

        private string GroupId => _storageService.GroupId ?? "";

        private string SectionOf(string unitId) =>
            (_storageService.GetProfilesResult?.profiles ?? new Topo.Model.Login.Profile[0]).FirstOrDefault(p => p.unit?.id == unitId)?.unit?.section ?? "";

        private string GetUser() => _storageService.GetProfilesResult?.profiles?.FirstOrDefault()?.member?.id ?? "";

        private string GetUserName() => _storageService.GetProfilesResult?.profiles?.FirstOrDefault()?.member?.name ?? "";
    }
}
