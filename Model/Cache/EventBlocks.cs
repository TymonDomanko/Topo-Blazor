using Topo.Model.Program;

namespace Topo.Model.Cache
{
    // Event lists are read and kept half a year at a time (January to June, July to December), so pages whose dates overlap share what is held,
    // looking further back reads only the half years not held, and a half year long past is kept until it is read again on request.
    public static class EventBlocks
    {
        private const int Months = 6;

        // The half years that cover the dates, each from its first moment to its last
        public static IEnumerable<(DateTime From, DateTime To)> Between(DateTime from, DateTime to)
        {
            if (to < from)
            {
                yield break;
            }
            for (var start = new DateTime(from.Year, from.Month <= Months ? 1 : 1 + Months, 1); start <= to; start = start.AddMonths(Months))
            {
                yield return (start, start.AddMonths(Months).AddSeconds(-1));
            }
        }

        // Whether a half year ending then is long enough past to be kept (the same age as an old event)
        public static bool IsOld(DateTime blockEnd, DateTime now) => blockEnd < now.AddMonths(-OldEvents.Months);

        // The events of the half years that are on at some time between the dates, each once, in order of starting
        public static Event[] Merge(IEnumerable<Event> events, DateTime from, DateTime to) =>
            events.Where(e => e.start_datetime <= to && (e.end_datetime >= from || e.start_datetime >= from))
                // an event that runs over the end of a half year is in both
                .DistinctBy(e => e.id)
                .OrderBy(e => e.start_datetime)
                .ToArray();
    }
}
