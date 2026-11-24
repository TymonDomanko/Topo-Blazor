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

    // An additional award (Camper, Walkabout or another) Terrain holds a record of: it has been handed over, on that day
    public class RecordedAward
    {
        public string MemberId { get; set; } = "";
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public string AwardId { get; set; } = "";
        public string AwardName { get; set; } = "";
        public DateTime Day { get; set; }
    }

    public static class AwardCheckRows
    {
        // The id of a row for an award Terrain already holds, so it can be told from one still to hand over (topo:)
        public const string RecordedIdPrefix = "terrain:";

        // The additional awards Terrain holds, as presented rows for the Approvals list ("Has been presented"). The day is the day
        // Terrain has them awarded, which is the day they were handed over; the logbook day is not known for them, so there is no awarded date.
        public static List<ApprovalsListModel> ToRecordedRows(IEnumerable<RecordedAward> recorded) =>
            recorded.Select(r => new ApprovalsListModel
            {
                member_id = r.MemberId,
                member_first_name = r.FirstName,
                member_last_name = r.LastName,
                achievement_id = RecordedIdPrefix + r.MemberId + "|" + r.AwardId,
                achievement_type = "additional_award",
                achievement_name = r.AwardName,
                submission_status = "Finalised",
                submission_outcome = "Awarded",
                submission_type = "Award",
                submission_date = r.Day.Date,
                awarded_date = null,
                presented_date = r.Day.Date
            }).ToList();

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
                    {
                        missing.Add((member, tier));
                    }
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
                    {
                        continue;
                    }
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

        // The levels below one Terrain holds that Terrain has no record of. A higher badge being awarded means the lower ones were too, so each is
        // recorded as awarded on the day the logbook says it was earned (earned: member|award -> the day), but never later than the nearest higher
        // level Terrain holds; with no earned day it takes that higher level's day. terrainRecordDates holds Terrain's own records
        // (member|award -> the day awarded); only the members asked for are looked at.
        public static List<(string memberId, AwardTier tier, DateTime day)> LowerLevels(IEnumerable<AwardTier> tiers, IReadOnlyDictionary<string, DateTime> terrainRecordDates, IEnumerable<string> memberIds,
            IReadOnlyDictionary<string, DateTime>? earned = null)
        {
            var tierList = tiers.ToList();
            var lower = new List<(string, AwardTier, DateTime)>();
            foreach (var memberId in memberIds.Distinct())
                foreach (var family in tierList.GroupBy(t => t.Family))
                {
                    var ordered = family.OrderBy(t => t.Threshold).ToList();
                    for (var i = 0; i < ordered.Count; i++)
                    {
                        if (terrainRecordDates.ContainsKey(memberId + "|" + ordered[i].Id))
                        {
                            continue;
                        }
                        // the nearest level above that Terrain holds gives the day
                        var above = ordered.Skip(i + 1).FirstOrDefault(t => terrainRecordDates.ContainsKey(memberId + "|" + t.Id));
                        if (above == null)
                        {
                            continue;
                        }
                        var day = terrainRecordDates[memberId + "|" + above.Id];
                        if (earned != null && earned.TryGetValue(memberId + "|" + ordered[i].Id, out var earnedDay) && earnedDay.Date < day)
                        {
                            day = earnedDay.Date;
                        }
                        lower.Add((memberId, ordered[i], day));
                    }
                }
            return lower;
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
