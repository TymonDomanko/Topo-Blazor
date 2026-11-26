using Topo.Model.Cache;
using Topo.Model.Logbook;

namespace TopoReportFunctionTest
{
    [TestClass]
    public class DataCacheTests
    {
        // Stands in for the browser's store
        private class Store : ICacheStore
        {
            public Dictionary<string, CacheEntry> Entries { get; } = new Dictionary<string, CacheEntry>();
            public int Reads;
            public int Sweeps;

            public Task<CacheEntry?> GetAsync(string key)
            {
                Reads++;
                return Task.FromResult(Entries.TryGetValue(key, out var entry) ? entry : null);
            }

            public Task PutAsync(CacheEntry entry)
            {
                Entries[entry.Key] = entry;
                return Task.CompletedTask;
            }

            public Task ClearAsync()
            {
                Entries.Clear();
                return Task.CompletedTask;
            }

            public Task RemoveFetchedBeforeAsync(long fetchedAt)
            {
                Sweeps++;
                foreach (var key in Entries.Where(e => e.Value.FetchedAt < fetchedAt).Select(e => e.Key).ToList())
                {
                    Entries.Remove(key);
                }
                return Task.CompletedTask;
            }
        }

        private static readonly DateTime Read = new DateTime(2026, 11, 20, 9, 0, 0, DateTimeKind.Utc);

        private static GetMemberLogbookMetricsResultModel Totals(float nights) => new GetMemberLogbookMetricsResultModel
        {
            results = new[] { new MetricsResult { name = "total_nights_camped", value = nights } }
        };

        private static async Task<(Store, DataCache)> Holding(float nights)
        {
            var store = new Store();
            var cache = new DataCache(store) { UtcNow = () => Read };
            await cache.PutAsync("a", Totals(nights));
            return (store, cache);
        }

        [TestMethod]
        public async Task WhatIsKeptComesBackWithWhenItWasRead()
        {
            var (store, _) = await Holding(12);
            // a new session reads it from the store
            var cache = new DataCache(store) { UtcNow = () => Read.AddHours(5) };

            var held = await cache.GetAsync<GetMemberLogbookMetricsResultModel>("a");

            Assert.IsNotNull(held);
            Assert.AreEqual(12, held.Value.results.Single().value);
            Assert.AreEqual(Read, held.FetchedUtc);
            Assert.IsFalse(held.IsStale);
        }

        [TestMethod]
        public async Task AfterADayItIsStillHandedOutButStale()
        {
            var (_, cache) = await Holding(12);

            cache.UtcNow = () => Read.AddDays(1).AddMinutes(-1);
            Assert.IsFalse((await cache.GetAsync<GetMemberLogbookMetricsResultModel>("a"))!.IsStale);
            cache.UtcNow = () => Read.AddDays(1);
            Assert.IsTrue((await cache.GetAsync<GetMemberLogbookMetricsResultModel>("a"))!.IsStale);
        }

        [TestMethod]
        public async Task AfterThirtyDaysItIsNotUsedAndIsDroppedFromTheStore()
        {
            var (store, cache) = await Holding(12);
            cache.UtcNow = () => Read.AddDays(30);
            Assert.IsNull(await cache.GetAsync<GetMemberLogbookMetricsResultModel>("a"));

            // a later session clears it out of the store, whatever page asks first
            var later = new DataCache(store) { UtcNow = () => Read.AddDays(31) };
            await later.GetAsync<GetMemberLogbookMetricsResultModel>("another");
            await later.GetAsync<GetMemberLogbookMetricsResultModel>("another");
            Assert.AreEqual(0, store.Entries.Count);
        }

        [TestMethod]
        public async Task TheStoreIsSweptOnceASessionAndEachEntryReadOnce()
        {
            var (store, _) = await Holding(12);
            var cache = new DataCache(store) { UtcNow = () => Read };
            var sweeps = store.Sweeps;
            var reads = store.Reads;

            await cache.GetAsync<GetMemberLogbookMetricsResultModel>("a");
            await cache.GetAsync<GetMemberLogbookMetricsResultModel>("a");

            Assert.AreEqual(sweeps + 1, store.Sweeps);
            Assert.AreEqual(reads + 1, store.Reads);
        }

        [TestMethod]
        public async Task AnEntryOfAnotherVersionOrThatCannotBeReadIsNotUsed()
        {
            var (store, _) = await Holding(12);
            store.Entries["a"].Version = DataCache.Version + 1;
            Assert.IsNull(await new DataCache(store) { UtcNow = () => Read }.GetAsync<GetMemberLogbookMetricsResultModel>("a"));

            store.Entries["a"].Version = DataCache.Version;
            store.Entries["a"].Json = "not json";
            Assert.IsNull(await new DataCache(store) { UtcNow = () => Read }.GetAsync<GetMemberLogbookMetricsResultModel>("a"));
        }

        [TestMethod]
        public async Task ClearingLeavesNothingBehindInMemoryOrTheStore()
        {
            var (store, cache) = await Holding(12);

            await cache.ClearAsync();

            Assert.AreEqual(0, store.Entries.Count);
            Assert.IsNull(await cache.GetAsync<GetMemberLogbookMetricsResultModel>("a"));
        }

        [TestMethod]
        public void WhenDataWasReadIsSaidAsTodayYesterdayOrDaysAgo()
        {
            var now = new DateTime(2026, 11, 26, 16, 30, 0);

            Assert.AreEqual("today 9:42am", DataAgeText.When(new DateTime(2026, 11, 26, 9, 42, 0), now));
            Assert.AreEqual("yesterday 4:10pm", DataAgeText.When(new DateTime(2026, 11, 25, 16, 10, 0), now));
            Assert.AreEqual("3 days ago", DataAgeText.When(new DateTime(2026, 11, 23, 23, 0, 0), now));
        }
    }
}
