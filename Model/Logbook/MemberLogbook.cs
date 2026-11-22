using System.Globalization;

namespace Topo.Model.Logbook
{
    // One Scout's logbook as the Logbook page shows it: Terrain's totals and every entry. The entries are the same rows the Logbook
    // report is made from, so what is on the page is what the report holds.
    public class MemberLogbook
    {
        public string MemberId { get; set; } = string.Empty;
        public string MemberName { get; set; } = string.Empty;
        public float TotalKilometres { get; set; }
        public float TotalNights { get; set; }
        public List<MemberLogbookReportViewModel> Entries { get; set; } = new List<MemberLogbookReportViewModel>();

        public int EntryCount => Entries.Count;

        // The day of the most recent entry, or null when there are none
        public DateTime? LastActivity => Entries.Count == 0 ? null : Entries.Max(e => e.ActivityDate);

        // Most recent first, as a leader looks for what a Scout did lately; entries on the same day by name
        public List<MemberLogbookReportViewModel> NewestFirst() =>
            Entries.OrderByDescending(e => e.ActivityDate).ThenBy(e => e.ActivityName, StringComparer.CurrentCultureIgnoreCase).ToList();

        // "12.5 km", "30 km" - a whole number has no decimal point
        public static string Kilometres(float km) => $"{km.ToString("0.#", CultureInfo.InvariantCulture)} km";

        // "1 night", "12 nights"
        public static string Nights(float nights) => $"{nights.ToString("0.#", CultureInfo.InvariantCulture)} {(nights == 1 ? "night" : "nights")}";

        public static string EntryText(int count) => $"{count} {(count == 1 ? "entry" : "entries")}";

        // The report rows for these Scouts, in the order given: one for each entry. A Scout with no entries has no row, as in the report.
        public static List<MemberLogbookReportViewModel> ReportRows(IEnumerable<MemberLogbook> logbooks) =>
            logbooks.SelectMany(l => l.Entries).ToList();
    }
}
