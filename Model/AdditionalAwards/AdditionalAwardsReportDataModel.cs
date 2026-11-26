namespace Topo.Model.AdditionalAwards
{
    public class AdditionalAwardsReportDataModel
    {
        public List<AdditionalAwardSpecificationListModel> AwardSpecificationsList { get; set; } = new List<AdditionalAwardSpecificationListModel>();
        public List<AdditionalAwardListModel> SortedAdditionalAwardsList { get; set; } = new List<AdditionalAwardListModel>();
        public List<string> DistinctAwards { get; set; } = new List<string>();

        // The same report with a column for every additional award open to the section (joey, cub, scout, venturer or rover), not only
        // those someone in the unit holds, so a leader can see what else is available. Columns stay in Terrain's order; awards no one
        // holds have empty cells. A section Terrain gives no awards for adds nothing.
        public AdditionalAwardsReportDataModel WithAllAvailable(string section)
        {
            var available = AwardSpecificationsList
                .Where(a => a.sections.Any(s => string.Equals(s, section, StringComparison.OrdinalIgnoreCase)))
                .Select(a => a.id);
            var order = AwardSpecificationsList.ToDictionary(a => a.id, a => a.additionalAwardSortIndex);
            return new AdditionalAwardsReportDataModel
            {
                AwardSpecificationsList = AwardSpecificationsList,
                SortedAdditionalAwardsList = SortedAdditionalAwardsList,
                DistinctAwards = DistinctAwards.Concat(available).Distinct()
                    .OrderBy(id => order.TryGetValue(id, out var index) ? index : -1)
                    .ToList()
            };
        }

        // The report for some of the Scouts only, each under the name given for their member id: their rows, with the awards across the
        // top those of the whole unit, so the table has the same columns whoever is in it
        public AdditionalAwardsReportDataModel ForMembers(IReadOnlyDictionary<string, string> namesById)
        {
            var awards = SortedAdditionalAwardsList
                .Where(a => namesById.ContainsKey(a.MemberId))
                .Select(a => a.WithName(namesById[a.MemberId]))
                .OrderBy(a => a.MemberName)
                .ThenBy(a => a.AwardSortIndex)
                .ToList();
            return new AdditionalAwardsReportDataModel
            {
                AwardSpecificationsList = AwardSpecificationsList,
                SortedAdditionalAwardsList = awards,
                DistinctAwards = DistinctAwards
            };
        }
    }
}
