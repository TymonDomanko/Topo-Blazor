using Topo.Model.BadgeStore;

namespace Topo.Model.BadgeOrder
{
    // The day of the group's last badge order, kept in the group's store (docs/badge-store.md) so every leader's copy of Topo counts from the same
    // day. It is an ordinary store record against the group's own id in place of a member's, whose "awarded" day is the day the order was placed.
    public static class BadgeOrderRecords
    {
        public const string LastOrderAwardId = "order_last";

        // The day of the last order the store says was placed (the latest record wins); null when none has been recorded
        public static DateTime? LastOrder(BadgeStoreContents contents, string groupId) =>
            contents.Records.Where(r => r.MemberId == groupId && r.AwardId == LastOrderAwardId).Select(r => (DateTime?)r.Awarded.Date).FirstOrDefault();
    }
}
