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
            var row = OasSummaryBuilder.Build(new[] { Scout("a", "Amy") }, records, "scout", false, false, OasSummaryLayout.SkillColumns()).Sheets.Single().Rows.Single();
            return row.Cells[Array.FindIndex(OasSummaryLayout.Streams, s => s.Key == stream)];
        }

        [TestMethod]
        public void TheSkillsAreTheNineInTheWallchartsOrderInThreeGroups()
        {
            CollectionAssert.AreEqual(new[] { "Bushcraft", "Bushwalking", "Camping", "Alpine", "Cycling", "Vertical", "Aquatics", "Boating", "Paddling" }, OasSummaryLayout.Streams.Select(s => s.Heading).ToArray());
            Assert.AreEqual(9, OasSummaryLayout.GroupSpans(OasSummaryLayout.SkillColumns()).Sum(g => g.Span));
            CollectionAssert.AreEqual(new[] { "CORE", "LAND", "WATER" }, OasSummaryLayout.GroupSpans(OasSummaryLayout.SkillColumns()).Select(g => g.Heading).ToArray());
        }

        [TestMethod]
        public void EveryRowHasACellForEachSkillEvenWithNoRecords()
        {
            var row = OasSummaryBuilder.Build(new[] { Scout("a", "Amy") }, new Result[0], "scout", false, false, OasSummaryLayout.SkillColumns()).Sheets.Single().Rows.Single();
            Assert.AreEqual(9, row.Cells.Count);
            Assert.IsTrue(row.Cells.All(c => c.Awarded == 0 && c.Working == 0 && c.State == OasWorking.None));
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
                "scout", false, false, OasSummaryLayout.SkillColumns()).Sheets.Single().Rows;
            var camping = Array.FindIndex(OasSummaryLayout.Streams, s => s.Key == "camping");
            var aquatics = Array.FindIndex(OasSummaryLayout.Streams, s => s.Key == "aquatics");
            Assert.AreEqual(2, rows.Single(r => r.Name == "Amy").Cells[camping].Awarded);
            Assert.AreEqual(0, rows.Single(r => r.Name == "Ben").Cells[camping].Awarded);
            Assert.AreEqual(1, rows.Single(r => r.Name == "Ben").Cells[aquatics].Awarded);
        }

        [TestMethod]
        public void TheSkillNameIsMatchedWhateverItsCase()
        {
            Assert.AreEqual(3, CellOf("camping", Record("a", "Camping", 3, "awarded")).Awarded);
        }

        [TestMethod]
        public void TheUnitIsInNameOrderAndNamedLikeTheOtherReports()
        {
            var sheets = OasSummaryBuilder.Build(new[] { Scout("1", "Zed", "Zee"), Scout("2", "Amy", "Ant"), Scout("3", "Amy", "Bee") }, new Result[0], "scout", false, false, OasSummaryLayout.SkillColumns()).Sheets;
            Assert.AreEqual(1, sheets.Count);
            CollectionAssert.AreEqual(new[] { "Amy A", "Amy B", "Zed" }, sheets[0].Rows.Select(r => r.Name).ToArray());
            var full = OasSummaryBuilder.Build(new[] { Scout("1", "Zed", "Zee") }, new Result[0], "scout", false, true, OasSummaryLayout.SkillColumns()).Sheets;
            Assert.AreEqual("Zed Zee", full[0].Rows.Single().Name);
        }

        [TestMethod]
        public void BreakingByPatrolKeepsThePatrolsAndTheScoutsInTheOrderGiven()
        {
            // as the unit is listed: a Patrol Leader comes before the rest of their patrol, whatever their name
            var sheets = OasSummaryBuilder.Build(new[]
            {
                Scout("1", "Zoe", "Z", "Wombats"), Scout("2", "Abe", "A", "Wombats"), Scout("3", "Ivy", "I", "Eagles")
            }, new Result[0], "scout", true, false, OasSummaryLayout.SkillColumns()).Sheets;
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
            new OasSummaryReport { UnitName = "Scout Unit", Section = "scout", GeneratedAt = new DateTime(2026, 11, 25), Columns = OasSummaryLayout.SkillColumns(), Sheets = sheets.ToList() };

        private static OasSummaryRow RowWith(string name, int stream, int awarded, int working = 0, OasWorking state = OasWorking.None)
        {
            var row = new OasSummaryRow { Name = name, Cells = OasSummaryLayout.Streams.Select(_ => new OasSummaryCell()).ToList() };
            row.Cells[stream] = new OasSummaryCell { Awarded = awarded, Working = working, State = state };
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
            Assert.AreEqual(OasWorking.InReview, back.Sheets.Single().Rows.Single().Cells[0].State);
            Assert.AreEqual(3, back.Sheets.Single().Rows.Single().Cells[0].Working);
            var types = Enum.GetValues<Topo.Model.ReportGeneration.ReportType>();
            Assert.AreEqual(Topo.Model.ReportGeneration.ReportType.OasSummary, types[^1]);
            Assert.AreEqual(21, Array.IndexOf(types, Topo.Model.ReportGeneration.ReportType.ReadyToSubmit), "the number the deployed function knows it by");
        }

        private static Result BranchRecord(string member, string stream, string branch, int stage, string status, params string[] ticked)
        {
            var record = Record(member, stream, stage, status, "scout", ticked);
            record.achievement_meta.branch = branch;
            return record;
        }

        private static readonly OasBranch[] Tree =
        {
            new OasBranch { Stream = "paddling", Key = "paddling", Title = "Paddling" },
            new OasBranch { Stream = "paddling", Key = "canoeing", Title = "Canoeing" },
            new OasBranch { Stream = "paddling", Key = "kayaking", Title = "Kayaking" },
            new OasBranch { Stream = "boating", Key = "sailing", Title = "Sailing" }
        };

        private static (List<OasSummaryColumn> columns, List<OasSummarySheet> sheets) Split(Result[] records, bool hideEmpty, params OasSummaryMember[] members)
        {
            var columns = OasSummaryLayout.BranchColumns(Tree, records);
            var report = OasSummaryBuilder.Build(members.Length == 0 ? new[] { Scout("a", "Amy") } : members, records, "scout", false, false, columns, hideEmpty);
            return (report.Columns, report.Sheets);
        }

        [TestMethod]
        public void BranchColumnsKeepTheTreesOrderStartWithMainAndAddBranchesOnlyTheRecordsKnow()
        {
            var columns = OasSummaryLayout.BranchColumns(Tree, new[] { BranchRecord("a", "paddling", "sea-kayaking", 4, "awarded") });
            var paddling = columns.Where(c => c.Stream == "paddling").ToList();
            CollectionAssert.AreEqual(new[] { "Main", "Canoeing", "Kayaking", "Sea Kayaking" }, paddling.Select(c => c.Heading).ToArray());
            Assert.AreEqual("paddling", paddling[0].Branch, "the stages before the skill forks");
            CollectionAssert.AreEqual(new[] { "Main", "Sailing" }, columns.Where(c => c.Stream == "boating").Select(c => c.Heading).ToArray());
            Assert.AreEqual(1, columns.Count(c => c.Stream == "camping"), "a skill that never forks is just Main");
            Assert.IsTrue(columns.All(c => c.Heading != ""));
        }

        [TestMethod]
        public void ABranchColumnCountsOnlyItsOwnBranchWhileASkillColumnCountsAll()
        {
            var records = new[] { BranchRecord("a", "paddling", "paddling", 3, "awarded"), BranchRecord("a", "paddling", "canoeing", 5, "awarded") };
            var (columns, sheets) = Split(records, false);
            var cells = sheets.Single().Rows.Single().Cells;
            Assert.AreEqual(3, cells[columns.FindIndex(c => c.Stream == "paddling" && c.Branch == "paddling")].Awarded);
            Assert.AreEqual(5, cells[columns.FindIndex(c => c.Stream == "paddling" && c.Branch == "canoeing")].Awarded);
            Assert.AreEqual(0, cells[columns.FindIndex(c => c.Stream == "paddling" && c.Branch == "kayaking")].Awarded);
            Assert.AreEqual(5, CellOf("paddling", records).Awarded);
        }

        [TestMethod]
        public void AStageBeingWorkedOnIsInTheBranchItIsIn()
        {
            var records = new[] { BranchRecord("a", "paddling", "paddling", 3, "awarded"), BranchRecord("a", "paddling", "canoeing", 4, "draft_review", "x") };
            var (columns, sheets) = Split(records, false);
            var canoeing = sheets.Single().Rows.Single().Cells[columns.FindIndex(c => c.Branch == "canoeing")];
            Assert.AreEqual(0, canoeing.Awarded);
            Assert.AreEqual(4, canoeing.Working);
            Assert.AreEqual(OasWorking.InProgress, canoeing.State);
            var main = sheets.Single().Rows.Single().Cells[columns.FindIndex(c => c.Branch == "paddling")];
            Assert.AreEqual(0, main.Working);
        }

        [TestMethod]
        public void BranchesNobodyHasStartedAreLeftOutButEverySkillKeepsItsMainColumn()
        {
            var records = new[] { BranchRecord("a", "paddling", "canoeing", 4, "awarded"), BranchRecord("b", "boating", "sailing", 4, "pending_review") };
            var (columns, sheets) = Split(records, true, Scout("a", "Amy"), Scout("b", "Ben"));
            CollectionAssert.AreEqual(new[] { "Main", "Canoeing" }, columns.Where(c => c.Stream == "paddling").Select(c => c.Heading).ToArray());
            CollectionAssert.AreEqual(new[] { "Main", "Sailing" }, columns.Where(c => c.Stream == "boating").Select(c => c.Heading).ToArray());
            Assert.AreEqual(9 + 2, columns.Count, "nine mains, canoeing and sailing");
            Assert.IsTrue(sheets.Single().Rows.All(r => r.Cells.Count == columns.Count), "every row matches the columns left");
            Assert.AreEqual(4, sheets.Single().Rows.Single(r => r.Name == "Amy").Cells[columns.FindIndex(c => c.Branch == "canoeing")].Awarded);
        }

        [TestMethod]
        public void EveryPatrolSheetHasTheSameColumnsWhenBranchesAreLeftOut()
        {
            var records = new[] { BranchRecord("a", "paddling", "canoeing", 4, "awarded") };
            var columns = OasSummaryLayout.BranchColumns(Tree, records);
            var report = OasSummaryBuilder.Build(new[] { Scout("a", "Amy", "", "Eagles"), Scout("b", "Ben", "", "Wombats") }, records, "scout", true, false, columns, true);
            Assert.AreEqual(2, report.Sheets.Count);
            Assert.IsTrue(report.Sheets.SelectMany(sheet => sheet.Rows).All(r => r.Cells.Count == report.Columns.Count));
            Assert.IsTrue(report.Columns.Any(c => c.Branch == "canoeing"), "Amy's branch is kept for Ben's sheet too");
        }

        [TestMethod]
        public void TheHeadingsOverBranchColumnsRunTogetherByGroupAndSkill()
        {
            var columns = OasSummaryLayout.BranchColumns(Tree, new Result[0]);
            Assert.IsTrue(OasSummaryLayout.HasBranchHeadings(columns));
            Assert.IsFalse(OasSummaryLayout.HasBranchHeadings(OasSummaryLayout.SkillColumns()));
            var streams = OasSummaryLayout.StreamSpans(columns);
            Assert.AreEqual(9, streams.Count);
            Assert.AreEqual(3, streams.Single(s => s.Heading == "Paddling").Span);
            Assert.AreEqual(columns.Count, streams.Sum(s => s.Span));
            var groups = OasSummaryLayout.GroupSpans(columns);
            CollectionAssert.AreEqual(new[] { "CORE", "LAND", "WATER" }, groups.Select(g => g.Heading).ToArray());
            Assert.AreEqual(columns.Count, groups.Sum(g => g.Span));
        }

        [TestMethod]
        public void TheWorkbookHasARowOfBranchNamesWhenSplitAndTheDataMovesDownOne()
        {
            var records = new[] { BranchRecord("a", "paddling", "canoeing", 5, "awarded") };
            var report = OasSummaryBuilder.Build(new[] { Scout("a", "Amy") }, records, "scout", false, false, OasSummaryLayout.BranchColumns(Tree, records), true);
            report.UnitName = "Scout Unit"; report.Section = "scout"; report.GeneratedAt = new DateTime(2026, 11, 25);
            var sheet = new ReportService().GenerateOasSummaryWorkbook(report, "Group", "scout", "Scout Unit").Worksheets[0];
            Assert.AreEqual("CORE", sheet.Range[4, 2].Text);
            Assert.AreEqual("Bushcraft", sheet.Range[5, 2].Text);
            Assert.AreEqual("Main", sheet.Range[6, 2].Text);
            Assert.AreEqual("Awarded", sheet.Range[7, 2].Text);
            Assert.AreEqual("Amy", sheet.Range[8, 1].Text);
            var canoeing = 2 + 2 * report.Columns.FindIndex(c => c.Branch == "canoeing");
            Assert.AreEqual("Canoeing", sheet.Range[6, canoeing].Text);
            Assert.AreEqual(5, sheet.Range[8, canoeing].Number);
        }

        [TestMethod]
        public void TheWorkbookKeepsTheNamesAndTheHeadingRowsInViewWhenScrolled()
        {
            var plain = new ReportService().GenerateOasSummaryWorkbook(ReportOf(new OasSummarySheet { Rows = { RowWith("Amy", 0, 1) } }), "Group", "scout", "Scout Unit").Worksheets[0];
            Assert.IsTrue(plain.IsFreezePanes);
            var records = new[] { BranchRecord("a", "paddling", "canoeing", 5, "awarded") };
            var split = OasSummaryBuilder.Build(new[] { Scout("a", "Amy") }, records, "scout", false, false, OasSummaryLayout.BranchColumns(Tree, records), true);
            Assert.IsTrue(new ReportService().GenerateOasSummaryWorkbook(split, "Group", "scout", "Scout Unit").Worksheets[0].IsFreezePanes);
        }
    }
}
