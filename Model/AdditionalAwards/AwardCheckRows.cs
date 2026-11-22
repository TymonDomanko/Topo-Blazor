using Topo.Model.Approvals;
using Topo.Model.BadgeStore;

namespace Topo.Model.AdditionalAwards
{
    public enum AwardRowStatus
    {
        // Earned by the logbook, but nothing records it yet
        NotFlagged,
        // The store says it was earned and has not been presented
        ToBePresented,
        // The store says a higher level is the one to hand over, so this one has nothing to present of its own
        NotHighest,
        // The store says it was presented
        Presented,
        // Terrain already has an award record for it, which Topo treats as done
        InTerrain
    }

    public class AwardRow
    {
        public string MemberId { get; set; } = "";
        public string MemberName { get; set; } = "";
        // How the member sorts in a list ("Last First"), when it differs from the name shown
        public string SortName { get; set; } = "";
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        // The unit they were found in, when more than one unit is checked
        public string Unit { get; set; } = "";
        public string AwardId { get; set; } = "";
        public string AwardName { get; set; } = "";
        public AwardRowStatus Status { get; set; }
        // The day it was earned: from the store's record, or worked out from the logbook. Null when the logbook cannot say.
        public DateTime? Earned { get; set; }
        public DateTime? Presented { get; set; }
        // The day the store record was written, which is when an award that is not yet presented joined the list of those to present
        public DateTime? FlaggedOn { get; set; }
        // The higher level that covers this one (NotHighest), by its award id
        public string SupersededBy { get; set; } = "";
        // The name of that higher level, for a screen to show
        public string SupersededByName { get; set; } = "";
        // For a record in the store
        public string EventId { get; set; } = "";
        public string CommentId { get; set; } = "";
        public string Key => MemberId + "|" + AwardId;
    }

    public class MemberTotals
    {
        public string MemberId { get; set; } = "";
        public string Name { get; set; } = "";
        public double Nights { get; set; }
        public double Kilometres { get; set; }
    }

    public static class AwardCheckRows
    {
        // The awards the store says were earned for these members, split by whether they have been presented. Names come from the
        // lists passed in; a member not in the list (another unit's) is left out.
        public static List<AwardRow> FromStore(BadgeStoreContents contents, IReadOnlyDictionary<string, string> memberNames, IReadOnlyDictionary<string, string> awardNames,
            IReadOnlyDictionary<string, string>? sortNames = null) =>
            contents.Records
                .Where(r => memberNames.ContainsKey(r.MemberId))
                .Select(r => new AwardRow
                {
                    MemberId = r.MemberId,
                    MemberName = memberNames[r.MemberId],
                    SortName = sortNames != null && sortNames.TryGetValue(r.MemberId, out var sortName) ? sortName : memberNames[r.MemberId],
                    AwardId = r.AwardId,
                    AwardName = awardNames.TryGetValue(r.AwardId, out var name) ? name : r.AwardId,
                    Status = r.Presented.HasValue ? AwardRowStatus.Presented : r.SupersededBy.Length > 0 ? AwardRowStatus.NotHighest : AwardRowStatus.ToBePresented,
                    SupersededBy = r.SupersededBy,
                    Earned = r.Awarded,
                    Presented = r.Presented,
                    FlaggedOn = (r.RecordedAt.Kind == DateTimeKind.Utc ? r.RecordedAt.ToLocalTime() : r.RecordedAt).Date,
                    EventId = r.EventId,
                    CommentId = r.CommentId
                })
                .OrderBy(r => r.SortName, StringComparer.CurrentCultureIgnoreCase).ThenBy(r => r.AwardId, StringComparer.Ordinal)
                .ToList();

