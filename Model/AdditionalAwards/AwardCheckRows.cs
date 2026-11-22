using Topo.Model.Approvals;

namespace Topo.Model.AdditionalAwards
{
    public enum AwardRowStatus
    {
        // The logbook says it was earned, Terrain has no record of it, and it is the badge to hand over
        ToBePresented,
        // Earned and not in Terrain, but a higher level (also not in Terrain) is the badge to hand over, so this one has nothing to present of its own
        NotHighest
    }

    // A Camper or Walkabout Award a Scout has earned that Terrain has no record of. Terrain counts the nights and km but never records the
    // award, so Topo works it out from the logbook. A record only exists in Terrain once it has been presented.
    public class AwardRow
    {
        public string MemberId { get; set; } = "";
        public string MemberName { get; set; } = "";
        // How the member sorts in a list ("Last First"), when it differs from the name shown
        public string SortName { get; set; } = "";
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        // The unit they were found in
        public string UnitId { get; set; } = "";
        public string Unit { get; set; } = "";
        public string AwardId { get; set; } = "";
        public string AwardName { get; set; } = "";
        public AwardRowStatus Status { get; set; }
        // The day it was earned, worked out from the logbook. Null when the logbook cannot say.
        public DateTime? Earned { get; set; }
        // The higher level that covers this one (NotHighest), by its award id and name
        public string SupersededBy { get; set; } = "";
        public string SupersededByName { get; set; } = "";
        public string Key => MemberId + "|" + AwardId;
    }

    public class MemberTotals
    {
        public string MemberId { get; set; } = "";
        public string Name { get; set; } = "";
        public double Nights { get; set; }
        public double Kilometres { get; set; }
    }

    // One Camper or Walkabout level a Scout has earned that Terrain has no record of, as the last logbook check found it. This is what is
    // kept between visits; what to hand over is worked out from it each time, against Terrain's records as they are then.
    public class MissingAward
    {
        public string MemberId { get; set; } = "";
        public string MemberName { get; set; } = "";
        public string SortName { get; set; } = "";
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public string UnitId { get; set; } = "";
        public string Unit { get; set; } = "";
        public string AwardId { get; set; } = "";
        public string AwardName { get; set; } = "";
        public DateTime? Earned { get; set; }
    }

    public static class AwardCheckRows
    {
        // The Camper and Walkabout levels each member's logbook totals reach that Terrain has no record of and that are still to be handed over.
        // A level below one Terrain does hold is left out: that higher badge was presented, so nothing is left to hand over for it.
        public static List<(MemberTotals member, AwardTier tier)> Missing(IEnumerable<MemberTotals> members, IEnumerable<AwardTier> tiers, ISet<string> terrainRecords)
        {
            var tierList = tiers.ToList();
            var missing = new List<(MemberTotals, AwardTier)>();
            foreach (var member in members)
                foreach (var tier in CamperWalkaboutRules.Reached(tierList, member.Nights, member.Kilometres))
                {
                    var highestHeld = tierList.Where(t => t.Family == tier.Family && terrainRecords.Contains(member.MemberId + "|" + t.Id)).Select(t => t.Threshold).DefaultIfEmpty(0).Max();
                    if (tier.Threshold > highestHeld && !terrainRecords.Contains(member.MemberId + "|" + tier.Id))
                        missing.Add((member, tier));
                }
            return missing;
        }

        // What to hand over. Only the highest badge of each kind is handed over, so for each member and kind:
        //  - a missing level with a level above it that Terrain holds is taken as done: that higher badge was presented, so nothing is left
        //    to hand over for it and it is not listed;
        //  - of the rest, the highest is the badge to hand over, and the others are listed as not the highest.
        // terrainRecords holds Terrain's own records (member|award).
        public static List<AwardRow> Pending(IEnumerable<MissingAward> missing, IEnumerable<AwardTier> tiers, ISet<string> terrainRecords)
        {
            var tierById = tiers.ToDictionary(t => t.Id);
            var rows = new List<AwardRow>();
            foreach (var member in missing.Where(m => tierById.ContainsKey(m.AwardId) && !terrainRecords.Contains(m.MemberId + "|" + m.AwardId)).GroupBy(m => m.MemberId))
                foreach (var family in member.GroupBy(m => tierById[m.AwardId].Family))
                {
                    var highestHeld = tiers.Where(t => t.Family == family.Key && terrainRecords.Contains(member.Key + "|" + t.Id)).Select(t => t.Threshold).DefaultIfEmpty(0).Max();
                    var open = family.Where(m => tierById[m.AwardId].Threshold > highestHeld).OrderBy(m => tierById[m.AwardId].Threshold).ToList();
                    if (open.Count == 0)
                        continue;
                    var highest = open[^1];
                    foreach (var m in open)
                        rows.Add(new AwardRow
                        {
                            MemberId = m.MemberId,
                            MemberName = m.MemberName,
                            SortName = m.SortName,
                            FirstName = m.FirstName,
                            LastName = m.LastName,
                            UnitId = m.UnitId,
                            Unit = m.Unit,
                            AwardId = m.AwardId,
                            AwardName = m.AwardName,
                            Earned = m.Earned,
                            Status = m == highest ? AwardRowStatus.ToBePresented : AwardRowStatus.NotHighest,
                            SupersededBy = m == highest ? "" : highest.AwardId,
                            SupersededByName = m == highest ? "" : highest.AwardName
                        });
                }
            return rows
                .OrderBy(r => string.IsNullOrEmpty(r.SortName) ? r.MemberName : r.SortName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(r => r.MemberId, StringComparer.Ordinal).ThenBy(r => r.AwardId, StringComparer.Ordinal)
                .ToList();
        }

        // The lower levels a presented one covers: presenting the badge to hand over records them too, so Terrain's record is complete
        public static List<AwardRow> CoveredBy(AwardRow row, IEnumerable<AwardRow> pending) =>
            pending.Where(r => r.MemberId == row.MemberId && r.Status == AwardRowStatus.NotHighest && r.SupersededBy == row.AwardId).ToList();

        // The awards to hand over, as rows for the Approvals list, which has filters of its own. They have no presented date (so "To be
        // presented" lists them). The "to award" column says which to hand over.
        public static List<ApprovalsListModel> ToApprovalRows(IEnumerable<AwardRow> rows) =>
            rows.Select(r =>
            {
                var name = r.MemberName.Trim();
                var split = name.LastIndexOf(' ');
                return new ApprovalsListModel
                {
                    member_id = r.MemberId,
                    member_first_name = r.FirstName.Length > 0 || r.LastName.Length > 0 ? r.FirstName : (split > 0 ? name.Substring(0, split) : name),
                    member_last_name = r.FirstName.Length > 0 || r.LastName.Length > 0 ? r.LastName : (split > 0 ? name.Substring(split + 1) : ""),
                    achievement_id = PendingApprovalItem.TopoIdPrefix + r.Key,
                    achievement_type = "additional_award",
                    achievement_name = r.AwardName,
                    submission_status = "Pending",
                    submission_outcome = "",
                    submission_type = "Award",
                    // Where the list's date range looks: the day it was earned
                    submission_date = (r.Earned ?? DateTime.Today).Date,
                    awarded_date = r.Earned,
                    presented_date = null,
                    to_award = r.Status == AwardRowStatus.ToBePresented
                        ? "Award"
                        : $"Not the highest - award {(r.SupersededByName.Length > 0 ? r.SupersededByName : r.SupersededBy)}"
                };
            }).ToList();
    }
}
