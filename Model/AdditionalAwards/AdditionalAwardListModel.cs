namespace Topo.Model.AdditionalAwards
{
    public class AdditionalAwardListModel
    {
        public string MemberId { get; set; } = string.Empty;
        public string MemberName { get; set; } = string.Empty;
        public string MemberPatrol { get; set; } = string.Empty;
        public string AwardId { get; set; } = string.Empty;
        public string AwardName { get; set; } = string.Empty;
        public int NightsCamped { get; set; } = 0;
        public int KMsHiked { get; set; } = 0;
        public int AwardSortIndex { get; set; } = 0;
        public DateTime? AwardDate { get; set; }
        public DateTime? PresentedDate { get; set; }
        public int AwardUnits { get; set; } = 0;

        // The same row under another name, for when the names are shown in full or shortened
        public AdditionalAwardListModel WithName(string name)
        {
            var copy = (AdditionalAwardListModel)MemberwiseClone();
            copy.MemberName = name;
            return copy;
        }
    }
}
