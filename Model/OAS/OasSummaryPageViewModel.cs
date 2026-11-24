namespace Topo.Model.OAS
{
    public class OasSummaryPageViewModel
    {
        public Dictionary<string, string> Units { get; set; } = new Dictionary<string, string>();
        public string UnitId { get; set; } = string.Empty;
        public string UnitName { get; set; } = string.Empty;
        public string GroupName { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
        public bool BreakByPatrol { get; set; }
        public bool UseFullNames { get; set; }
        // A column for each branch of each skill (Canoeing, Sailing, ...) instead of one for the whole skill
        public bool SplitBranches { get; set; }
    }
}
