using Topo.Model.Cache;
using Topo.Model.History;
using Topo.Model.Logbook;

namespace Topo.Services
{
    public interface IHistoryService
    {
        public Task<ScoutingHistoryModel> GetScoutingHistory(string memberId, string memberName, string currentRole);
    }

    public class HistoryService : IHistoryService
    {
        private readonly StorageService _storageService;
        private readonly ITerrainAPIService _terrainAPIService;

        private readonly ITerrainLocks _locks;

        public HistoryService(StorageService storageService, ITerrainAPIService terrainAPIService, ITerrainLocks locks)
        {
            _locks = locks;
            _storageService = storageService;
            _terrainAPIService = terrainAPIService;
        }

        public async Task<ScoutingHistoryModel> GetScoutingHistory(string memberId, string memberName, string currentRole)
        {
            var input = new ScoutingHistoryInput
            {
                MemberName = memberName,
                AsOf = DateTime.Today,
                CurrentRole = currentRole,
                CurrentSection = SafeSection()
            };

            input.Milestones = await _terrainAPIService.GetMilestoneResultsForMember(memberId);
            input.Oas = await _terrainAPIService.GetOASResultsForMember(memberId);
            input.Sia = await _terrainAPIService.GetSIAResultsForMember(memberId);
            input.AdditionalAwards = await _terrainAPIService.GetAdditionalAwardResultsForMember(memberId);
            input.IntroToScouting = await _terrainAPIService.GetIntroductionToScoutingResultsForMember(memberId);
            input.IntroToSection = await _terrainAPIService.GetIntroductionToSectionResultsForMember(memberId);
            input.AllAchievements = await _terrainAPIService.GetAllAchievementsForMember(memberId);
            input.UnitNames = await GetUnitNames();
            input.AwardNames = await AdditionalAwardNames.Get(_terrainAPIService);
            input.Logbook = await GetLogbook(memberId);

            return ScoutingHistoryBuilder.Build(input);
        }

        private string SafeSection()
        {
            try
            {
                return _storageService.Section;
            }
            catch (IndexOutOfRangeException)
            {
                // no unit selected
                return "";
            }
        }

        // The group's own units can be named; units in other groups cannot (Terrain refuses), so are left out
        private async Task<Dictionary<string, string>> GetUnitNames()
        {
            var names = new Dictionary<string, string>();
            foreach (var unit in _storageService.Units ?? new Dictionary<string, string>())
                names[unit.Key] = unit.Value;

            if (!string.IsNullOrEmpty(_storageService.GroupId))
            {
                foreach (var unit in await _terrainAPIService.GetGroupUnitsAsync(_storageService.GroupId))
                    names[unit.id] = unit.name;
            }
            return names;
        }

        // The logbook needs the member's profile taken on, as the logbook report does. Reading through the data cache, the profile is only locked
        // and taken on if something of the logbook has to be read from Terrain.
        private async Task<List<GetMemberLogbookDetailResultModel>> GetLogbook(string memberId)
        {
            var entries = new List<GetMemberLogbookDetailResultModel>();
            IDisposable? profile = null;
            Task? assuming = null;
            async Task AssumeAsync()
            {
                profile = await _locks.ProfileAsync();
                await _terrainAPIService.RevokeAssumedProfiles();
                await _terrainAPIService.AssumeProfile(memberId);
            }
            using var scope = CacheScope.Current == null ? null : CacheScope.Begin(CacheScope.Current.Mode);
            if (scope != null)
            {
                scope.BeforeRead = () => assuming ??= AssumeAsync();
            }
            try
            {
                if (scope == null)
                {
                    await AssumeAsync();
                }
                var summary = await _terrainAPIService.GetMemberLogbookSummary(memberId);
                if (summary != null)
                {
                    foreach (var item in summary.results)
                    {
                        var detail = await _terrainAPIService.GetMemberLogbookDetail(memberId, item.id);
                        if (detail != null)
                        {
                            entries.Add(detail);
                        }
                    }
                }
            }
            finally
            {
                if (profile != null)
                {
                    try
                    {
                        await _terrainAPIService.RevokeAssumedProfiles();
                    }
                    finally
                    {
                        profile.Dispose();
                    }
                }
            }
            return entries;
        }
    }
}
