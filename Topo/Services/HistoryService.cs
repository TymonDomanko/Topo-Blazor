using Topo.Model;
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

        public HistoryService(StorageService storageService, ITerrainAPIService terrainAPIService)
        {
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

            // None of these depends on another, and none needs the member's profile taken on, so they are all asked for at
            // once instead of one after another.
            var milestones = _terrainAPIService.GetMilestoneResultsForMember(memberId);
            var oas = _terrainAPIService.GetOASResultsForMember(memberId);
            var sia = _terrainAPIService.GetSIAResultsForMember(memberId);
            var additionalAwards = _terrainAPIService.GetAdditionalAwardResultsForMember(memberId);
            var introToScouting = _terrainAPIService.GetIntroductionToScoutingResultsForMember(memberId);
            var introToSection = _terrainAPIService.GetIntroductionToSectionResultsForMember(memberId);
            var allAchievements = _terrainAPIService.GetAllAchievementsForMember(memberId);
            var unitNames = GetUnitNames();
            var awardNames = AdditionalAwardNames.Get(_terrainAPIService);
            await Task.WhenAll(milestones, oas, sia, additionalAwards, introToScouting, introToSection, allAchievements, unitNames, awardNames);
            input.Milestones = milestones.Result;
            input.Oas = oas.Result;
            input.Sia = sia.Result;
            input.AdditionalAwards = additionalAwards.Result;
            input.IntroToScouting = introToScouting.Result;
            input.IntroToSection = introToSection.Result;
            input.AllAchievements = allAchievements.Result;
            input.UnitNames = unitNames.Result;
            input.AwardNames = awardNames.Result;
            // the logbook needs the member's profile taken on, so it comes after
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
                    // each entry's detail is a request of its own: read a few at a time, kept in the order listed
                    var details = await ReadsAtOnce.SelectAsync(summary.results, item => _terrainAPIService.GetMemberLogbookDetail(memberId, item.id));
                    foreach (var detail in details)
                    {
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
