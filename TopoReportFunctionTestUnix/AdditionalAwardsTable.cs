using Topo.Model.AdditionalAwards;
using Topo.Services;

namespace TopoReportFunctionTest
{
    [TestClass]
    public class AdditionalAwardsTableTests
    {
        private static readonly List<AdditionalAwardSpecificationListModel> Specs = new List<AdditionalAwardSpecificationListModel>
        {
            new AdditionalAwardSpecificationListModel { id = "camper_award_10", name = "Camper Award - 10 Nights", additionalAwardSortIndex = 0, additionalAwardValue = 10 },
            new AdditionalAwardSpecificationListModel { id = "camper_award_25", name = "Camper Award - 25 Nights", additionalAwardSortIndex = 1, additionalAwardValue = 25 },
            new AdditionalAwardSpecificationListModel { id = "walkabout_10", name = "Walkabout Award - 10km", additionalAwardSortIndex = 2, additionalAwardValue = 10 }
        };

        // The row the service adds first for each Scout, which carries their totals and no award
        // The name doubles as the member id in these tests
        private static AdditionalAwardListModel Totals(string name, int nights, int kms, string patrol = "") =>
            new AdditionalAwardListModel { MemberId = name, MemberName = name, MemberPatrol = patrol, NightsCamped = nights, KMsHiked = kms };

        private static AdditionalAwardListModel Award(string name, string id, int nights, int kms, DateTime? awarded, DateTime? presented = null, string patrol = "")
        {
            var spec = Specs.FirstOrDefault(s => s.id == id);
            return new AdditionalAwardListModel
            {
                MemberId = name, MemberName = name, MemberPatrol = patrol, AwardId = id, AwardName = spec?.name ?? "", AwardSortIndex = spec?.additionalAwardSortIndex ?? 0,
                NightsCamped = nights, KMsHiked = kms, AwardDate = awarded, PresentedDate = presented, AwardUnits = spec?.additionalAwardValue ?? 0
            };
        }

        private static AdditionalAwardsReportDataModel Data(params AdditionalAwardListModel[] awards)
        {
            var sorted = awards.OrderBy(a => a.MemberName).ThenBy(a => a.AwardSortIndex).ToList();
            return new AdditionalAwardsReportDataModel { AwardSpecificationsList = Specs, SortedAdditionalAwardsList = sorted, DistinctAwards = AwardsTable.DistinctAwards(sorted) };
        }

        private static AwardsTableCell CellFor(AwardsTable table, string name, string awardId) =>
            table.Rows.Single(r => r.Name == name).Cells[table.Columns.FindIndex(c => c.AwardId == awardId)];

        [TestMethod]
        public void EachAwardGetsAColumnInOrderWithItsNameAndABlankOneStandsForNoAward()
        {
            var table = AwardsTable.Build(Data(
                Totals("Amy", 3, 4), Award("Amy", "camper_award_10", 3, 4, null),
                Totals("Ben", 30, 0), Award("Ben", "camper_award_25", 30, 0, null), Award("Ben", "walkabout_10", 30, 0, null)));

            // the rows that carry only a Scout's totals have no award, so there is no column for them
            CollectionAssert.AreEqual(new[] { "camper_award_10", "camper_award_25", "walkabout_10" }, table.Columns.Select(c => c.AwardId).ToList());
            CollectionAssert.AreEqual(new[] { "Camper Award - 10 Nights", "Camper Award - 25 Nights", "Walkabout Award - 10km" }, table.Columns.Select(c => c.Heading).ToList());
            Assert.IsTrue(table.Rows.All(r => r.Cells.Count == table.Columns.Count));
        }

        [TestMethod]
        public void ScoutsAreOneRowEachByNameWithTheirTotals()
        {
            var table = AwardsTable.Build(Data(Totals("Zoe", 12, 7), Award("Zoe", "camper_award_10", 12, 7, null), Totals("Amy", 3, 4)));

            CollectionAssert.AreEqual(new[] { "Amy", "Zoe" }, table.Rows.Select(r => r.Name).ToList());
            Assert.AreEqual(4, table.Rows[0].KMsHiked);
            Assert.AreEqual(3, table.Rows[0].NightsCamped);
            Assert.AreEqual(7, table.Rows[1].KMsHiked);
            Assert.AreEqual(12, table.Rows[1].NightsCamped);
        }

