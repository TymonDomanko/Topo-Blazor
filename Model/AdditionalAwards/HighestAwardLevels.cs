namespace Topo.Model.AdditionalAwards
{
    public static class HighestAwardLevels
    {
        // The highest Camper Award (nights) and Walkabout Award (km) level a member has, and whether it has been presented. Terrain's own
        // records count as presented; a Topo record is presented only when it says so. 0 means none.
        public static (int camper, bool camperPresented, int walkabout, bool walkaboutPresented) For(string memberId, IEnumerable<string> terrainAwardIds, IEnumerable<AwardRow> storeRows)
        {
            var presented = new Dictionary<string, bool>();
            foreach (var id in terrainAwardIds)
                presented[id] = true;
            foreach (var row in storeRows.Where(r => r.MemberId == memberId))
                presented[row.AwardId] = presented.GetValueOrDefault(row.AwardId) || row.Status == AwardRowStatus.Presented;

            var tiers = CamperWalkaboutRules.Tiers(presented.Keys.Select(id => (id, id)));
            var camper = tiers.Where(t => t.IsCamper).OrderByDescending(t => t.Threshold).FirstOrDefault();
            var walkabout = tiers.Where(t => !t.IsCamper).OrderByDescending(t => t.Threshold).FirstOrDefault();
            return (camper?.Threshold ?? 0, camper != null && presented[camper.Id], walkabout?.Threshold ?? 0, walkabout != null && presented[walkabout.Id]);
        }
    }
}
