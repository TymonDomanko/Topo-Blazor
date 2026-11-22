namespace Topo.Model.AdditionalAwards
{
    public class AdditionalAwardListModel
    {
        public string MemberName { get; set; } = string.Empty;
        public string AwardId { get; set; } = string.Empty;
        public string AwardName { get; set; } = string.Empty;
        public int NightsCamped { get; set; } = 0;
        public int KMsHiked { get; set; } = 0;
        public int AwardSortIndex { get; set; } = 0;
        public DateTime? AwardDate { get; set; }
        public DateTime? PresentedDate { get; set; }
        public int AwardUnits { get; set; } = 0;
        // From the Topo records: a lower level covered by a higher one that is the badge to hand over, so nothing of its own to present
        public bool NotHighest { get; set; }
    }
}
