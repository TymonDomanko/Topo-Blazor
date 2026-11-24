using Topo.Model.OAS;
using Topo.Services;

namespace TopoReportFunctionTest
{
    [TestClass]
    public class OasSummaryTests
    {
        private static OasSummaryMember Scout(string id, string first, string last = "", string patrol = "") =>
            new OasSummaryMember { MemberId = id, FirstName = first, LastName = last, Patrol = patrol };

        private static Result Record(string member, string stream, int stage, string status, string section = "scout", params string[] ticked)
        {
            var result = new Result
            {
                member_id = member,
                section = section,
                status = status,
                achievement_meta = new Achievement_Meta { stream = stream, stage = stage }
            };
            foreach (var tick in ticked)
                result.answers[tick + "_verifiedDate"] = "01/02/2026";
            return result;
        }

        private static OasSummaryCell CellOf(string stream, params Result[] records)
        {
            var row = OasSummaryBuilder.Build(new[] { Scout("a", "Amy") }, records, "scout", false, false).Single().Rows.Single();
            return row.Streams[Array.FindIndex(OasSummaryLayout.Streams, s => s.Key == stream)];
        }

        [TestMethod]
        public void TheSkillsAreTheNineInTheWallchartsOrderInThreeGroups()
        {
            CollectionAssert.AreEqual(new[] { "Bushcraft", "Bushwalking", "Camping", "Alpine", "Cycling", "Vertical", "Aquatics", "Boating", "Paddling" }, OasSummaryLayout.Streams.Select(s => s.Heading).ToArray());
            Assert.AreEqual(9, OasSummaryLayout.Groups.Sum(g => g.Span));
        }

        [TestMethod]
        public void EveryRowHasACellForEachSkillEvenWithNoRecords()
        {
            var row = OasSummaryBuilder.Build(new[] { Scout("a", "Amy") }, new Result[0], "scout", false, false).Single().Rows.Single();
            Assert.AreEqual(9, row.Streams.Count);
            Assert.IsTrue(row.Streams.All(c => c.Awarded == 0 && c.Working == 0 && c.State == OasWorking.None));
        }

        [TestMethod]
        public void TheAwardedStageIsTheHighestAwarded()
        {
            var cell = CellOf("camping", Record("a", "camping", 1, "awarded"), Record("a", "camping", 3, "awarded"), Record("a", "camping", 2, "awarded"));
            Assert.AreEqual(3, cell.Awarded);
            Assert.AreEqual(OasWorking.None, cell.State);
        }

        [TestMethod]
        public void StagesAwardedInAnotherSectionStillCount()
        {
            Assert.AreEqual(4, CellOf("alpine", Record("a", "alpine", 4, "awarded", "cub")).Awarded);
        }

        [TestMethod]
        public void AStageStartedAboveTheAwardedOneIsWhatIsBeingWorkedOn()
        {
            var cell = CellOf("bushcraft", Record("a", "bushcraft", 2, "awarded"), Record("a", "bushcraft", 3, "draft_review", "scout", "tie_a_knot"));
            Assert.AreEqual(2, cell.Awarded);
            Assert.AreEqual(3, cell.Working);
            Assert.AreEqual(OasWorking.InProgress, cell.State);
        }

        [TestMethod]
        public void AStageOpenedWithNothingTickedIsNotStarted()
        {
            var cell = CellOf("bushcraft", Record("a", "bushcraft", 2, "awarded"), Record("a", "bushcraft", 3, "draft_review"));
            Assert.AreEqual(0, cell.Working);
            Assert.AreEqual(OasWorking.None, cell.State);
        }

        [TestMethod]
        public void TheLogbookTickAloneCountsAsStarted()
        {
            var record = Record("a", "bushcraft", 1, "draft_review");
            record.answers["logbook_entry_ab12cd"] = "true";
            Assert.AreEqual(OasWorking.InProgress, CellOf("bushcraft", record).State);
        }

        [TestMethod]
        public void ReviewAndApprovedAreShownAsThoseStatesWhateverIsTicked()
        {
            Assert.AreEqual(OasWorking.InReview, CellOf("boating", Record("a", "boating", 1, "pending_review")).State);
            Assert.AreEqual(OasWorking.Approved, CellOf("boating", Record("a", "boating", 1, "approved")).State);
        }

        [TestMethod]
        public void OfSeveralStagesInTheWorkTheLowestIsShown()
        {
            var cell = CellOf("paddling", Record("a", "paddling", 5, "draft_review", "scout", "x"), Record("a", "paddling", 3, "pending_review"));
            Assert.AreEqual(3, cell.Working);
            Assert.AreEqual(OasWorking.InReview, cell.State);
        }

        [TestMethod]
        public void OfTwoRecordsForOneStageTheFurthestAlongIsShown()
        {
            var cell = CellOf("paddling", Record("a", "paddling", 2, "draft_review", "scout", "x"), Record("a", "paddling", 2, "approved"));
            Assert.AreEqual(OasWorking.Approved, cell.State);
        }

