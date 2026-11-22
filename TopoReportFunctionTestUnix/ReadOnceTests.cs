using Topo.Model;

namespace TopoReportFunctionTest
{
    [TestClass]
    public class ReadOnceTests
    {
        private static readonly string[] Ids = { "a", "b", "c", "d" };

        private static Func<string, Task<string?>> Reader(List<string> asked) => async id =>
        {
            lock (asked)
                asked.Add(id);
            await Task.Delay(1);
            return id.ToUpper();
        };

        [TestMethod]
        public async Task WithNothingKnownEverythingIsReadInTheOrderGiven()
        {
            var asked = new List<string>();
            var known = new Dictionary<string, string>();
            var result = await ReadOnce.SelectAsync(Ids, i => i, known, Reader(asked));
            CollectionAssert.AreEqual(new[] { "A", "B", "C", "D" }, result.ToArray());
            Assert.AreEqual(4, asked.Count);
            Assert.AreEqual(4, known.Count, "what was read is remembered");
        }

        [TestMethod]
        public async Task ASecondReadOfOverlappingItemsReadsOnlyTheNewOnesAndKeepsTheOrder()
        {
            var known = new Dictionary<string, string>();
            await ReadOnce.SelectAsync(new[] { "b", "c" }, i => i, known, Reader(new List<string>()));

            var asked = new List<string>();
            var result = await ReadOnce.SelectAsync(Ids, i => i, known, Reader(asked));

            CollectionAssert.AreEqual(new[] { "A", "B", "C", "D" }, result.ToArray());
            CollectionAssert.AreEquivalent(new[] { "a", "d" }, asked, "b and c were not asked for again");
        }

        [TestMethod]
        public async Task WithNoMemoryEveryItemIsReadEachTime()
        {
            var asked = new List<string>();
            await ReadOnce.SelectAsync(Ids, i => i, null, Reader(asked));
            await ReadOnce.SelectAsync(Ids, i => i, null, Reader(asked));
            Assert.AreEqual(8, asked.Count);
        }

        [TestMethod]
        public async Task AnAnswerThatComesBackNullIsLeftOutOfTheMemoryAndAskedForAgain()
        {
            var known = new Dictionary<string, string>();
            var result = await ReadOnce.SelectAsync(new[] { "a", "x" }, i => i, known, id => Task.FromResult<string?>(id == "x" ? null : "A"));
            CollectionAssert.AreEqual(new string?[] { "A", null }, result.ToArray());
            CollectionAssert.AreEqual(new[] { "a" }, known.Keys.ToArray());

            var asked = new List<string>();
            await ReadOnce.SelectAsync(new[] { "a", "x" }, i => i, known, Reader(asked));
            CollectionAssert.AreEqual(new[] { "x" }, asked);
        }

        [TestMethod]
        public async Task ProgressCountsOnlyTheItemsThatHadToBeRead()
        {
            var known = new Dictionary<string, string> { ["a"] = "A", ["b"] = "B" };
            var totals = new List<int>();
            var dones = new List<int>();
            await ReadOnce.SelectAsync(Ids, i => i, known, Reader(new List<string>()), (done, total) => { lock (totals) { dones.Add(done); totals.Add(total); } });
            CollectionAssert.AreEquivalent(new[] { 2, 2 }, totals);
            CollectionAssert.AreEquivalent(new[] { 1, 2 }, dones);
        }
    }
}