        [TestMethod]
        public void AnAwardedAwardShowsItsDateAndAPresentedOneShowsThePresentedDateInGreen()
        {
            var awarded = new DateTime(2026, 3, 5);
            var presented = new DateTime(2026, 4, 20);
            var table = AwardsTable.Build(Data(
                Totals("Amy", 12, 0), Award("Amy", "camper_award_10", 12, 0, awarded),
                Totals("Ben", 12, 0), Award("Ben", "camper_award_10", 12, 0, awarded, presented)));

            var notPresented = CellFor(table, "Amy", "camper_award_10");
            Assert.AreEqual(awarded, notPresented.Date);
            Assert.AreEqual(AwardsTable.Awarded, notPresented.Colour);

            var done = CellFor(table, "Ben", "camper_award_10");
            Assert.AreEqual(presented, done.Date);
            Assert.AreEqual(AwardsTable.Presented, done.Colour);
        }

        [TestMethod]
        public void ANextAwardAlreadyWithinReachIsRedAndSoIsTheTotalThatEarnedIt()
        {
            var table = AwardsTable.Build(Data(
                Totals("Amy", 12, 3), Award("Amy", "camper_award_10", 12, 3, null), Award("Amy", "walkabout_10", 12, 3, null), // 12 nights is enough, 3 km is not
                Totals("Ben", 5, 40), Award("Ben", "camper_award_10", 5, 40, null), Award("Ben", "walkabout_10", 5, 40, null)));

            var amy = table.Rows.Single(r => r.Name == "Amy");
            Assert.AreEqual(AwardsTable.NotAwarded, CellFor(table, "Amy", "camper_award_10").Colour);
            Assert.AreEqual(AwardsTable.NotAwarded, amy.NightsColour);
            Assert.IsNull(CellFor(table, "Amy", "walkabout_10").Colour);
            Assert.IsNull(amy.KMsColour);

            var ben = table.Rows.Single(r => r.Name == "Ben");
            Assert.IsNull(CellFor(table, "Ben", "camper_award_10").Colour);
            Assert.IsNull(ben.NightsColour);
            Assert.AreEqual(AwardsTable.NotAwarded, CellFor(table, "Ben", "walkabout_10").Colour);
            Assert.AreEqual(AwardsTable.NotAwarded, ben.KMsColour);
        }

        [TestMethod]
        public void AnAwardTerrainGaveNoNameForLandsInTheBlankColumnRatherThanBeingLost()
        {
            var table = AwardsTable.Build(Data(Totals("Amy", 0, 0), Award("Amy", "", 0, 0, new DateTime(2026, 1, 2), new DateTime(2026, 1, 9))));

            // an award with no specification has no id to find, so it lands in the blank column, which stays because something is in it
            Assert.AreEqual(1, table.Columns.Count);
            Assert.AreEqual("", table.Columns[0].Heading);
            Assert.AreEqual(new DateTime(2026, 1, 9), table.Rows.Single().Cells[0].Date);
        }

        [TestMethod]
        public void TheReportForSomeScoutsHasOnlyTheirRowsUnderTheWholeUnitsAwards()
        {
            var everyone = Data(
                Totals("Amy", 3, 4), Award("Amy", "camper_award_10", 3, 4, null),
                Totals("Ben", 30, 40), Award("Ben", "camper_award_25", 30, 40, null), Award("Ben", "walkabout_10", 30, 40, null));

            var justAmy = everyone.ForMembers(new Dictionary<string, string> { ["Amy"] = "Amy" });
            var table = AwardsTable.Build(justAmy);

            CollectionAssert.AreEqual(new[] { "Amy" }, table.Rows.Select(r => r.Name).ToList());
            // the awards across the top are the unit's, so a Scout's report is laid out like the unit's whoever is in it
            CollectionAssert.AreEqual(everyone.DistinctAwards, justAmy.DistinctAwards);
            CollectionAssert.AreEqual(new[] { "camper_award_10", "camper_award_25", "walkabout_10" }, table.Columns.Select(c => c.AwardId).ToList());
            Assert.AreEqual(5, everyone.SortedAdditionalAwardsList.Count); // the full report is left as it was

            Assert.AreEqual(0, AwardsTable.Build(everyone.ForMembers(new Dictionary<string, string>())).Rows.Count);
        }

        [TestMethod]
        public void ScoutsAreShownUnderTheNamesGivenAndOrderedByThem()
        {
            var everyone = Data(Totals("id-1", 1, 1), Totals("id-2", 2, 2), Totals("id-3", 3, 3));
            foreach (var award in everyone.SortedAdditionalAwardsList)
                award.MemberName = "Full Name " + award.MemberId; // the report as it was read, under full names

            var shortened = everyone.ForMembers(new Dictionary<string, string> { ["id-1"] = "Zed", ["id-2"] = "Amy B", ["id-3"] = "Amy A" });
            var table = AwardsTable.Build(shortened);

            CollectionAssert.AreEqual(new[] { "Amy A", "Amy B", "Zed" }, table.Rows.Select(r => r.Name).ToList());
            Assert.AreEqual(3, table.Rows.Single(r => r.Name == "Amy A").NightsCamped); // each keeps their own totals
            Assert.IsTrue(everyone.SortedAdditionalAwardsList.All(a => a.MemberName.StartsWith("Full Name"))); // and the original is not renamed
        }

