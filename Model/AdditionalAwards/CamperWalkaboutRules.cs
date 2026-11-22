using System.Globalization;

namespace Topo.Model.AdditionalAwards
{
    // A Camper Award (nights camped) or Walkabout Award (km hiked) at one level
    public class AwardTier
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        // "camper" counts nights, "walkabout" counts km
        public string Family { get; set; } = "";
        public int Threshold { get; set; }
        public bool IsCamper => Family == "camper";
    }

    // One logbook entry, reduced to what counts toward the two awards
    public class LogbookAwardEntry
    {
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        public double Nights { get; set; }
        public double Kilometres { get; set; }
    }

    public static class CamperWalkaboutRules
    {
        // The levels from Terrain's additional award list: camper_award_10, camper_award_25, walkabout_50 and so on. Ids that are not
        // a Camper or Walkabout level are left out. The list comes from Terrain, so a level Terrain adds later appears by itself.
        public static List<AwardTier> Tiers(IEnumerable<(string id, string name)> specifications)
        {
            var tiers = new List<AwardTier>();
            foreach (var (id, name) in specifications)
            {
                var parts = id.Split('_');
                string family;
                string number;
                if (parts.Length == 3 && parts[0] == "camper" && parts[1] == "award")
                {
                    family = "camper";
                    number = parts[2];
                }
                else if (parts.Length == 2 && parts[0] == "walkabout")
                {
                    family = "walkabout";
                    number = parts[1];
                }
                else
                    continue;
                if (int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var threshold) && threshold > 0)
                    tiers.Add(new AwardTier { Id = id, Name = name, Family = family, Threshold = threshold });
            }
            return tiers.OrderBy(t => t.Family).ThenBy(t => t.Threshold).ToList();
        }

        // The tiers a member has reached, from Terrain's own totals (nights camped, km hiked)
        public static List<AwardTier> Reached(IEnumerable<AwardTier> tiers, double nightsCamped, double kmHiked) =>
            tiers.Where(t => (t.IsCamper ? nightsCamped : kmHiked) >= t.Threshold).ToList();

        // A logbook entry as it counts toward the awards: a camp counts a night for each day it runs, and a hike counts its distance
        // (the same way the Logbook report adds them up). distanceWalkaboutMetres is Terrain's figure, in metres.
        public static LogbookAwardEntry? ToEntry(string startDate, string endDate, string[] categories, double distanceWalkaboutMetres)
        {
            if (!DateTime.TryParseExact(startDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start))
                return null;
            if (!DateTime.TryParseExact(endDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))
                end = start;
            var camping = categories.Contains("camping");
            var hiking = categories.Contains("walking_hike");
            return new LogbookAwardEntry
            {
                Start = start,
                End = end,
                Nights = camping ? Math.Max(0, (end - start).TotalDays) : 0,
                Kilometres = hiking ? distanceWalkaboutMetres / 1000.0 : 0
            };
        }

        // The day each reached tier was earned, going through the logbook oldest first. Terrain's total can be more than the entries
        // add up to (progress imported from before the logbook, or entries since removed); that extra is taken as coming first.
        // A tier already reached by that extra alone has no date from the logbook (null), as the logbook cannot say when.
        // A tier is earned on the last day of the entry that took the running total to it.
        public static Dictionary<string, DateTime?> EarnedDates(IEnumerable<AwardTier> reachedTiers, IEnumerable<LogbookAwardEntry> entries,
            double terrainNights, double terrainKm)
        {
            var ordered = entries.OrderBy(e => e.Start).ThenBy(e => e.End).ToList();
            var dates = new Dictionary<string, DateTime?>();
            foreach (var group in reachedTiers.GroupBy(t => t.IsCamper))
            {
                var camper = group.Key;
                var terrainTotal = camper ? terrainNights : terrainKm;
                var entryTotal = ordered.Sum(e => camper ? e.Nights : e.Kilometres);
                var running = Math.Max(0, terrainTotal - entryTotal);
                var runningAfter = new List<(double total, DateTime day)>();
                foreach (var entry in ordered)
                {
                    var counts = camper ? entry.Nights : entry.Kilometres;
                    if (counts <= 0)
                        continue;
                    running += counts;
                    runningAfter.Add((running, entry.End));
                }
                var before = Math.Max(0, terrainTotal - entryTotal);
                foreach (var tier in group)
                {
                    if (before >= tier.Threshold)
                    {
                        dates[tier.Id] = null;
                        continue;
                    }
                    var crossing = runningAfter.FirstOrDefault(r => r.total >= tier.Threshold);
                    dates[tier.Id] = crossing.day == default ? null : crossing.day;
                }
            }
            return dates;
        }
    }
}
