namespace Topo.Model.AdditionalAwards
{
    public class AdditionalAwardsReportDataModel
    {
        public List<AdditionalAwardSpecificationListModel> AwardSpecificationsList { get; set; } = new List<AdditionalAwardSpecificationListModel>();
        public List<AdditionalAwardListModel> SortedAdditionalAwardsList { get; set; } = new List<AdditionalAwardListModel>();
        public List<string> DistinctAwards { get; set; } = new List<string>();

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