        [TestMethod]
        public void AStageNotAboveTheAwardedOneIsNotWorkedOn()
        {
            var cell = CellOf("vertical", Record("a", "vertical", 3, "awarded"), Record("a", "vertical", 2, "pending_review"), Record("a", "vertical", 3, "draft_review", "scout", "x"));
            Assert.AreEqual(3, cell.Awarded);
            Assert.AreEqual(0, cell.Working);
        }

        [TestMethod]
        public void WorkInAnotherSectionIsNotShown()
        {
            var cell = CellOf("cycling", Record("a", "cycling", 2, "pending_review", "cub"));
            Assert.AreEqual(0, cell.Working);
        }

        [TestMethod]
        public void OtherStatusesAreNotWorkInProgress()
        {
            Assert.AreEqual(OasWorking.None, CellOf("camping", Record("a", "camping", 1, "not_required")).State);
            Assert.AreEqual(OasWorking.None, CellOf("camping", Record("a", "camping", 1, "rejected", "scout", "x")).State);
        }

        [TestMethod]
        public void RecordsAreMatchedToTheirScoutAndSkill()
        {
            var rows = OasSummaryBuilder.Build(new[] { Scout("a", "Amy"), Scout("b", "Ben") },
                new[] { Record("a", "camping", 2, "awarded"), Record("b", "aquatics", 1, "awarded"), Record("zz", "camping", 9, "awarded"), Record("a", "no-such-skill", 4, "awarded") },
                "scout", false, false).Single().Rows;
            var camping = Array.FindIndex(OasSummaryLayout.Streams, s => s.Key == "camping");
            var aquatics = Array.FindIndex(OasSummaryLayout.Streams, s => s.Key == "aquatics");
            Assert.AreEqual(2, rows.Single(r => r.Name == "Amy").Streams[camping].Awarded);
            Assert.AreEqual(0, rows.Single(r => r.Name == "Ben").Streams[camping].Awarded);
            Assert.AreEqual(1, rows.Single(r => r.Name == "Ben").Streams[aquatics].Awarded);
        }

        [TestMethod]
        public void TheSkillNameIsMatchedWhateverItsCase()
        {
            Assert.AreEqual(3, CellOf("camping", Record("a", "Camping", 3, "awarded")).Awarded);
        }

        [TestMethod]
        public void TheUnitIsInNameOrderAndNamedLikeTheOtherReports()
        {
            var sheets = OasSummaryBuilder.Build(new[] { Scout("1", "Zed", "Zee"), Scout("2", "Amy", "Ant"), Scout("3", "Amy", "Bee") }, new Result[0], "scout", false, false);
            Assert.AreEqual(1, sheets.Count);
            CollectionAssert.AreEqual(new[] { "Amy A", "Amy B", "Zed" }, sheets[0].Rows.Select(r => r.Name).ToArray());
            var full = OasSummaryBuilder.Build(new[] { Scout("1", "Zed", "Zee") }, new Result[0], "scout", false, true);
            Assert.AreEqual("Zed Zee", full[0].Rows.Single().Name);
        }

        [TestMethod]
        public void BreakingByPatrolKeepsThePatrolsAndTheScoutsInTheOrderGiven()
        {
            // as the unit is listed: a Patrol Leader comes before the rest of their patrol, whatever their name
            var sheets = OasSummaryBuilder.Build(new[]
            {
                Scout("1", "Zoe", "Z", "Wombats"), Scout("2", "Abe", "A", "Wombats"), Scout("3", "Ivy", "I", "Eagles")
            }, new Result[0], "scout", true, false);
            CollectionAssert.AreEqual(new[] { "Wombats", "Eagles" }, sheets.Select(s => s.Patrol).ToArray());
            CollectionAssert.AreEqual(new[] { "Zoe", "Abe" }, sheets[0].Rows.Select(r => r.Name).ToArray());
        }

        [TestMethod]
        public void ANoteOnTheStageSaysWhereItIsUpTo()
        {
            Assert.AreEqual("3", OasSummaryLayout.WorkingText(new OasSummaryCell { Working = 3, State = OasWorking.InProgress }));
            Assert.AreEqual("3 R", OasSummaryLayout.WorkingText(new OasSummaryCell { Working = 3, State = OasWorking.InReview }));
            Assert.AreEqual("3 A", OasSummaryLayout.WorkingText(new OasSummaryCell { Working = 3, State = OasWorking.Approved }));
            Assert.AreEqual("", OasSummaryLayout.WorkingText(new OasSummaryCell()));
        }

        private static OasSummaryReport ReportOf(params OasSummarySheet[] sheets) =>
            new OasSummaryReport { UnitName = "Scout Unit", Section = "scout", GeneratedAt = new DateTime(2026, 11, 25), Sheets = sheets.ToList() };

        private static OasSummaryRow RowWith(string name, int stream, int awarded, int working = 0, OasWorking state = OasWorking.None)
        {
            var row = new OasSummaryRow { Name = name, Streams = OasSummaryLayout.Streams.Select(_ => new OasSummaryCell()).ToList() };
            row.Streams[stream] = new OasSummaryCell { Awarded = awarded, Working = working, State = state };
            return row;
        }

