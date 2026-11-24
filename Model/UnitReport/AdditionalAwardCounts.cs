using Topo.Model.AdditionalAwards;

namespace Topo.Model.UnitReport
{
    public static class AdditionalAwardCounts
    {
        // Each award with how many were given, by name as before, except that the Camper Awards and the Walkabout Awards go from the
        // fewest nights or the shortest distance up to the most (10, 25, 50, 75 nights; 10, 50, 100, 150, 200 km), which a sort by name
        // cannot do (it puts 100 km before 50 km). idByName gives the award's id, as the number comes from that.
        public static List<UnitBadgeCount> Sorted(IDictionary<string, int> countsByName, IDictionary<string, string> idByName) =>
            countsByName
                .Select(a =>
                {
                    var tier = idByName.TryGetValue(a.Key, out var id) ? CamperWalkaboutRules.Tiers(new[] { (id, a.Key) }).FirstOrDefault() : null;
                    // The years of service of a section go in order of years too ("2 Years" before "10 Years")
                    if (tier == null && id != null && Topo.Model.YearsOfService.YearsOfServiceAwards.TryParse(id, out var section, out var years))
                    {
                        return (a.Key, a.Value, family: "Years of Service " + section, threshold: years);
                    }
                    return (a.Key, a.Value, family: tier?.Family ?? "", threshold: tier?.Threshold ?? 0);
                })
                // a Camper or Walkabout Award sorts as its family name ("camper", "walkabout") then its number; any other award as its own name
                .OrderBy(a => a.family.Length > 0 ? a.family : a.Key, StringComparer.OrdinalIgnoreCase)
                .ThenBy(a => a.threshold)
                .ThenBy(a => a.Key, StringComparer.OrdinalIgnoreCase)
                .Select(a => new UnitBadgeCount { Name = a.Key, Count = a.Value, Id = idByName.TryGetValue(a.Key, out var awardId) ? awardId : "" })
                .ToList();
    }
}
