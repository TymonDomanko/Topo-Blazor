namespace Topo.Model.Attendance
{
    public class AttendancePageViewModel
    {
        public string GroupName { get; set; } = string.Empty;
        public Dictionary<string, string> Units { get; set; } = new Dictionary<string, string>();
        public string UnitId { get; set; } = string.Empty;
        public Dictionary<string, string> Calendars { get; set; } = new Dictionary<string, string>();
        public HashSet<string> SelectedCalendars { get; set; } = new HashSet<string>();
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public bool BreakByPatrol { get; set; }
        public bool UseFullNames { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
    }
}
