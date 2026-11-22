using Topo.Model.BadgeStore;

namespace Topo.Model.AdditionalAwards
{
    public enum PlannedKind
    {
        // The badge for this level is still to be handed over
        ToBePresented,
        // A higher level has already been presented, so this one is taken as presented the same day
        AlreadyPresented,
        // A higher level is still to be presented and is the badge handed over, so this one is not
        NotHighest
    }

    // What to write to the store for one award level
    public class PlannedAward
    {
        public AwardRow Row { get; set; } = new AwardRow();
        public PlannedKind Kind { get; set; }
        public DateTime Awarded { get; set; }
        public DateTime? Presented { get; set; }
        // The higher level that covers this one: the one already presented (AlreadyPresented) or the one still to be presented (NotHighest)
        public AwardTier? CoveredBy { get; set; }
        // True for a level the store already holds, which is only rewritten because a higher level changes what it should say
        public bool IsUpdate { get; set; }
        public string SupersededBy => Kind == PlannedKind.NotHighest && CoveredBy != null ? CoveredBy.Id : "";
        public string Note => Kind switch
        {
            PlannedKind.AlreadyPresented => $"already presented: {CoveredBy?.Name} was presented on {Presented:dd/MM/yyyy}",
            PlannedKind.NotHighest => $"not the highest: {CoveredBy?.Name} is the one to present",
            _ => "to be presented"
        };
    }

    public static class AwardFlagPlan
    {
        // Only the highest badge of each kind is handed over, so what to write for a missing level depends on the higher levels:
        //  - If a higher level of the same kind has already been presented (a Terrain record, or the store says presented), the level
        //    is taken as presented too, on the same day as the nearest higher level that was.
        //  - Otherwise, if a higher level is still to be presented (flagged already, or missing and flagged now), the highest of those is
        //    the badge to hand over and this level is marked as not the highest (covered by it).
        //  - Otherwise this is the highest level still to be presented.
        // Levels the store already holds as to be presented are rewritten when a higher level changes what they should say.
        // terrainRecordDates holds Terrain's own award records (member|award, the day awarded), which count as presented.
        public static List<PlannedAward> Build(IEnumerable<AwardRow> missing, IEnumerable<AwardTier> tiers,
            IReadOnlyDictionary<string, DateTime> terrainRecordDates, BadgeStoreContents store, DateTime today)
        {
            var tierById = tiers.ToDictionary(t => t.Id);
            var planned = new List<PlannedAward>();
            foreach (var member in missing.Where(m => tierById.ContainsKey(m.AwardId)).GroupBy(m => m.MemberId))
            {
                foreach (var family in member.GroupBy(m => tierById[m.AwardId].Family))
                {
                    var memberId = member.Key;
                    var familyTiers = tiers.Where(t => t.Family == family.Key).OrderBy(t => t.Threshold).ToList();
                    var storeRecords = store.Records.Where(r => r.MemberId == memberId && tierById.ContainsKey(r.AwardId) && tierById[r.AwardId].Family == family.Key)
                        .ToDictionary(r => r.AwardId);

                    // Each level's presented day, if it has one: Terrain's record, or the store's presented date
                    var presentedOn = new Dictionary<string, DateTime>();
                    foreach (var tier in familyTiers)
                    {
                        if (terrainRecordDates.TryGetValue(memberId + "|" + tier.Id, out var terrainDay))
                            presentedOn[tier.Id] = terrainDay;
                        else if (storeRecords.TryGetValue(tier.Id, out var record) && record.Presented.HasValue)
                            presentedOn[tier.Id] = record.Presented.Value;
                    }

                    // The levels still to be presented: flagged in the store without a presented date, or missing and flagged now
                    var unpresented = new List<(AwardTier tier, AwardRow? missingRow, BadgeStoreRecord? existing)>();
                    foreach (var tier in familyTiers)
                    {
                        if (presentedOn.ContainsKey(tier.Id))
                            continue;
                        var row = family.FirstOrDefault(m => m.AwardId == tier.Id);
                        if (row != null)
                            unpresented.Add((tier, row, null));
                        else if (storeRecords.TryGetValue(tier.Id, out var existing))
                            unpresented.Add((tier, null, existing));
                    }

                    // A level with a presented level above it is taken as presented; the rest compete to be the one handed over
                    var competing = new List<(AwardTier tier, AwardRow? missingRow, BadgeStoreRecord? existing)>();
                    foreach (var item in unpresented)
                    {
                        var higherPresented = familyTiers.Where(t => t.Threshold > item.tier.Threshold && presentedOn.ContainsKey(t.Id)).OrderBy(t => t.Threshold).FirstOrDefault();
                        if (higherPresented != null)
                        {
                            var day = presentedOn[higherPresented.Id];
                            planned.Add(Plan(item.tier, item.missingRow, item.existing, PlannedKind.AlreadyPresented, day, day, higherPresented, memberId, today));
                        }
                        else
                            competing.Add(item);
                    }

                    var highest = competing.OrderByDescending(c => c.tier.Threshold).FirstOrDefault();
                    foreach (var item in competing)
                    {
                        if (item.tier.Id == highest.tier.Id)
                        {
                            // The highest is only written when it is new; one already flagged as it should be is left alone
                            if (item.missingRow != null)
                                planned.Add(Plan(item.tier, item.missingRow, null, PlannedKind.ToBePresented, EarnedOrToday(item.missingRow, today), null, null, memberId, today));
                            else if (item.existing != null && item.existing.SupersededBy.Length > 0)
                                planned.Add(Plan(item.tier, null, item.existing, PlannedKind.ToBePresented, item.existing.Awarded, null, null, memberId, today));
                        }
                        else if (item.existing == null || item.existing.SupersededBy != highest.tier.Id)
                            planned.Add(Plan(item.tier, item.missingRow, item.existing, PlannedKind.NotHighest, item.missingRow != null ? EarnedOrToday(item.missingRow, today) : item.existing!.Awarded, null, highest.tier, memberId, today));
                    }
                }
            }
            return planned
                .OrderBy(p => string.IsNullOrEmpty(p.Row.SortName) ? p.Row.MemberName : p.Row.SortName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(p => p.Row.MemberId, StringComparer.Ordinal).ThenBy(p => p.Row.AwardId, StringComparer.Ordinal)
                .ToList();
        }

        private static DateTime EarnedOrToday(AwardRow row, DateTime today) => (row.Earned ?? today).Date;

        // A missing level is written new. A level the store already holds is only rewritten if what it should say changed.
        private static PlannedAward Plan(AwardTier tier, AwardRow? missingRow, BadgeStoreRecord? existing, PlannedKind kind, DateTime awarded, DateTime? presented,
            AwardTier? coveredBy, string memberId, DateTime today)
        {
            var row = missingRow ?? new AwardRow
            {
                MemberId = memberId,
                AwardId = tier.Id,
                AwardName = tier.Name,
                Earned = existing?.Awarded,
                EventId = existing?.EventId ?? "",
                CommentId = existing?.CommentId ?? ""
            };
            return new PlannedAward { Row = row, Kind = kind, Awarded = awarded.Date, Presented = presented?.Date, CoveredBy = coveredBy, IsUpdate = missingRow == null };
        }
    }
}
