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
        public bool Active => StoreFound && StartedFrom.HasValue;
    }

    public interface IYearsOfServiceService
    {
        // Whether this copy of Topo shows years of service (off until the leader turns it on in Settings). While it is off nothing is read
        // from the store, because reading it briefly switches the group calendar on in Terrain.
        Task<bool> IsEnabledAsync();
        Task SetEnabledAsync(bool enabled);
        // Reads the group's store (from a copy kept for a few minutes unless refresh is asked for)
        Task<YearsOfServiceStatus> StatusAsync(bool refresh = false);
        // The years of service to hand over for the unit's youth: the anniversaries passed since tracking started that the store has no record of.
        // Empty when tracking has not been started, and when the store cannot be read.
        Task<List<AwardRow>> PendingAsync(string unitId);
        // The years of service the store holds a record of for the unit's youth
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
    // kept in the group's badge store (docs/badge-store.md), and the list of what to hand over is worked out each time from Terrain's own
    // records of when each member's service began.
    public class YearsOfServiceService : IYearsOfServiceService
    {
        private static readonly TimeSpan StoreCacheFor = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan MembersCacheFor = TimeSpan.FromMinutes(30);

        private readonly StorageService _storageService;
        private readonly ITerrainAPIService _terrainAPIService;
        private readonly IMembersService _membersService;
        private readonly IBadgeStoreService _badgeStoreService;
        private readonly ILocalStorageService _localStorage;

        // The store is read one call at a time: reading it switches the group calendar on and puts it back
        private readonly SemaphoreSlim _storeGate = new SemaphoreSlim(1, 1);
        private BadgeStoreContents? _store;
        private string _storeProblem = "";
        private DateTime _storeReadAt = DateTime.MinValue;
        private HashSet<string> _trusted = new HashSet<string>();
        private readonly Dictionary<string, (List<ServiceMember> members, DateTime readAt)> _membersCache = new();

        public YearsOfServiceService(StorageService storageService, ITerrainAPIService terrainAPIService, IMembersService membersService, IBadgeStoreService badgeStoreService, ILocalStorageService localStorage)
        {
            _localStorage = localStorage;
            _storageService = storageService;
            _terrainAPIService = terrainAPIService;
            _membersService = membersService;
            _badgeStoreService = badgeStoreService;
        }

        private string EnabledKey => $"TopoYearsOfService:{GroupId}";

        public async Task<bool> IsEnabledAsync() => !string.IsNullOrEmpty(GroupId) && await _localStorage.GetItemAsync<bool>(EnabledKey);

        public async Task SetEnabledAsync(bool enabled)
        {
            await _localStorage.SetItemAsync(EnabledKey, enabled);
            _store = null;
        }

        public async Task<YearsOfServiceStatus> StatusAsync(bool refresh = false)
        {
            var contents = await ReadStoreAsync(refresh);
            if (contents == null)
            {
                return new YearsOfServiceStatus { Problem = _storeProblem };
            }
            return new YearsOfServiceStatus
            {
                StoreFound = contents.StoreEvents > 0,
                StartedFrom = YearsOfServiceAwards.StartedFrom(contents, GroupId),
                Recorded = YearsOfServiceAwards.RecordKeys(contents).Count,
                IgnoredRecords = contents.IgnoredUntrustedAuthor + contents.IgnoredUnreadable + contents.IgnoredNewerVersion
            };
        }

        public async Task<List<AwardRow>> PendingAsync(string unitId)
        {
            if (!await IsEnabledAsync())
            {
                return new List<AwardRow>();
            }
            var contents = await ReadStoreAsync(false);
            var from = contents == null ? null : YearsOfServiceAwards.StartedFrom(contents, GroupId);
            if (contents == null || contents.StoreEvents == 0 || from == null)
            {
                return new List<AwardRow>();
            }
            return PendingFor(await MembersAsync(unitId), unitId, from.Value, contents);
        }

        public async Task<List<RecordedAward>> RecordedAsync(string unitId)
        {
            if (!await IsEnabledAsync())
            {
                return new List<RecordedAward>();
            }
            var contents = await ReadStoreAsync(false);
            if (contents == null || contents.StoreEvents == 0)
            {
                return new List<RecordedAward>();
            }
            return YearsOfServiceAwards.Recorded(contents, await MembersAsync(unitId));
        }

        public async Task<AwardWriteSummary> PresentAsync(IEnumerable<AwardRow> rows, DateTime presented)
        {
            var summary = new AwardWriteSummary();
            // Read fresh just before writing, so a year a leader on another copy of Topo has just recorded is not recorded twice
            var contents = await ReadStoreAsync(true);
            var from = contents == null ? null : YearsOfServiceAwards.StartedFrom(contents, GroupId);
            if (contents == null || contents.StoreEvents == 0 || from == null)
            {
                summary.Failures.Add(contents == null ? $"The years of service records could not be read. {_storeProblem}".Trim() : "Years of service tracking has not been started for the group.");
                return summary;
            }

            foreach (var unit in rows.Where(r => YearsOfServiceAwards.IsYearsOfService(r.AwardId)).GroupBy(r => r.UnitId))
            {
                var pending = PendingFor(await MembersAsync(unit.Key), unit.Key, from.Value, contents);
                foreach (var row in unit)
                {
                    // The years this one covers were not the badge handed over, so they are recorded the same day
                    foreach (var level in new[] { row }.Concat(AwardCheckRows.CoveredBy(row, pending)))
                    {
                        var result = await _badgeStoreService.RecordAwardAsync(GetUser(), GroupId, level.MemberId, level.AwardId, (level.Earned ?? presented).Date, presented.Date, IsTrusted);
                        if (result.Outcome == BadgeStoreWriteOutcome.AlreadyRecorded)
                        {
                            summary.Skipped++;
                        }
                        else if (result.Succeeded)
                        {
                            summary.Done++;
                        }
                        else
                        {
                            summary.Failures.Add($"{level.MemberName}, {level.AwardName}: {result.Message}");
                        }
                    }
                }
            }
            _store = null;
            return summary;
        }

        public async Task<BadgeStoreWriteResult> CreateStoreAsync()
        {
            var result = await _badgeStoreService.CreateStoreEventAsync(GetUser(), GetUserName(), GroupId);
            _store = null;
            return result;
        }

        public async Task<BadgeStoreWriteResult> StartAsync(DateTime from)
        {
            await ReadStoreAsync(true);
            var result = await _badgeStoreService.RecordAwardAsync(GetUser(), GroupId, GroupId, YearsOfServiceAwards.StartAwardId, from.Date, null, IsTrusted);
            _store = null;
            return result;
        }

        private List<AwardRow> PendingFor(List<ServiceMember> members, string unitId, DateTime from, BadgeStoreContents contents)
        {
            var section = SectionOf(unitId);
            if (section.Length == 0)
            {
                return new List<AwardRow>();
            }
            var reached = YearsOfServiceAwards.Reached(members, section, from, DateTime.Today);
            return YearsOfServiceAwards.Pending(reached, YearsOfServiceAwards.RecordKeys(contents));
        }

        // The group's store, or null when it could not be read (and why, in _storeProblem)
        private async Task<BadgeStoreContents?> ReadStoreAsync(bool refresh)
        {
            await _storeGate.WaitAsync();
            try
            {
                if (!refresh && _store != null && DateTime.UtcNow - _storeReadAt < StoreCacheFor)
                {
                    return _store;
                }
                if (string.IsNullOrEmpty(GroupId) || string.IsNullOrEmpty(GetUser()))
                {
                    _storeProblem = "Not signed in to a group.";
                    return null;
                }
                try
                {
                    _trusted = await AdultLeaderIdsAsync();
                    _store = await _badgeStoreService.ReadAsync(GetUser(), GroupId, IsTrusted);
                    _storeProblem = "";
                    _storeReadAt = DateTime.UtcNow;
                    return _store;
                }
                catch (Exception ex)
                {
                    _store = null;
                    _storeProblem = ex.Message;
                    return null;
                }
            }
            finally
            {
                _storeGate.Release();
            }
        }

        // The store event can be opened by everyone in the group, so only a record written by an adult leader of one of the leader's units counts
        private bool IsTrusted(string memberId) => memberId == GetUser() || _trusted.Contains(memberId);

        private async Task<HashSet<string>> AdultLeaderIdsAsync()
        {
            var ids = new HashSet<string>();
            foreach (var unitId in _storageService.Units.Keys)
            {
                foreach (var member in (await _membersService.GetMembersAsync(unitId)).Where(m => m.isAdultLeader == 1))
                {
                    ids.Add(member.id);
                }
            }
            return ids;
        }

        // The unit's youth and the day each one's service in the section began (from the whole unit's records, read once)
        private async Task<List<ServiceMember>> MembersAsync(string unitId)
        {
            if (_membersCache.TryGetValue(unitId, out var cached) && DateTime.UtcNow - cached.readAt < MembersCacheFor)
            {
                return cached.members;
            }
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
            _membersCache[unitId] = (members, DateTime.UtcNow);
            return members;
        }

        private string GroupId => _storageService.GroupId ?? "";

        private string SectionOf(string unitId) =>
            (_storageService.GetProfilesResult?.profiles ?? new Topo.Model.Login.Profile[0]).FirstOrDefault(p => p.unit?.id == unitId)?.unit?.section ?? "";

        private string GetUser() => _storageService.GetProfilesResult?.profiles?.FirstOrDefault()?.member?.id ?? "";

        private string GetUserName() => _storageService.GetProfilesResult?.profiles?.FirstOrDefault()?.member?.name ?? "";
    }
}
