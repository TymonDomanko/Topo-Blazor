namespace Topo.Model.UnitReport
{
    public class UnitReportPageViewModel
    {
        public string GroupName { get; set; } = string.Empty;
        public Dictionary<string, string> Units { get; set; } = new Dictionary<string, string>();
        public string UnitId { get; set; } = string.Empty;
        public Dictionary<string, string> Calendars { get; set; } = new Dictionary<string, string>();
        public HashSet<string> SelectedCalendars { get; set; } = new HashSet<string>();
        // Search from the earliest date the report looks at, instead of the range
        public bool AllTime { get; set; }
        // The current quarter to start with, as the Section Report's "This quarter"
        public DateTime FromDate { get; set; } = new DateTime(DateTime.Today.Year, (DateTime.Today.Month - 1) / 3 * 3 + 1, 1);
        public DateTime ToDate { get; set; } = new DateTime(DateTime.Today.Year, (DateTime.Today.Month - 1) / 3 * 3 + 1, 1).AddMonths(3).AddDays(-1);
        public string ErrorMessage { get; set; } = string.Empty;
        public UnitReportModel? Report { get; set; }
    }
}
