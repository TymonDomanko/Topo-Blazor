using Topo.Model.Cache;

namespace TopoReportFunctionTest
{
    [TestClass]
    public class CachedPageTests
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
            public Task ClearAsync() => Task.CompletedTask;
            public Task RemoveFetchedBeforeAsync(long fetchedAt) => Task.CompletedTask;
        }

        private static readonly DateTime Start = new DateTime(2026, 11, 20, 9, 0, 0, DateTimeKind.Utc);

        // A cache and a stand-in for Terrain that answers with a new figure each time it is asked
        private class Terrain
        {
            public DateTime Now = Start;
            public int Calls;
            public bool Answers = true;
            public DataCache Cache { get; }

            public Terrain()
            {
                Cache = new DataCache(new Store()) { UtcNow = () => Now };
            }

            public Task<string> Read() => Cache.ReadThroughAsync("members", () =>
            {
                Calls++;
                return Task.FromResult(Answers ? (true, $"answer {Calls}") : (false, "no"));
            });
        }

        [TestMethod]
        public async Task OutsideAScopeEveryReadGoesToTerrainAndNothingIsKept()
        {
            var terrain = new Terrain();

            Assert.AreEqual("answer 1", await terrain.Read());
            Assert.AreEqual("answer 2", await terrain.Read());
            Assert.IsNull(await terrain.Cache.GetAsync<string>("members"));
        }

        [TestMethod]
        public async Task InAScopeAHeldReadServesAndItsAgeIsNoted()
        {
            var terrain = new Terrain();
            using (CacheScope.Begin(CacheMode.Held))
            {
                await terrain.Read();
            }
            terrain.Now = Start.AddDays(2);

            using var scope = CacheScope.Begin(CacheMode.Held);
            Assert.AreEqual("answer 1", await terrain.Read());

            Assert.AreEqual(1, terrain.Calls);
            Assert.AreEqual(Start, scope.OldestUtc);
            Assert.IsTrue(scope.UsedStale);
        }

        [TestMethod]
        public async Task AFreshScopeReadsAgainOnlyWhatIsStaleAndRereadReadsAlways()
        {
            var terrain = new Terrain();
            using (CacheScope.Begin(CacheMode.Fresh))
            {
                await terrain.Read();
                await terrain.Read();
                Assert.AreEqual(1, terrain.Calls);
                terrain.Now = Start.AddDays(1);
                Assert.AreEqual("answer 2", await terrain.Read());
            }

            using var scope = CacheScope.Begin(CacheMode.Reread);
            Assert.AreEqual("answer 3", await terrain.Read());
            Assert.IsFalse(scope.UsedStale);
        }

        [TestMethod]
        public async Task WhenTerrainDoesNotAnswerTheOlderCopyIsUsed()
        {
            var terrain = new Terrain();
            using (CacheScope.Begin(CacheMode.Reread))
            {
                await terrain.Read();
                terrain.Answers = false;
                Assert.AreEqual("answer 1", await terrain.Read());
            }
            // and a failed answer is not kept
            Assert.AreEqual("answer 1", (await terrain.Cache.GetAsync<string>("members"))!.Value);
        }

        [TestMethod]
        public void LeavingAScopeBringsBackTheOneAround()
        {
            Assert.IsNull(CacheScope.Current);
            using (var outer = CacheScope.Begin(CacheMode.Held))
            {
                using (CacheScope.Begin(CacheMode.Reread))
                {
                    Assert.AreEqual(CacheMode.Reread, CacheScope.Current!.Mode);
                }
                Assert.AreSame(outer, CacheScope.Current);
            }
            Assert.IsNull(CacheScope.Current);
        }

        [TestMethod]
        public async Task APageShowsWhatIsHeldThenTheNewDataWhenWhatItHadWasStale()
        {
            var terrain = new Terrain();
            var shown = new List<string>();
            var updatingWhenShown = new List<bool>();
            var page = new PageLoad();
            Func<Action<string>, CancellationToken, Task<string>> read = (progress, token) => terrain.Read();
            Action<string> show = data =>
            {
                shown.Add(data);
                updatingWhenShown.Add(page.Updating);
            };

            await page.LoadAsync(read, show);
            await page.LoadAsync(read, show); // in date: not read again
            CollectionAssert.AreEqual(new[] { "answer 1", "answer 1" }, shown);

            shown.Clear();
            updatingWhenShown.Clear();
            terrain.Now = Start.AddDays(3);
            await page.LoadAsync(read, show);

            CollectionAssert.AreEqual(new[] { "answer 1", "answer 2" }, shown);
            CollectionAssert.AreEqual(new[] { false, true }, updatingWhenShown);
            Assert.IsFalse(page.Updating);
            Assert.AreEqual(Start.AddDays(3), page.ReadUtc);
        }

        [TestMethod]
        public async Task RefreshReadsAgainEvenWhenInDate()
        {
            var terrain = new Terrain();
            var shown = "";
            var page = new PageLoad();
            await page.LoadAsync((progress, token) => terrain.Read(), data => shown = data);

            await page.RefreshAsync((progress, token) => terrain.Read(), data => shown = data);

            Assert.AreEqual("answer 2", shown);
        }

        [TestMethod]
        public async Task ALoadThatALaterOneOvertakesChangesNothing()
        {
            var page = new PageLoad();
            var slow = new TaskCompletionSource<string>();
            var shown = new List<string>();

            var first = page.LoadAsync((progress, token) => slow.Task, shown.Add);
            await page.LoadAsync((progress, token) => Task.FromResult("second unit"), shown.Add);
            slow.SetResult("first unit");
            await first;

            CollectionAssert.AreEqual(new[] { "second unit" }, shown);
            Assert.IsFalse(page.Loading);
        }

        [TestMethod]
        public async Task AReadThatFailsIsReportedAndLeavesThePageIdle()
        {
            var page = new PageLoad();

            await page.LoadAsync<string>((progress, token) => throw new InvalidOperationException(), data => { });

            Assert.IsTrue(page.Failed);
            Assert.IsFalse(page.Loading);
            Assert.IsFalse(page.Updating);
        }
    }
}
