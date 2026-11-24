namespace Topo.Model.Approvals
{
    public class ApprovalsListModel
    {
        public string member_id { get; set; } = string.Empty;
        public string member_first_name { get; set; } = string.Empty;
        public string member_last_name { get; set; } = string.Empty;
        public string member_display_name => $"{member_first_name} {member_last_name}";
        public string achievement_id { get; set; } = string.Empty;
        public string achievement_type { get; set; } = string.Empty;
        public string achievement_name { get; set; } = string.Empty;
        public string submission_status { get; set; } = string.Empty;
        public string submission_outcome { get; set; } = string.Empty;
        public string submission_type { get; set; } = string.Empty;
        public DateTime submission_date { get; set; }
        public DateTime? awarded_date { get; set; }
        public DateTime? presented_date { get; set; }
        // For a Camper or Walkabout Award, which badge to hand over: "Award", or "Not the highest - award <the higher level>". Blank for Terrain's own approvals.
        public string to_award { get; set; } = string.Empty;
        // For an Outdoor Adventure Skill: which line it is (stream and branch) and its stage, so a lower stage can be told from the highest
        public string oas_line { get; set; } = string.Empty;
        public int oas_stage { get; set; }
    }
}
