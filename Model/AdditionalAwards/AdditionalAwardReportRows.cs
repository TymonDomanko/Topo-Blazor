namespace Topo.Model.AdditionalAwards
{
    public static class AdditionalAwardReportRows
    {
        // The Camper and Walkabout Awards the Topo records hold, as rows for the Additional Awards report beside Terrain's own records.
        // An award Terrain already has a record for is left to Terrain's record (which counts as presented). Otherwise a record the store
        // says is presented has its presented date, one still to be presented has an awarded date only (the report colours it as awarded,
        // not presented), and one covered by a higher level is marked NotHighest.
        public static List<AdditionalAwardListModel> FromStore(IEnumerable<AwardRow> stored, ISet<string> terrainKeys, IReadOnlyDictionary<string, AdditionalAwardSpecificationListModel> specifications,
            IReadOnlyDictionary<string, (string name, int nights, int kms)> memberTotals)
        {
            var rows = new List<AdditionalAwardListModel>();
            foreach (var row in stored)
            {
                if (terrainKeys.Contains(row.Key))
                    continue;
                specifications.TryGetValue(row.AwardId, out var specification);
                memberTotals.TryGetValue(row.MemberId, out var totals);
                rows.Add(new AdditionalAwardListModel
                {
                    MemberName = !string.IsNullOrEmpty(totals.name) ? totals.name : row.MemberName,
                    NightsCamped = totals.nights,
                    KMsHiked = totals.kms,
                    AwardId = row.AwardId,
                    AwardName = specification?.name ?? row.AwardName,
                    AwardSortIndex = specification?.additionalAwardSortIndex ?? 0,
                    AwardDate = row.Earned,
                    PresentedDate = row.Presented,
                    AwardUnits = specification?.additionalAwardValue ?? 0,
                    NotHighest = row.Status == AwardRowStatus.NotHighest
                });
            }
            return rows;
        }
    }
}