        [TestMethod]
        public void TheReportCanBeSplitIntoATablePerPatrolWithTheSameAwardsAcrossTheTopOfEach()
        {
            var data = Data(
                Totals("Amy", 3, 4, "Red"), Award("Amy", "camper_award_10", 3, 4, new DateTime(2026, 1, 2), patrol: "Red"),
                Totals("Ben", 30, 40, "Blue"), Award("Ben", "walkabout_10", 30, 40, null, patrol: "Blue"),
                Totals("Cat", 0, 0));

            var whole = AwardsTable.BuildSheets(data, false);
            Assert.AreEqual(1, whole.Count);
            Assert.AreEqual(3, whole.Single().Rows.Count);
            Assert.AreEqual("", whole.Single().Patrol);

            var patrols = AwardsTable.BuildSheets(data, true);
            CollectionAssert.AreEqual(new[] { "", "Blue", "Red" }, patrols.Select(t => t.Patrol).ToList()); // no patrol first
            CollectionAssert.AreEqual(new[] { "Cat" }, patrols[0].Rows.Select(r => r.Name).ToList());
            CollectionAssert.AreEqual(new[] { "Ben" }, patrols[1].Rows.Select(r => r.Name).ToList());
            CollectionAssert.AreEqual(new[] { "Amy" }, patrols[2].Rows.Select(r => r.Name).ToList());

            // the same awards in the same order on every sheet, so the sheets line up
            var columns = whole.Single().Columns.Select(c => c.AwardId).ToList();
            Assert.IsTrue(patrols.All(t => t.Columns.Select(c => c.AwardId).SequenceEqual(columns)));
            Assert.AreEqual(new DateTime(2026, 1, 2), CellFor(patrols[2], "Amy", "camper_award_10").Date);
            Assert.AreEqual("Amy", patrols[2].Rows.Single().MemberId); // each row knows whose it is, so it can be ticked
        }

        [TestMethod]
        public void NobodyStillGivesOneEmptyTableWhetherOrNotSplitByPatrol()
        {
            Assert.AreEqual(1, AwardsTable.BuildSheets(new AdditionalAwardsReportDataModel(), false).Count);
            Assert.AreEqual(1, AwardsTable.BuildSheets(new AdditionalAwardsReportDataModel(), true).Count);
            Assert.AreEqual(0, AwardsTable.BuildSheets(new AdditionalAwardsReportDataModel(), true).Single().Rows.Count);
        }

        [TestMethod]
        public void TheWorkbookHasASheetForTheUnitOrOneForEachPatrolNamedAndTitledByIt()
        {
            SetEnvironment.SetSyncfusionLicense();
            var data = Data(
                Totals("Amy", 3, 4, "Red"), Award("Amy", "camper_award_10", 3, 4, new DateTime(2026, 1, 2), patrol: "Red"),
                Totals("Ben", 30, 40, "Blue"), Totals("Cat", 0, 0));
            var service = new ReportService();

            var unit = service.GenerateAdditionalAwardsWorkbook(data.AwardSpecificationsList, data.SortedAdditionalAwardsList, data.DistinctAwards, "Group", "scout", "Scout Unit");
            Assert.AreEqual(1, unit.Worksheets.Count);
            StringAssert.StartsWith(unit.Worksheets[0].Range[2, 2].Text, "Additional Badges Awarded as at");

            var patrols = service.GenerateAdditionalAwardsWorkbook(data.AwardSpecificationsList, data.SortedAdditionalAwardsList, data.DistinctAwards, "Group", "scout", "Scout Unit", true);
            CollectionAssert.AreEqual(new[] { "Unit", "Blue", "Red" }, Enumerable.Range(0, patrols.Worksheets.Count).Select(i => patrols.Worksheets[i].Name).ToList());
            StringAssert.StartsWith(patrols.Worksheets[2].Range[2, 2].Text, "Red Additional Badges Awarded as at");
            Assert.AreEqual("Amy", patrols.Worksheets[2].Range[5, 1].Text); // the one Scout of the patrol is the row under the headings
            Assert.AreEqual("Ben", patrols.Worksheets[1].Range[5, 1].Text);
        }

        [TestMethod]
        public void NothingToShowBuildsAnEmptyTable()
        {
            var table = AwardsTable.Build(new AdditionalAwardsReportDataModel());

            Assert.AreEqual(0, table.Columns.Count);
            Assert.AreEqual(0, table.Rows.Count);
        }
    }
}
