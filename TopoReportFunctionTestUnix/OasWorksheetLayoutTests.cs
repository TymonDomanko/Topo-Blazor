using Topo.Model.OAS;

namespace TopoReportFunctionTest
{
    [TestClass]
    public class OasWorksheetLayoutTests
    {
        private static OASWorksheetAnswers Answer(string title, string member, string input, string group, int groupSort, int sort, bool answered = false, bool awarded = false, string text = "", string patrol = "") =>
            new OASWorksheetAnswers
            {
                TemplateTitle = title, MemberId = member.ToLowerInvariant(), MemberName = member, MemberPatrol = patrol,
                InputId = input, InputLabel = input + " label", InputTitle = group, InputTitleSortIndex = groupSort, InputSortIndex = sort,
                Answered = answered, Awarded = awarded, MemberAnswer = text
            };

        // Two Scouts, three requirements in Plan and Do, as the answers come from the service (in no particular order)
        private static List<OASWorksheetAnswers> Camping2() => new List<OASWorksheetAnswers>
        {
            Answer("Camping 2", "Ben", "do_1", "Do", 2, 3, true, false, "12/11/26"),
            Answer("Camping 2", "Amy", "plan_2", "Plan", 1, 2, true, true, "01/10/26"),
            Answer("Camping 2", "Amy", "plan_1", "Plan", 1, 1),
            Answer("Camping 2", "Amy", "do_1", "Do", 2, 3, true, false, ""),
            Answer("Camping 2", "Ben", "plan_1", "Plan", 1, 1),
            Answer("Camping 2", "Ben", "plan_2", "Plan", 1, 2)
        };

        [TestMethod]
        public void ARequirementIsARowAndAScoutIsAColumnInTheOrderTerrainHasThem()
        {
            var sheet = OasWorksheetLayout.Build(Camping2(), false).Single();
            Assert.AreEqual("Camping 2", sheet.Title);
            CollectionAssert.AreEqual(new[] { "plan_1 label", "plan_2 label", "do_1 label" }, sheet.Requirements.ToArray());
            CollectionAssert.AreEqual(new[] { "Amy", "Ben" }, sheet.Scouts.ToArray());
            Assert.AreEqual(3, sheet.Cells.Count);
            Assert.IsTrue(sheet.Cells.All(row => row.Count == 2));
        }

        [TestMethod]
        public void PlanDoAndReviewHeadingsCoverTheirRequirementsAndAreColoured()
        {
            var groups = OasWorksheetLayout.Build(Camping2(), false).Single().Groups;
            CollectionAssert.AreEqual(new[] { "Plan", "Do" }, groups.Select(g => g.Heading).ToArray());
            CollectionAssert.AreEqual(new[] { 2, 1 }, groups.Select(g => g.Span).ToArray());
            Assert.AreEqual("#add8e6", groups[0].Colour);
            Assert.AreEqual("#87ceeb", groups[1].Colour);
            Assert.AreEqual("", OasWorksheetLayout.ColourOf("Something else"));
        }

        [TestMethod]
        public void AwardedIsGreenWithNoTextAndInProgressIsOrangeWithTheDateOrY()
        {
            var cells = OasWorksheetLayout.Build(Camping2(), false).Single().Cells;
            // row 1 is plan_2: Amy awarded, Ben nothing
            Assert.AreEqual(OasWorksheetCellKind.Awarded, cells[1][0].Kind);
            Assert.AreEqual("", cells[1][0].Text);
            Assert.AreEqual(OasWorksheetCellKind.Blank, cells[1][1].Kind);
            // row 2 is do_1: Amy ticked with no date, Ben with one
            Assert.AreEqual(OasWorksheetCellKind.InProgress, cells[2][0].Kind);
            Assert.AreEqual("Y", cells[2][0].Text);
            Assert.AreEqual("12/11/26", cells[2][1].Text);
        }

        [TestMethod]
        public void OnlyTheLogbookRequirementShowsATextWhenAwarded()
        {
            var answers = new List<OASWorksheetAnswers> { Answer("Camping 1", "Amy", "logbook_up_to_date", "Do", 1, 1, true, true, "Yes"), Answer("Camping 1", "Amy", "x", "Do", 1, 2, true, true, "01/10/26") };
            var cells = OasWorksheetLayout.Build(answers, false).Single().Cells;
            Assert.AreEqual("Yes", cells[0][0].Text);
            Assert.AreEqual("", cells[1][0].Text);
        }

        [TestMethod]
        public void StagesAreSeparateSheetsInTitleOrder()
        {
            var answers = Camping2().Concat(new[] { Answer("Bushcraft 1", "Amy", "a", "Plan", 1, 1) }).ToList();
            CollectionAssert.AreEqual(new[] { "Bushcraft 1", "Camping 2" }, OasWorksheetLayout.Build(answers, false).Select(s => s.Title).ToArray());
        }

        [TestMethod]
        public void BreakingByPatrolMakesASheetForEachPatrolWithItsOwnScouts()
        {
            var answers = new List<OASWorksheetAnswers>
            {
                Answer("Camping 2", "Amy", "a", "Plan", 1, 1, patrol: "Eagles"), Answer("Camping 2", "Ben", "a", "Plan", 1, 1, patrol: "Wombats"), Answer("Camping 2", "Cat", "a", "Plan", 1, 1, patrol: "Eagles")
            };
            var sheets = OasWorksheetLayout.Build(answers, true);
            CollectionAssert.AreEqual(new[] { "Camping 2 - Eagles", "Camping 2 - Wombats" }, sheets.Select(s => s.Title).ToArray());
            CollectionAssert.AreEqual(new[] { "Amy", "Cat" }, sheets[0].Scouts.ToArray());
            Assert.AreEqual(1, OasWorksheetLayout.Build(answers, false).Count);
        }

        [TestMethod]
        public void NoAnswersMakeNoSheets()
        {
            Assert.AreEqual(0, OasWorksheetLayout.Build(new OASWorksheetAnswers[0], false).Count);
        }
    }
}
