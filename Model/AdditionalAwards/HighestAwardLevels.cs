namespace Topo.Model.AdditionalAwards
{
    public static class HighestAwardLevels
    {
        // The highest Camper Award (nights) and Walkabout Award (km) level a member has in Terrain's own records. 0 means none.
        public static (int camper, int walkabout) For(IEnumerable<string> terrainAwardIds)
        {
            var tiers = CamperWalkaboutRules.Tiers(terrainAwardIds.Select(id => (id, id)));
            return (tiers.Where(t => t.IsCamper).Select(t => t.Threshold).DefaultIfEmpty(0).Max(),
                    tiers.Where(t => !t.IsCamper).Select(t => t.Threshold).DefaultIfEmpty(0).Max());
        }
    }
}
