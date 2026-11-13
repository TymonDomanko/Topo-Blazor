namespace Topo.Model
{
    // The reports are made by a function that runs in UTC. A date that still carries the user's timezone (DateTimeKind.Local) is
    // sent with an offset such as +10:00, and the function reads it back as a different moment, so a time early in the morning
    // shows as the day before. Dates for a report are sent as the user's own wall-clock time with no timezone, like the dates
    // Terrain writes (see also AwardDate).
    public static class ReportDate
    {
        // The moment as the user sees it, with no timezone. A UTC or local moment is converted to local time first; a date
        // with no kind is already a wall-clock date and is kept as it is.
        public static DateTime ForReport(DateTime moment) =>
            moment.Kind == DateTimeKind.Unspecified ? moment : DateTime.SpecifyKind(moment.ToLocalTime(), DateTimeKind.Unspecified);

        public static DateTime? ForReport(DateTime? moment) => moment.HasValue ? ForReport(moment.Value) : null;
    }
}
