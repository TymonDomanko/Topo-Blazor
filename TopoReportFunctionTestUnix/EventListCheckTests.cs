using Topo.Model.Cache;
using Topo.Model.Program;

namespace TopoReportFunctionTest
{
    [TestClass]
    public class EventListCheckTests
    {
        private class Store : ICacheStore
        {
            private readonly Dictionary<string, CacheEntry> _entries = new Dictionary<string, CacheEntry>();
            public Task<CacheEntry?> GetAsync(string key) => Task.FromResult(_entries.TryGetValue(key, out var entry) ? entry : null);
            public Task PutAsync(CacheEntry entry)
            {
                _entries[entry.Key] = entry;
                return Task.CompletedTask;
            }
            public Task<IReadOnlyList<string>> KeysAsync() => Task.FromResult<IReadOnlyList<string>>(_entries.Keys.ToList());
            public Task RemoveAsync(IReadOnlyList<string> keys) => Task.CompletedTask;
            public Task ClearAsync() => Task.CompletedTask;
            public Task RemoveFetchedBeforeAsync(long fetchedAt) => Task.CompletedTask;
        }

        private static Event Hike(string invitee = "scouts") => new Event
        {
            id = "e1", title = "Hike", status = "planned", invitee_id = invitee, invitee_type = "unit",
            start_datetime = new DateTime(2026, 11, 20, 9, 0, 0), end_datetime = new DateTime(2026, 11, 20, 15, 0, 0)
        };

        [TestMethod]
        public async Task AnEventIsNewTheFirstTimeAndUnchangedAfter()
        {
            var cache = new DataCache(new Store());

            Assert.IsTrue(await EventListCheck.ChangedAsync(cache, "k", Hike()));
            Assert.IsFalse(await EventListCheck.ChangedAsync(cache, "k", Hike()));
        }

        [TestMethod]
        public async Task ARenamedMovedOrConcludedEventHasChanged()
        {
            var changes = new Action<Event>[]
            {
                e => e.title = "Night hike", e => e.start_datetime = e.start_datetime.AddDays(7), e => e.end_datetime = e.end_datetime.AddHours(1),
                e => e.status = "concluded", e => e.challenge_area = "outdoors"
            };
            foreach (var change in changes)
            {
                var cache = new DataCache(new Store());
                await EventListCheck.ChangedAsync(cache, "k", Hike());
                var changed = Hike();
                change(changed);

                Assert.IsTrue(await EventListCheck.ChangedAsync(cache, "k", changed));
                Assert.IsFalse(await EventListCheck.ChangedAsync(cache, "k", changed));
            }
        }

        // listed once for the Scouts and once for the Cubs, it is not taken to have changed back and forth
        [TestMethod]
        public async Task ASharedEventListedForAnotherCalendarSettlesAfterOnce()
        {
            var cache = new DataCache(new Store());
            await EventListCheck.ChangedAsync(cache, "k", Hike("scouts"));

            Assert.IsTrue(await EventListCheck.ChangedAsync(cache, "k", Hike("cubs")));
            Assert.IsFalse(await EventListCheck.ChangedAsync(cache, "k", Hike("scouts")));
            Assert.IsFalse(await EventListCheck.ChangedAsync(cache, "k", Hike("cubs")));
        }
    }
}
