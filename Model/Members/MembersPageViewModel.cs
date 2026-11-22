using System.ComponentModel.DataAnnotations;
using System.Xml.Linq;

namespace Topo.Model.Members
{
    // Which of the three reports the page is showing
    public enum MemberReportView
    {
        MemberList,
        PatrolList,
        PatrolSheets
    }

    public class MembersPageViewModel
    {
        public MemberReportView View { get; set; } = MemberReportView.MemberList;
        public MemberListSort Sort { get; set; } = MemberListSort.Alphabetical;
        [Display(Name = "Unit")]
        public string UnitId { get; set; } = string.Empty;
        public string UnitName { get; set; } = string.Empty;
        public Dictionary<string, string> Units { get; set; } = new Dictionary<string, string>();
        public List<MemberListModel>? Members { get; set; }
        [Display(Name = "Include Leaders")]
        public bool IncludeLeaders { get; set; } = false;
        public string GroupName { get; set; } = string.Empty;
        // Names on every list and sheet are shortened (first name and as much of the surname as tells people apart) unless this is ticked
        public bool UseFullNames { get; set; }
    }
}
