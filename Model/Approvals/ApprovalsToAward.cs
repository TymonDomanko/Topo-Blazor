namespace Topo.Model.Approvals
{
    // Only the highest badge of a kind is handed over, so when a Scout has several stages of the same Outdoor Adventure Skill still to be
    // presented, the "To award" column says which one to award and that the others are not the highest.
    public static class ApprovalsToAward
    {
        public static void ApplyOas(IEnumerable<ApprovalsListModel> approvals)
        {
            var oas = approvals
                .Where(a => a.achievement_type == "outdoor_adventure_skill" && a.submission_type == "Award" && a.oas_line.Length > 0
                            && !string.Equals(a.submission_outcome, "rejected", StringComparison.OrdinalIgnoreCase))
                .GroupBy(a => (a.member_id, a.oas_line));
            foreach (var line in oas)
            {
                var toPresent = line.Where(a => !a.presented_date.HasValue).OrderBy(a => a.oas_stage).ToList();
                if (toPresent.Count == 0)
                    continue;
                var highest = toPresent[^1];
                var presentedAbove = line.Where(a => a.presented_date.HasValue && a.oas_stage > highest.oas_stage).OrderByDescending(a => a.oas_stage).FirstOrDefault();
                foreach (var row in toPresent)
                {
                    if (presentedAbove != null)
                        row.to_award = $"Not the highest - {presentedAbove.achievement_name} was already presented";
                    else if (row == highest)
                        row.to_award = "Award";
                    else
                        row.to_award = $"Not the highest - award {highest.achievement_name}";
                }
            }
        }
    }
}
