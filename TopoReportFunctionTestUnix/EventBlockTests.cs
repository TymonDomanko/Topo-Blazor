using Topo.Model.Cache;
using Topo.Model.Program;

namespace TopoReportFunctionTest
{
    [TestClass]
    public class EventBlockTests
    {
        private static Event On(string id, DateTime start, int hours = 2) => new Event { id = id, start_datetime = start, end_datetime = start.AddHours(hours) };

        [TestMethod]
        public void DatesAreCoveredByWholeHalfYears()
        {
            var blocks = EventBlocks.Between(new DateTime(2025, 8, 15), new DateTime(2026, 7, 1)).ToList();

            CollectionAssert.AreEqual(new[] { new DateTime(2025, 7, 1), new DateTime(2026, 1, 1), new DateTime(2026, 7, 1) }, blocks.Select(b => b.From).ToList());
            Assert.AreEqual(new DateTime(2025, 12, 31, 23, 59, 59), blocks[0].To);
            Assert.AreEqual(new DateTime(2026, 6, 30, 23, 59, 59), blocks[1].To);
        }

        [TestMethod]
        public void DatesInsideOneHalfYearNeedOneBlockAndBackwardDatesNone()
        {
            Assert.AreEqual(1, EventBlocks.Between(new DateTime(2026, 2, 1), new DateTime(2026, 2, 2)).Count());
            Assert.AreEqual(0, EventBlocks.Between(new DateTime(2026, 3, 1), new DateTime(2026, 2, 1)).Count());
        }

        [TestMethod]
        public void LookingFurtherBackKeepsTheSameBlocksAndAddsEarlierOnes()
        {
            var near = EventBlocks.Between(new DateTime(2026, 5, 27), new DateTime(2027, 5, 27)).ToList();
            var further = EventBlocks.Between(new DateTime(2025, 11, 27), new DateTime(2027, 5, 27)).ToList();

            CollectionAssert.IsSubsetOf(near, further);
            Assert.AreEqual(near.Count + 1, further.Count);
        }

        [TestMethod]
        public void AHalfYearIsKeptOnceItEndedMoreThanFourMonthsAgo()
        {
            var now = new DateTime(2026, 11, 27);

            Assert.IsTrue(EventBlocks.IsOld(new DateTime(2026, 6, 30, 23, 59, 59), now));
            Assert.IsFalse(EventBlocks.IsOld(new DateTime(2026, 12, 31, 23, 59, 59), now));
        }

        [TestMethod]
        public void MergedBlocksHoldEachEventOnceAndOnlyThoseOnBetweenTheDates()
        {
            var overNewYear = On("camp", new DateTime(2025, 12, 31, 20, 0, 0), 24);
            var events = new[]
            {
                On("later", new DateTime(2026, 3, 1)), overNewYear, On("before", new DateTime(2025, 10, 1)), overNewYear,
                On("after", new DateTime(2026, 6, 1))
            };

            var merged = EventBlocks.Merge(events, new DateTime(2026, 1, 1), new DateTime(2026, 4, 1));

            CollectionAssert.AreEqual(new[] { "camp", "later" }, merged.Select(e => e.id).ToList());
        }
    }
}
