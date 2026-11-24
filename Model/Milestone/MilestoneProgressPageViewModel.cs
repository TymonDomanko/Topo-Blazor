namespace Topo.Model.Milestone
{
    public class MilestoneProgressPageViewModel
    {
        public Dictionary<string, string> Units { get; set; } = new Dictionary<string, string>();
        public string UnitId { get; set; } = string.Empty;
        public string UnitName { get; set; } = string.Empty;
        public string GroupName { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
        // 1, 2 or 3
        public int Stage { get; set; } = 1;
        // Pages only for the Scouts whose Participates and Leads for the milestone are in and who have not been awarded it
        public bool OnlyReady { get; set; } = true;
        public bool UseFullNames { get; set; }
    }
}
