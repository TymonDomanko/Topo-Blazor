using Topo.Model.BadgeStore;

namespace Topo.Model.BadgeOrder
{
    // The day of each unit's last badge order, kept in the group's store (docs/badge-store.md) so every leader's copy of Topo counts from the same
    // day. It is an ordinary store record against the unit's id in place of a member's, whose "awarded" day is the day the order was placed. It is
    // kept for each unit, so an order for one section does not move the day the others count from, and an order for the whole group counts each
    // section from its own last order.
    public static class BadgeOrderRecords
    {
        public const string LastOrderAwardId = "order_last";

        // The day of the unit's last order the store says was placed (the latest record wins); null when none has been recorded
        public static DateTime? LastOrder(BadgeStoreContents contents, string unitId) =>
            contents.Records.Where(r => r.MemberId == unitId && r.AwardId == LastOrderAwardId).Select(r => (DateTime?)r.Awarded.Date).FirstOrDefault();

        // The last order of each of the units that has one
        public static Dictionary<string, DateTime> LastOrders(BadgeStoreContents contents, IEnumerable<string> unitIds)
        {
            var days = new Dictionary<string, DateTime>();
            foreach (var unitId in unitIds)
            {
                var day = LastOrder(contents, unitId);
                if (day.HasValue)
                {
                    days[unitId] = day.Value;
                }
            }
            return days;
        }
    }
}
