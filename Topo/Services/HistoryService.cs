using Topo.Model.History;
using Topo.Model.Logbook;

namespace Topo.Services
{
    public interface IHistoryService
    {
        public Task<ScoutingHistoryModel> GetScoutingHistory(string memberId, string memberName, string currentRole, Action<string>? progress = null);
    }

    public class HistoryService : IHistoryService
    {
        private readonly StorageService _storageService;
        private readonly ITerrainAPIService _terrainAPIService;

        public HistoryService(StorageService storageService, ITerrainAPIService terrainAPIService)
        {
            _storageService = storageService;
            _terrainAPIService = terrainAPIService;
        }

        public async Task<ScoutingHistoryModel> GetScoutingHistory(string memberId, string memberName, string currentRole, Action<string>? progress = null)
        {
            var input = new ScoutingHistoryInput
            {
                MemberName = memberName,
                AsOf = DateTime.Today,
                CurrentRole = currentRole,
                CurrentSection = SafeSection()
            };

            progress?.Invoke($"{memberName}: milestones");
            input.Milestones = await _terrainAPIService.GetMilestoneResultsForMember(memberId);
            progress?.Invoke($"{memberName}: Outdoor Adventure Skills");
            input.Oas = await _terrainAPIService.GetOASResultsForMember(memberId);
            progress?.Invoke($"{memberName}: Special Interest Areas");
            input.Sia = await _terrainAPIService.GetSIAResultsForMember(memberId);
            progress?.Invoke($"{memberName}: additional awards");
            input.AdditionalAwards = await _terrainAPIService.GetAdditionalAwardResultsForMember(memberId);
            progress?.Invoke($"{memberName}: introductions");
            input.IntroToScouting = await _terrainAPIService.GetIntroductionToScoutingResultsForMember(memberId);
            input.IntroToSection = await _terrainAPIService.GetIntroductionToSectionResultsForMember(memberId);
            progress?.Invoke($"{memberName}: all achievements");
            input.AllAchievements = await _terrainAPIService.GetAllAchievementsForMember(memberId);
            input.UnitNames = await GetUnitNames();
            input.AwardNames = await AdditionalAwardNames.Get(_terrainAPIService);
            progress?.Invoke($"{memberName}: logbook");
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

        // The logbook needs the member's profile taken on, as the logbook report does
        private async Task<List<GetMemberLogbookDetailResultModel>> GetLogbook(string memberId)
        {
            var entries = new List<GetMemberLogbookDetailResultModel>();
            await _terrainAPIService.RevokeAssumedProfiles();
            await _terrainAPIService.AssumeProfile(memberId);
            try
            {
                var summary = await _terrainAPIService.GetMemberLogbookSummary(memberId);
                if (summary != null)
                {
                    foreach (var item in summary.results)
                    {
                        var detail = await _terrainAPIService.GetMemberLogbookDetail(memberId, item.id);
                        if (detail != null)
                            entries.Add(detail);
                    }
                }
            }
            finally
            {
                await _terrainAPIService.RevokeAssumedProfiles();
            }
            return entries;
        }
    }
}
