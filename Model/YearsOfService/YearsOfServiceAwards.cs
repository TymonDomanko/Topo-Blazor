using System.Globalization;
using Topo.Model.AdditionalAwards;
using Topo.Model.BadgeStore;

namespace Topo.Model.YearsOfService
{
    // A youth member of a unit and the day their service in the section began (see YearsOfServiceCalculator)
    public class ServiceMember
    {
        public string MemberId { get; set; } = "";
        public string MemberName { get; set; } = "";
        public string SortName { get; set; } = "";
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public string UnitId { get; set; } = "";
        public string Unit { get; set; } = "";
        // The first event they were credited with in the section, or their intro to the section when no event is logged. Null when neither is known.
        public DateTime? Start { get; set; }
    }

    // The Years of Service badge: one for each year in each section. Terrain has no record of it (it is not one of its additional awards), so
    // Topo works out from the day a member's service began when each anniversary passes, lists it as to be presented, and keeps the record of
    // it in the group's badge store (Model/BadgeStore), which every leader's copy of Topo reads.
    //
    // An award has an id of its own, "yos_<section>_<years>" (yos_scout_3), so it sits beside Camper and Walkabout awards in the lists and
    // the store. Only the newest year is the badge to hand over, as with those awards.
    public static class YearsOfServiceAwards
    {
        public const string IdPrefix = "yos_";
        // The store record that says Topo started tracking years of service, and from when. It is kept as an ordinary store record against the
        // group's own id in place of a member's, so the store needs nothing new: its "awarded" day is the day tracking started from.
        public const string StartAwardId = "yos_start";
        // Nobody serves this long, so a bad start date cannot make the list run away
        private const int MostYears = 60;

        public static string AwardId(string section, int years) => $"{IdPrefix}{section.ToLowerInvariant()}_{years}";

        public static bool IsYearsOfService(string awardId) => TryParse(awardId, out _, out _);

        public static bool TryParse(string awardId, out string section, out int years)
        {
            section = "";
            years = 0;
            var parts = awardId.Split('_');
            if (parts.Length != 3 || parts[0] != "yos" || parts[1].Length == 0)
            {
                return false;
            }
            section = parts[1];
            return int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out years) && years > 0;
        }

        // "Scout 3 Years of Service"
        public static string AwardName(string section, int years) =>
            $"{SectionName(section)} {years} {(years == 1 ? "Year" : "Years")} of Service";

        public static string SectionName(string section) =>
            section.Length == 0 ? "" : char.ToUpperInvariant(section[0]) + section.Substring(1).ToLowerInvariant();

        // The day the member passes their nth year in the section
        public static DateTime Anniversary(DateTime start, int years) => start.Date.AddYears(years);

        // The anniversaries members have passed from the day tracking started, up to today. Anniversaries before that day are not listed: they were
        // handed over, or not, before Topo kept track. One row for each year, with the day it was reached.
        public static List<MissingAward> Reached(IEnumerable<ServiceMember> members, string section, DateTime from, DateTime today)
        {
            var reached = new List<MissingAward>();
            foreach (var member in members.Where(m => m.Start.HasValue))
            {
                for (var years = 1; years <= MostYears; years++)
                {
                    var day = Anniversary(member.Start!.Value, years);
                    if (day > today.Date)
                    {
                        break;
                    }
                    if (day < from.Date)
                    {
                        continue;
                    }
                    reached.Add(new MissingAward
                    {
                        MemberId = member.MemberId,
                        MemberName = member.MemberName,
                        SortName = member.SortName,
                        FirstName = member.FirstName,
                        LastName = member.LastName,
                        UnitId = member.UnitId,
                        Unit = member.Unit,
                        AwardId = AwardId(section, years),
                        AwardName = AwardName(section, years),
                        Earned = day
                    });
                }
            }
            return reached;
        }

        // Every year of a section is a level of one kind, so the highest is the badge to hand over (see AwardCheckRows.Pending)
        private static List<AwardTier> Tiers(IEnumerable<string> awardIds)
        {
            var tiers = new List<AwardTier>();
            foreach (var id in awardIds.Distinct())
            {
                if (TryParse(id, out var section, out var years))
                {
                    tiers.Add(new AwardTier { Id = id, Name = AwardName(section, years), Family = IdPrefix + section, Threshold = years });
                }
            }
            return tiers;
        }

        // What to hand over: the anniversaries reached that the store has no record of. records holds the store's records as member|award.
        public static List<AwardRow> Pending(IEnumerable<MissingAward> reached, ISet<string> records)
        {
            var list = reached.ToList();
            var recorded = records.Select(r => r.Split('|').LastOrDefault() ?? "");
            return AwardCheckRows.Pending(list, Tiers(list.Select(m => m.AwardId).Concat(recorded)), records);
        }

        // The store's member|award keys for years of service records
        public static HashSet<string> RecordKeys(BadgeStoreContents contents) =>
            contents.Records.Where(r => IsYearsOfService(r.AwardId)).Select(r => r.MemberId + "|" + r.AwardId).ToHashSet();

        // The day tracking started from, the latest the store says; null when it has not been started
        public static DateTime? StartedFrom(BadgeStoreContents contents, string groupId) =>
            contents.Records.Where(r => r.MemberId == groupId && r.AwardId == StartAwardId).Select(r => (DateTime?)r.Awarded).FirstOrDefault();

        // The years of service the store has a record of for these members, for the "has been presented" list. The day is the day it was
        // presented, or the anniversary when that was not recorded.
        public static List<RecordedAward> Recorded(BadgeStoreContents contents, IEnumerable<ServiceMember> members) =>
            contents.Records
                .Where(r => IsYearsOfService(r.AwardId))
                .Join(members, r => r.MemberId, m => m.MemberId, (r, m) =>
                {
                    TryParse(r.AwardId, out var section, out var years);
                    return new RecordedAward
                    {
                        MemberId = m.MemberId,
                        FirstName = m.FirstName,
                        LastName = m.LastName,
                        AwardId = r.AwardId,
                        AwardName = AwardName(section, years),
                        Day = (r.Presented ?? r.Awarded).Date
                    };
                }).ToList();
    }
}