        [TestMethod]
        public void TheWorkbookHasTheHeadingsAndTwoColumnsForEachSkill()
        {
            var report = ReportOf(new OasSummarySheet { Rows = { RowWith("Amy", 0, 2, 3, OasWorking.InReview) } });
            var sheet = new ReportService().GenerateOasSummaryWorkbook(report, "Group", "scout", "Scout Unit").Worksheets[0];
            Assert.AreEqual("Unit", sheet.Name);
            Assert.AreEqual("Scout Unit", sheet.Range[2, 2].Text);
            StringAssert.Contains(sheet.Range[3, 2].Text, "Outdoor Adventure Skills as at");
            Assert.AreEqual("CORE", sheet.Range[4, 2].Text);
            Assert.AreEqual("LAND", sheet.Range[4, 8].Text);
            Assert.AreEqual("WATER", sheet.Range[4, 14].Text);
            Assert.AreEqual("Bushcraft", sheet.Range[5, 2].Text);
            Assert.AreEqual("Paddling", sheet.Range[5, 18].Text);
            Assert.AreEqual("Name", sheet.Range[6, 1].Text);
            Assert.AreEqual("Awarded", sheet.Range[6, 2].Text);
            Assert.AreEqual("Working on", sheet.Range[6, 3].Text);
            Assert.AreEqual("Working on", sheet.Range[6, 19].Text);
        }

        [TestMethod]
        public void TheWorkbookShowsAwardedAndWorkingOnWithTheirShading()
        {
            var report = ReportOf(new OasSummarySheet { Rows = { RowWith("Amy", 3, 4, 5, OasWorking.Approved), RowWith("Ben", 8, 1) } });
            var sheet = new ReportService().GenerateOasSummaryWorkbook(report, "Group", "scout", "Scout Unit").Worksheets[0];
            // Alpine is the fourth skill, so its columns are 8 and 9
            Assert.AreEqual("Amy", sheet.Range[7, 1].Text);
            Assert.AreEqual(4, sheet.Range[7, 8].Number);
            Assert.AreEqual("5 A", sheet.Range[7, 9].Text);
            Assert.AreEqual("Ben", sheet.Range[8, 1].Text);
            Assert.AreEqual(1, sheet.Range[8, 18].Number);
            Assert.IsTrue(string.IsNullOrEmpty(sheet.Range[8, 19].Text), "nothing being worked on");
            Assert.AreNotEqual(sheet.Range[7, 8].CellStyle.Color, sheet.Range[7, 9].CellStyle.Color, "awarded and working on are shaded differently");
        }

        [TestMethod]
        public void TheWorkbookHasASheetForEachPatrolAndAnEmptyUnitStillMakesASheet()
        {
            var patrols = new ReportService().GenerateOasSummaryWorkbook(ReportOf(new OasSummarySheet { Patrol = "Eagles" }, new OasSummarySheet { Patrol = "Wombats" }), "Group", "scout", "Scout Unit");
            Assert.AreEqual(2, patrols.Worksheets.Count);
            Assert.AreEqual("Eagles", patrols.Worksheets[0].Name);
            StringAssert.Contains(patrols.Worksheets[1].Range[3, 2].Text, "Wombats Outdoor Adventure Skills");
            var none = new ReportService().GenerateOasSummaryWorkbook(ReportOf(), "Group", "scout", "Scout Unit");
            Assert.AreEqual(1, none.Worksheets.Count);
        }

        [TestMethod]
        public void TheWorkbookEndsWithWhatTheShadingMeans()
        {
            var report = ReportOf(new OasSummarySheet { Rows = { RowWith("Amy", 0, 1) } });
            var sheet = new ReportService().GenerateOasSummaryWorkbook(report, "Group", "scout", "Scout Unit").Worksheets[0];
            Assert.AreEqual(OasSummaryLayout.Legend, sheet.Range[9, 1].Text);
        }

        [TestMethod]
        public void TheReportTravelsToTheFunctionAsItWasBuiltAndItsTypeIsLastSoOtherNumbersDoNotMove()
        {
            var report = ReportOf(new OasSummarySheet { Patrol = "Eagles", Rows = { RowWith("Amy", 0, 2, 3, OasWorking.InReview) } });
            var back = Newtonsoft.Json.JsonConvert.DeserializeObject<OasSummaryReport>(Newtonsoft.Json.JsonConvert.SerializeObject(report))!;
            Assert.AreEqual("Eagles", back.Sheets.Single().Patrol);
            Assert.AreEqual(OasWorking.InReview, back.Sheets.Single().Rows.Single().Streams[0].State);
            Assert.AreEqual(3, back.Sheets.Single().Rows.Single().Streams[0].Working);
            var types = Enum.GetValues<Topo.Model.ReportGeneration.ReportType>();
            Assert.AreEqual(Topo.Model.ReportGeneration.ReportType.OasSummary, types[^1]);
            Assert.AreEqual(21, Array.IndexOf(types, Topo.Model.ReportGeneration.ReportType.ReadyToSubmit), "the number the deployed function knows it by");
        }
    }
}
