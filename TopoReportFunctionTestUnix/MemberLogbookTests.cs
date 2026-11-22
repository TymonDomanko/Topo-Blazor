using Topo.Model.Logbook;

namespace TopoReportFunctionTest
{
    [TestClass]
    public class MemberLogbookTests
    {
        private static MemberLogbookReportViewModel Entry(string who, string name, string date, float km = 0, float nights = 0) => new MemberLogbookReportViewModel
        {
            MemberName = who, ActivityName = name, ActivityDate = DateTime.Parse(date), KilometersHiked = km, NightsCamped = nights
        };

        private static MemberLogbook Amy() => new MemberLogbook
        {
            MemberId = "a", MemberName = "Amy Ay", TotalKilometres = 12.5f, TotalNights = 3,
            Entries =
            {
                Entry("Amy Ay", "Winter camp", "2026-06-12", 0, 2),
                Entry("Amy Ay", "Day hike", "2026-09-01", 12.5f),
                Entry("Amy Ay", "Beach hike", "2026-09-01", 0)
            }
        };

        [TestMethod]
        public void TheEntriesAreCountedAndTheLatestIsTheMostRecentDay()
        {
            var logbook = Amy();
            Assert.AreEqual(3, logbook.EntryCount);
            Assert.AreEqual(new DateTime(2026, 9, 1), logbook.LastActivity);
        }

        [TestMethod]
        public void NoEntriesMeansNoLatestDay()
        {
            var logbook = new MemberLogbook { MemberId = "b", MemberName = "Ben Bee", TotalNights = 0 };
            Assert.AreEqual(0, logbook.EntryCount);
            Assert.IsNull(logbook.LastActivity);
            Assert.AreEqual(0, logbook.NewestFirst().Count);
        }

        [TestMethod]
        public void EntriesAreListedNewestFirstAndSameDayEntriesByName()
        {
            CollectionAssert.AreEqual(new[] { "Beach hike", "Day hike", "Winter camp" }, Amy().NewestFirst().Select(e => e.ActivityName).ToList());
        }

        [TestMethod]
        public void TotalsAreWrittenWithoutAPointlessDecimalAndWithTheRightPlural()
        {
            Assert.AreEqual("12.5 km", MemberLogbook.Kilometres(12.5f));
            Assert.AreEqual("30 km", MemberLogbook.Kilometres(30f));
            Assert.AreEqual("0 km", MemberLogbook.Kilometres(0));
            Assert.AreEqual("1 night", MemberLogbook.Nights(1));
            Assert.AreEqual("12 nights", MemberLogbook.Nights(12));
            Assert.AreEqual("0 nights", MemberLogbook.Nights(0));
            Assert.AreEqual("1 entry", MemberLogbook.EntryText(1));
            Assert.AreEqual("8 entries", MemberLogbook.EntryText(8));
        }

        [TestMethod]
        public void TheReportHasARowForEachEntryOfTheScoutsGivenInOrderAndNoneForAScoutWithoutEntries()
        {
            var empty = new MemberLogbook { MemberId = "b", MemberName = "Ben Bee" };
            var rows = MemberLogbook.ReportRows(new[] { empty, Amy() });
            Assert.AreEqual(3, rows.Count);
            Assert.IsTrue(rows.All(r => r.MemberName == "Amy Ay"));
        }
    }
}
