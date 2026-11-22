namespace Topo.Model.Approvals
{
    // The Approvals list's filters: the date range, To be presented, Has been presented and Show Rejected. They apply to the Camper and
    // Walkabout Awards listed beside Terrain's own approvals just the same.
    public static class ApprovalsFilter
    {
        public static List<ApprovalsListModel> Apply(IEnumerable<ApprovalsListModel>? approvals, DateTime from, DateTime to, bool toBePresented, bool isPresented, bool showRejected)
        {
            var rows = (approvals ?? Enumerable.Empty<ApprovalsListModel>()).Where(a => a.submission_date >= from && a.submission_date <= to.AddDays(1)).ToList();
            if (toBePresented)
                rows = rows.Where(a => a.submission_type == "Award" && !a.presented_date.HasValue).ToList();
            if (isPresented)
                rows = rows.Where(a => a.presented_date.HasValue && (a.achievement_type == "additional_award" || a.presented_date != a.awarded_date)).ToList();
            if (!showRejected)
                rows = rows.Where(a => a.submission_outcome.ToLower() != "rejected").ToList();
            return rows;
        }
    }
}
