using Topo.Model.Program;

namespace Topo.Model.Cache
{
    // The event list is read from Terrain every time, and what each entry said is remembered, so that what is kept of an event can be dropped when
    // its entry changes. A change the list does not show (who came, say) is not seen this way: that waits for the kept read to go stale, or for
    // Refresh data.
    public static class EventListCheck
    {
        // Whether an event's entry is new or differs from when it was last listed; either way it is remembered as it is now. An event shared
        // between calendars may be listed for one invitee or another, so each is remembered on its own.
        public static async Task<bool> ChangedAsync(DataCache cache, string key, Event listed)
        {
            var entry = string.Join("\n", listed.title, listed.start_datetime.ToString("O"), listed.end_datetime.ToString("O"), listed.status,
                listed.challenge_area, listed.section, listed.invitee_type, listed.invitee_name, listed.group_id);
            var seen = (await cache.GetAsync<Dictionary<string, string>>(key))?.Value ?? new Dictionary<string, string>();
            if (seen.TryGetValue(listed.invitee_id, out var before) && before == entry)
            {
                return false;
            }
            seen[listed.invitee_id] = entry;
            await cache.PutAsync(key, seen, keep: true);
            return true;
        }
    }
}