        // The awards the store holds, as rows for the Approvals list, which has filters of its own. A level still to be presented has no presented
        // date (so "To be presented" lists it); one the store says is presented has one. The "to award" column says which to hand over.
        public static List<ApprovalsListModel> ToApprovalRows(IEnumerable<AwardRow> rows) =>
            rows.Select(r =>
            {
                var presented = r.Status == AwardRowStatus.Presented;
                var name = r.MemberName.Trim();
                var split = name.LastIndexOf(' ');
                return new ApprovalsListModel
                {
                    member_id = r.MemberId,
                    member_first_name = r.FirstName.Length > 0 || r.LastName.Length > 0 ? r.FirstName : (split > 0 ? name.Substring(0, split) : name),
                    member_last_name = r.FirstName.Length > 0 || r.LastName.Length > 0 ? r.LastName : (split > 0 ? name.Substring(split + 1) : ""),
                    achievement_id = PendingApprovalItem.StoreIdPrefix + r.Key,
                    achievement_type = "additional_award",
                    achievement_name = r.AwardName,
                    submission_status = presented ? "Awarded" : "Pending",
                    submission_outcome = presented ? "Awarded" : "",
                    submission_type = "Award",
                    // Where the list's date range looks: the day it was presented, otherwise the day it was flagged as to be presented
                    // (like a pending award in Terrain, it is dated from when it joined the list, not from when it was earned)
                    submission_date = (r.Presented ?? r.FlaggedOn ?? r.Earned ?? DateTime.Today).Date,
                    awarded_date = r.Earned,
                    presented_date = r.Presented,
                    to_award = r.Status switch
                    {
                        AwardRowStatus.ToBePresented => "Award",
                        AwardRowStatus.NotHighest => $"Not the highest - award {(r.SupersededByName.Length > 0 ? r.SupersededByName : r.SupersededBy)}",
                        _ => ""
                    }
                };
            }).ToList();

        // For each member, the Camper and Walkabout tiers their logbook totals reach that nothing records yet: not in Terrain's own
        // additional award records and not in the store. These are what to flag as to be presented.
        public static List<(MemberTotals member, AwardTier tier)> Missing(IEnumerable<MemberTotals> members, IEnumerable<AwardTier> tiers,
            ISet<string> terrainRecords, BadgeStoreContents store)
        {
            var inStore = store.Records.Select(r => r.MemberId + "|" + r.AwardId).ToHashSet();
            var missing = new List<(MemberTotals, AwardTier)>();
            foreach (var member in members)
                foreach (var tier in CamperWalkaboutRules.Reached(tiers, member.Nights, member.Kilometres))
                {
                    var key = member.MemberId + "|" + tier.Id;
                    if (!terrainRecords.Contains(key) && !inStore.Contains(key))
                        missing.Add((member, tier));
                }
            return missing;
        }

        // Rows for the check page: what is missing (to flag), with the day each was earned when the logbook can say
        public static List<AwardRow> ForFlagging(IEnumerable<(MemberTotals member, AwardTier tier)> missing, IReadOnlyDictionary<string, Dictionary<string, DateTime?>> earnedDates) =>
            missing.Select(m => new AwardRow
            {
                MemberId = m.member.MemberId,
                MemberName = m.member.Name,
                AwardId = m.tier.Id,
                AwardName = m.tier.Name,
                Status = AwardRowStatus.NotFlagged,
                Earned = earnedDates.TryGetValue(m.member.MemberId, out var dates) && dates.TryGetValue(m.tier.Id, out var day) ? day : null
            })
            .OrderBy(r => r.MemberName, StringComparer.CurrentCultureIgnoreCase).ThenBy(r => r.AwardId, StringComparer.Ordinal)
            .ToList();
    }
}

namespace Topo.Model.AdditionalAwards
{
    public static class AwardRecordSummaries
    {
        // One member's Camper and Walkabout Awards from the Topo records, lowest level first
        public static List<Model.Progress.AwardRecordSummary> ForMember(IEnumerable<AwardRow> rows, string memberId) =>
            rows.Where(r => r.MemberId == memberId)
                .OrderBy(r => r.AwardId, StringComparer.Ordinal)
                .Select(r => new Model.Progress.AwardRecordSummary
                {
                    Name = r.AwardName,
                    Awarded = r.Earned,
                    Presented = r.Presented,
                    Status = r.Status switch
                    {
                        AwardRowStatus.Presented => "Presented",
                        AwardRowStatus.NotHighest => $"Not the highest - award {(r.SupersededByName.Length > 0 ? r.SupersededByName : r.SupersededBy)}",
                        _ => "To be presented"
                    }
                })
                .ToList();
    }
}
