namespace Topo.Model.Approvals
{
    // The Approvals list's filters: the date range, To be presented, Has been presented and Show Rejected. To be presented, Has been presented
    // and Show Rejected apply to the Camper and Walkabout Awards to hand over, listed beside Terrain's own approvals, just the same. The date
    // range does not hide a badge still waiting to be handed over, however long ago it was earned or approved: the Camper and Walkabout Awards
    // to hand over are never narrowed by it, and with To be presented ticked it is not applied to anything, since every row left is waiting.
    public static class ApprovalsFilter
    {
        public static List<ApprovalsListModel> Apply(IEnumerable<ApprovalsListModel>? approvals, DateTime from, DateTime to, bool toBePresented, bool isPresented, bool showRejected)
        {
            var rows = (approvals ?? Enumerable.Empty<ApprovalsListModel>())
                .Where(a => toBePresented || IsToHandOver(a) || (a.submission_date >= from && a.submission_date <= to.AddDays(1))).ToList();
            if (toBePresented)
            {
                rows = rows.Where(a => a.submission_type == "Award" && !a.presented_date.HasValue).ToList();
            }
            if (isPresented)
            {
                rows = rows.Where(a => a.presented_date.HasValue && a.presented_date != a.awarded_date).ToList();
            }
            if (!showRejected)
            {
                rows = rows.Where(a => a.submission_outcome.ToLower() != "rejected").ToList();
            }
            return rows;
        }

        // A Camper or Walkabout Award Topo found in the logbook, not an approval Terrain holds
        private static bool IsToHandOver(ApprovalsListModel a) =>
            a.achievement_id != null && a.achievement_id.StartsWith(PendingApprovalItem.TopoIdPrefix, StringComparison.Ordinal);
    }
}
