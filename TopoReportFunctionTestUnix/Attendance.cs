using Newtonsoft.Json;
using Topo.Model.Attendance;
using Topo.Model.Members;
using Topo.Model.ReportGeneration;
using Topo.Model.Program;
using Topo.Model.UnitReport;
using TopoReportFunction;

using System.Drawing;

namespace TopoReportFunctionTest
{
    [TestClass]
    public class AttendanceTests
    {
        private static readonly DateTime Now = new DateTime(2026, 10, 1, 12, 0, 0);

        private static MemberListModel Youth(string id, string first, string last, string patrol = "Lyrebird", string duty = "", int order = 3, bool council = false, int adult = 0) =>
            new MemberListModel { id = id, member_number = "N" + id, first_name = first, last_name = last, patrol_name = patrol, patrol_duty = duty, patrol_order = order, unit_council = council, isAdultLeader = adult };

        // who is the member numbers of those who attended
        private static UnitEventInput Event(string id, DateTime start, int hours = 2, int nights = 0, string area = "outdoors", string title = "Activity", string scope = "unit",
            string status = "concluded", bool hike = false, params string[] who) =>
            new UnitEventInput
            {
                Scope = scope,
                Event = new GetEventResultModel
                {
                    id = id, title = title, status = status, challenge_area = area, start_datetime = start, end_datetime = start.AddDays(nights).AddHours(hours),
                    attendance = new Attendance { attendee_members = who.Select(w => new Attendance_Members { id = "id" + w, member_number = w }).ToArray() },
                    achievement_pathway_logbook_data = new Achievement_Pathway_Logbook_Data { categories = hike ? new object[] { "walking_hike" } : new object[0] }
                }
            };

        // Wednesdays 2026-09-02, 09, 16, 23, 30
        private static DateTime Wed(int week) => new DateTime(2026, 9, 2, 19, 0, 0).AddDays(7 * week);

        private static AttendanceInput Input(params UnitEventInput[] events) => new AttendanceInput
        {
            UnitName = "Scout Unit", Section = "scout", From = new DateTime(2026, 9, 1), To = new DateTime(2026, 9, 30), AsOf = Now,
            Members = new List<MemberListModel>
            {
                Youth("1", "Ana", "Chen"), Youth("2", "Ben", "Lee"), Youth("3", "Cat", "Moss", "Platypus", "PL", 2), Youth("4", "Una", "Vale", "", "UL", 0), Youth("9", "Adult", "Leader", adult: 1)
            },
            Events = events.ToList()
        };

        [TestMethod]
        public void EventsAreSortedIntoKindsFromTheEventsThemselves()
        {
            var data = AttendanceBuilder.Analyse(Input(
                Event("a", Wed(0), who: new[] { "N1" }), Event("b", Wed(1), who: new[] { "N1" }), Event("c", Wed(2), who: new[] { "N1" }),
                Event("d", new DateTime(2026, 9, 5, 9, 0, 0), who: new[] { "N1" }),
                Event("e", new DateTime(2026, 9, 12, 9, 0, 0), nights: 2, who: new[] { "N1" }),
                Event("f", new DateTime(2026, 9, 13, 9, 0, 0), hike: true, who: new[] { "N1" }),
                Event("g", Wed(3), title: "Unit Council", who: new[] { "N1" })));

            Assert.AreEqual(DayOfWeek.Wednesday, data.MeetingDay);
            CollectionAssert.AreEqual(
                new[] { AttendanceKind.ScoutNight, AttendanceKind.ScoutNight, AttendanceKind.ScoutNight, AttendanceKind.Other, AttendanceKind.Camp, AttendanceKind.Hike, AttendanceKind.UnitCouncil },
                data.Events.OrderBy(e => e.Id).Select(e => e.Kind).ToList());
        }

        [TestMethod]
        public void AHikeOnTheMeetingNightIsThatWeeksScoutNight()
        {
            var data = AttendanceBuilder.Analyse(Input(
                Event("a", Wed(0), who: new[] { "N1" }), Event("b", Wed(1), who: new[] { "N1" }), Event("walk", Wed(2), hike: true, who: new[] { "N1" }),
                Event("hike", new DateTime(2026, 9, 13, 9, 0, 0), hike: true, who: new[] { "N1" })));

            Assert.AreEqual(AttendanceKind.ScoutNight, data.Events.Single(e => e.Id == "walk").Kind);
            Assert.AreEqual(AttendanceKind.Hike, data.Events.Single(e => e.Id == "hike").Kind);
        }

        [TestMethod]
        public void OnlyEventsThatHappenedForTheWholeUnitWithAttendanceAreCounted()
        {
            var data = AttendanceBuilder.Analyse(Input(
                Event("ok", Wed(0), who: new[] { "N1" }),
                Event("outside the period", new DateTime(2026, 8, 12, 19, 0, 0), who: new[] { "N1" }),
                Event("not yet", new DateTime(2026, 10, 7, 19, 0, 0), who: new[] { "N1" }),
                Event("cancelled", Wed(1), status: "cancelled", who: new[] { "N1" }),
                Event("patrol", Wed(2), scope: "patrol", who: new[] { "N1" }),
                Event("project", Wed(3), scope: "member", who: new[] { "N1" }),
                Event("nobody recorded", Wed(4)),
                Event("ok", Wed(0), who: new[] { "N1" })));

            Assert.AreEqual(1, data.Events.Count);
            Assert.AreEqual("ok", data.Events[0].Id);
            Assert.AreEqual(1, data.EventsLeftOut);
        }

        [TestMethod]
        public void TheReportCountsEventsAndSharesForEachYouthWithTheUnitLeaderAndPatrolLeaderTreatedAsYouth()
        {
            var data = AttendanceBuilder.Analyse(Input(
                Event("a", Wed(0), who: new[] { "N1", "N3", "N4" }), Event("b", Wed(1), who: new[] { "N1", "N3" }),
                Event("c", Wed(2), who: new[] { "N1" }), Event("d", Wed(3), who: new[] { "N1", "N2" })));
            var report = AttendanceLayout.Build(data, false, false);

            Assert.AreEqual("Wednesday", report.MeetingDay);
            CollectionAssert.AreEqual(new[] { "Last seen", "Last seen", "Scout nights (4)", "Scout nights (4)", "All events (4)", "All events (4)", "Challenge areas" }, report.Columns.Select(c => c.Group).ToList());
            var rows = report.Tables.Single().Rows;
            // the unit leader first, then by patrol, the patrol leader before the rest; the adult is not listed
            CollectionAssert.AreEqual(new[] { "Una", "Ana", "Ben", "Cat" }, rows.Select(r => r.Name).ToList());
            var ana = rows.Single(r => r.Name == "Ana");
            Assert.AreEqual("4", ana.Cells[2].Text);
            Assert.AreEqual("100%", ana.Cells[3].Text);
            Assert.AreEqual("50%", rows.Single(r => r.Name == "Cat").Cells[3].Text);
            Assert.AreEqual("25%", rows.Single(r => r.Name == "Una").Cells[3].Text);
            Assert.AreEqual("PL", rows.Single(r => r.Name == "Cat").Role);
        }

        [TestMethod]
        public void KindsWithNoEventsGetNoColumnsAndTheUnitCouncilIsOnlyForItsMembers()
        {
            var input = Input(Event("a", Wed(0), who: new[] { "N1", "N3" }), Event("b", Wed(1), title: "Unit Council", who: new[] { "N3" }), Event("c", Wed(2), who: new[] { "N1" }));
            input.Members.Single(m => m.id == "3").unit_council = true;
            var report = AttendanceLayout.Build(AttendanceBuilder.Analyse(input), false, true);

            CollectionAssert.AreEqual(new[] { "Last seen", "Scout nights (2)", "Unit councils (1)", "All events (3)", "Challenge areas" },
                report.Columns.Select(c => c.Group).Distinct().ToList());
            var council = report.Columns.FindIndex(c => c.Group == "Unit councils (1)" && !c.IsPercent);
            var rows = report.Tables.Single().Rows;
            Assert.AreEqual("1", rows.Single(r => r.Name == "Cat Moss").Cells[council].Text);
            Assert.AreEqual("", rows.Single(r => r.Name == "Ana Chen").Cells[council].Text);
            Assert.IsNull(rows.Single(r => r.Name == "Ana Chen").Cells[council].Value);
        }

        [TestMethod]
        public void FullNamesAndSplitByPatrolAreOptions()
        {
            var data = AttendanceBuilder.Analyse(Input(Event("a", Wed(0), who: new[] { "N1" }), Event("b", Wed(1), who: new[] { "N1", "N3" })));

            var split = AttendanceLayout.Build(data, true, true);
            CollectionAssert.AreEqual(new[] { "Unit", "Lyrebird", "Platypus" }, split.Tables.Select(t => t.Patrol).ToList());
            Assert.AreEqual("Cat Moss", split.Tables.Single(t => t.Patrol == "Platypus").Rows.Single().Name);

            var together = AttendanceLayout.Build(data, false, false);
            Assert.AreEqual(1, together.Tables.Count);
            Assert.AreEqual("", together.Tables[0].Patrol);
            CollectionAssert.AreEqual(new[] { "Una", "Ana", "Ben", "Cat" }, together.Tables[0].Rows.Select(r => r.Name).ToList());
        }

        [TestMethod]
        public void EachTableHasAnAverageAndTheColoursRunFromRedThroughYellowToGreen()
        {
            var input = Input(Event("a", Wed(0), who: new[] { "N1", "N2", "N3" }), Event("b", Wed(1), who: new[] { "N1", "N2" }), Event("c", Wed(2), who: new[] { "N1" }));
            input.Members = input.Members.Where(m => m.id is "1" or "2" or "3").ToList();
            var report = AttendanceLayout.Build(AttendanceBuilder.Analyse(input), false, false);
            var rows = report.Tables.Single().Rows;
            var percent = report.Columns.FindIndex(c => c.IsPercent);

            Assert.AreEqual("#63BE7B", rows.Single(r => r.Name == "Ana").Cells[percent].Colour);
            Assert.AreEqual("#FFEB84", rows.Single(r => r.Name == "Ben").Cells[percent].Colour);
            Assert.AreEqual("#F8696B", rows.Single(r => r.Name == "Cat").Cells[percent].Colour);
            // (100 + 67 + 33) / 3
            Assert.AreEqual("67%", report.Tables.Single().Average[percent].Text);
            Assert.AreEqual("", report.Tables.Single().Average[percent].Colour);
        }

        [TestMethod]
        public void AColumnWhereEveryoneIsTheSameIsNotColoured()
        {
            var data = AttendanceBuilder.Analyse(Input(Event("a", Wed(0), who: new[] { "N1", "N2", "N3", "N4" })));
            var report = AttendanceLayout.Build(data, false, false);

            Assert.IsTrue(report.Tables.Single().Rows.SelectMany(r => r.Cells).All(c => c.Colour == ""));
        }

        private static AttendanceReport SampleReport(bool split)
        {
            var input = Input(
                Event("a", Wed(0), who: new[] { "N1", "N2", "N3", "N4" }), Event("b", Wed(1), who: new[] { "N1", "N2", "N3" }), Event("c", Wed(2), area: "community", who: new[] { "N1", "N3" }),
                Event("d", Wed(3), who: new[] { "N1" }), Event("e", new DateTime(2026, 9, 5, 9, 0, 0), area: "creative", who: new[] { "N1", "N2" }),
                Event("f", new DateTime(2026, 9, 12, 9, 0, 0), nights: 2, area: "personal_growth", who: new[] { "N1", "N3" }),
                Event("g", new DateTime(2026, 9, 13, 9, 0, 0), hike: true, who: new[] { "N2" }), Event("h", Wed(3), title: "Unit Council", who: new[] { "N3", "N4" }));
            input.Members.Single(m => m.id == "3").unit_council = true;
            input.Members.Single(m => m.id == "4").unit_council = true;
            return AttendanceLayout.Build(AttendanceBuilder.Analyse(input), split, false);
        }

        [DataTestMethod]
        [DataRow(false, OutputType.PDF, "pdf")]
        [DataRow(false, OutputType.Excel, "xlsx")]
        [DataRow(true, OutputType.PDF, "pdf")]
        [DataRow(true, OutputType.Excel, "xlsx")]
        public void ReportsRenderAsPdfAndExcel(bool split, OutputType outputType, string extension)
        {
            SetEnvironment.SetSyncfusionLicense();
            var request = new ReportGenerationRequest
            {
                ReportType = ReportType.AttendanceAnalysis, GroupName = "Sample Group", Section = "scout", UnitName = "Scout Unit",
                OutputType = outputType, ReportData = JsonConvert.SerializeObject(SampleReport(split))
            };
            var proxyRequest = new Amazon.Lambda.APIGatewayEvents.APIGatewayProxyRequest { Body = JsonConvert.SerializeObject(request) };

            var result = new Function().FunctionHandler(proxyRequest, null);

            Assert.IsFalse(string.IsNullOrEmpty(result), $"{extension} came back empty");
            Directory.CreateDirectory("TestResults");
            File.WriteAllBytes($@"TestResults/Attendance_{(split ? "Patrols" : "Unit")}.{extension}", Convert.FromBase64String(result));
        }

        [TestMethod]
        public void TheWorkbookHasASheetForEachPatrolWithTheColumnsAndColoursOfThePage()
        {
            SetEnvironment.SetSyncfusionLicense();
            var report = SampleReport(true);
            var workbook = new Topo.Services.ReportService().GenerateAttendanceAnalysisWorkbook(report, "Sample Group", "scout");

            CollectionAssert.AreEqual(new[] { "Unit", "Lyrebird", "Platypus" }, workbook.Worksheets.Select(w => w.Name).ToList());
            var sheet = workbook.Worksheets["Lyrebird"];
            var texts = Enumerable.Range(1, sheet.UsedRange.LastRow).SelectMany(r => Enumerable.Range(1, sheet.UsedRange.LastColumn).Select(c => sheet.Range[r, c].Text)).ToList();
            foreach (var expected in new[] { "Attendance", "Scout Unit - Lyrebird", "Name", "Role", "Ana", "Ben", "Average" })
                CollectionAssert.Contains(texts, expected);
            Assert.IsTrue(texts.Any(t => t != null && t.StartsWith("Scout nights (")));
            // Ana came to every Scout night, the most in the column, so her cell is green
            var anaRow = Enumerable.Range(1, sheet.UsedRange.LastRow).Single(r => sheet.Range[r, 1].Text == "Ana");
            Assert.AreEqual(Color.FromArgb(0x63, 0xBE, 0x7B).ToArgb(), sheet.Range[anaRow, 6].CellStyle.Color.ToArgb());
        }

        [TestMethod]
        public void EventsBeforeAYouthJoinedTheSectionAreNotHeldAgainstThem()
        {
            var input = Input(Event("a", Wed(0), who: new[] { "N1" }), Event("b", Wed(1), who: new[] { "N1", "N2" }), Event("c", Wed(2), who: new[] { "N1", "N2" }), Event("d", Wed(3), who: new[] { "N1", "N2" }));
            // Ben was invested on the third night; Cat never came and was never invested, so every night counts for her
            input.InvestedOn["2"] = Wed(2).Date;
            var rows = AttendanceLayout.Build(AttendanceBuilder.Analyse(input), false, false).Tables.Single().Rows;
            var percent = AttendanceLayout.Build(AttendanceBuilder.Analyse(input), false, false).Columns.FindIndex(c => c.IsPercent);

            // Ben could have come to two nights and came to both, though he also came to the night before
            Assert.AreEqual("100%", rows.Single(r => r.Name == "Ben").Cells[percent].Text);
            Assert.AreEqual("100%", rows.Single(r => r.Name == "Ana").Cells[percent].Text);
            Assert.AreEqual("0%", rows.Single(r => r.Name == "Cat").Cells[percent].Text);
        }

        [TestMethod]
        public void AYouthWithNoInvestitureCountsFromTheFirstEventTheyCameTo()
        {
            var input = Input(Event("a", Wed(0), who: new[] { "N1" }), Event("b", Wed(1), who: new[] { "N1" }), Event("c", Wed(2), who: new[] { "N1", "N3" }), Event("d", Wed(3), who: new[] { "N1" }));
            var data = AttendanceBuilder.Analyse(input);
            var cat = input.Members.Single(m => m.id == "3");

            Assert.AreEqual(Wed(2).Date, data.JoinedOn(cat));
            Assert.AreEqual(2, data.PossibleFor(cat).Count);
            Assert.IsNull(data.JoinedOn(input.Members.Single(m => m.id == "2")));
        }

        [TestMethod]
        public void TheEarlierOfTheInvestitureAndTheFirstEventIsWhenTheyJoined()
        {
            var input = Input(Event("a", Wed(0), who: new[] { "N1", "N2" }), Event("b", Wed(1), who: new[] { "N1", "N2" }), Event("c", Wed(2), who: new[] { "N1", "N2" }));
            input.InvestedOn["2"] = Wed(2).Date;
            var data = AttendanceBuilder.Analyse(input);

            // Ben turned up on the first night, before he was invested
            Assert.AreEqual(Wed(0).Date, data.JoinedOn(input.Members.Single(m => m.id == "2")));
            // invested before ever turning up
            input.InvestedOn["3"] = Wed(1).Date;
            Assert.AreEqual(Wed(1).Date, AttendanceBuilder.Analyse(input).JoinedOn(input.Members.Single(m => m.id == "3")));
        }

        [TestMethod]
        public void TheInvestitureIsTheEarliestAwardedIntroductionToTheSection()
        {
            var intros = new Topo.Model.Progress.GetIntroductionResultsModel
            {
                results = new[]
                {
                    new Topo.Model.Progress.Result { section = "cub", status = "awarded", imported = new Topo.Model.Progress.Imported { date_awarded = "2024-02-10" } },
                    new Topo.Model.Progress.Result { section = "Scout", status = "awarded", imported = new Topo.Model.Progress.Imported { date_awarded = "2025-05-20" } },
                    new Topo.Model.Progress.Result { section = "scout", status = "awarded", imported = new Topo.Model.Progress.Imported { date_awarded = "2025-06-20" } },
                    new Topo.Model.Progress.Result { section = "scout", status = "pending", imported = new Topo.Model.Progress.Imported { date_awarded = "2025-01-01" } }
                }
            };

            Assert.AreEqual(new DateTime(2025, 5, 20), AttendanceBuilder.InvestedOn(intros, "scout"));
            Assert.IsNull(AttendanceBuilder.InvestedOn(intros, "venturer"));
        }

        // Ana came to every night, Ben to the first two, Cat to the first only, and Una to none
        private static AttendanceInput FiveNights() => Input(
            Event("a", Wed(0), who: new[] { "N1", "N2", "N3" }), Event("b", Wed(1), who: new[] { "N1", "N2" }), Event("c", Wed(2), who: new[] { "N1" }),
            Event("d", Wed(3), who: new[] { "N1" }), Event("e", Wed(4), who: new[] { "N1" }));

        [TestMethod]
        public void ScoutNightsSinceAYouthWasLastSeenAreCounted()
        {
            var input = FiveNights();
            var data = AttendanceBuilder.Analyse(input);
            (int? NightsSince, DateTime? LastSeen) Of(string id) => data.NotSeen(input.Members.Single(m => m.id == id));

            Assert.AreEqual(0, Of("1").NightsSince);
            Assert.AreEqual(((int?)3, (DateTime?)Wed(1).Date), Of("2"));
            Assert.AreEqual(((int?)4, (DateTime?)Wed(0).Date), Of("3"));
            // never came, and never invested, so there is no telling when they joined or how many nights they could have missed
            Assert.AreEqual(((int?)null, (DateTime?)null), Of("4"));
            Assert.AreEqual("", AttendanceData.NotSeenLevel(2));
            Assert.AreEqual("amber", AttendanceData.NotSeenLevel(3));
            Assert.AreEqual("red", AttendanceData.NotSeenLevel(4));
        }

        [TestMethod]
        public void ComingToAnythingCountsAsSeenNotJustAScoutNight()
        {
            // Cat missed the last three Scout nights but came to a camp after the second
            var input = FiveNights();
            input.Events.Add(Event("camp", new DateTime(2026, 9, 12, 9, 0, 0), nights: 1, who: new[] { "N3" }));
            var data = AttendanceBuilder.Analyse(input);
            var (nightsSince, lastSeen) = data.NotSeen(input.Members.Single(m => m.id == "3"));

            Assert.AreEqual(new DateTime(2026, 9, 12), lastSeen);
            // the Scout nights of 16, 23 and 30 September came after it
            Assert.AreEqual((int?)3, nightsSince);
        }

        [TestMethod]
        public void AYouthNotSeenInThePeriodIsFoundInEarlierEvents()
        {
            var input = FiveNights();
            // Una came to the middle of three August nights, none of the September ones
            input.EarlierEvents.Add(Event("e1", new DateTime(2026, 8, 5, 19, 0, 0), who: new[] { "N1" }));
            input.EarlierEvents.Add(Event("e2", new DateTime(2026, 8, 12, 19, 0, 0), who: new[] { "N1", "N4" }));
            input.EarlierEvents.Add(Event("e3", new DateTime(2026, 8, 19, 19, 0, 0), who: new[] { "N1" }));
            var data = AttendanceBuilder.Analyse(input);
            var una = input.Members.Single(m => m.id == "4");

            CollectionAssert.AreEqual(new[] { "4" }, data.NotSeenInPeriod().Select(m => m.id).ToList());
            Assert.AreEqual(3, data.Earlier.Count);
            Assert.IsTrue(data.Earlier.All(e => e.Kind == AttendanceKind.ScoutNight));
            // the 19 August night and the five in September have come since
            Assert.AreEqual(((int?)6, (DateTime?)new DateTime(2026, 8, 12)), data.NotSeen(una));
            // the period's own counts are unchanged by the earlier events
            Assert.AreEqual(5, data.Events.Count);
        }

        [TestMethod]
        public void EarlierEventsInThePeriodOrCancelledOrUnrecordedAreIgnored()
        {
            var input = FiveNights();
            input.EarlierEvents.Add(Event("in period", Wed(0), who: new[] { "N4" }));
            input.EarlierEvents.Add(Event("cancelled", new DateTime(2026, 8, 5, 19, 0, 0), status: "cancelled", who: new[] { "N4" }));
            input.EarlierEvents.Add(Event("nobody", new DateTime(2026, 8, 12, 19, 0, 0)));
            var data = AttendanceBuilder.Analyse(input);

            Assert.AreEqual(0, data.Earlier.Count);
            Assert.AreEqual(((int?)null, (DateTime?)null), data.NotSeen(input.Members.Single(m => m.id == "4")));
            Assert.AreEqual(0, AttendanceBuilder.Analyse(FiveNights()).Earlier.Count);
        }

        [TestMethod]
        public void OnlyNightsSinceTheYouthJoinedCount()
        {
            var input = FiveNights();
            // Una was invested on the fourth night, so only that and the last night could have been missed
            input.InvestedOn["4"] = Wed(3).Date;
            var data = AttendanceBuilder.Analyse(input);

            Assert.AreEqual((int?)2, data.NotSeen(input.Members.Single(m => m.id == "4")).NightsSince);
        }

        [TestMethod]
        public void TheReportFlagsWhoHasNotBeenSeenAndColoursTheirCells()
        {
            var report = AttendanceLayout.Build(AttendanceBuilder.Analyse(FiveNights()), false, false);

            // Una never came and was never invested, so there is no telling how long she has missed: shown as not seen, but not flagged
            CollectionAssert.AreEqual(new[] { "Cat", "Ben" }, report.NotSeen.Select(f => f.Name).ToList());
            CollectionAssert.AreEqual(new[] { "red", "amber" }, report.NotSeen.Select(f => f.Level).ToList());
            Assert.AreEqual("9 Sep 2026", report.NotSeen.Single(f => f.Name == "Ben").LastSeen);

            Assert.AreEqual("Date", report.Columns[0].Heading);
            Assert.AreEqual("Nights since", report.Columns[1].Heading);
            var rows = report.Tables.Single().Rows;
            Assert.AreEqual("#FFC000", rows.Single(r => r.Name == "Ben").Cells[1].Colour);
            Assert.AreEqual("#F8696B", rows.Single(r => r.Name == "Cat").Cells[1].Colour);
            Assert.AreEqual("", rows.Single(r => r.Name == "Ana").Cells[1].Colour);
            Assert.AreEqual("Not seen", rows.Single(r => r.Name == "Una").Cells[0].Text);
            Assert.AreEqual("", rows.Single(r => r.Name == "Una").Cells[1].Text);
            Assert.AreEqual("", rows.Single(r => r.Name == "Una").Cells[1].Colour);
            Assert.AreEqual("", report.Tables.Single().Average[1].Text);
            Assert.AreEqual(report.Columns.Count, report.Tables.Single().Average.Count);
        }

        [TestMethod]
        public void NobodyFlaggedMeansNoListAndNoScoutNightsMeansNoColumns()
        {
            var input = Input(Event("a", Wed(0), who: new[] { "N1", "N2", "N3", "N4" }), Event("b", Wed(1), who: new[] { "N1", "N2", "N3", "N4" }));
            var report = AttendanceLayout.Build(AttendanceBuilder.Analyse(input), false, false);
            Assert.AreEqual(0, report.NotSeen.Count);
            Assert.IsTrue(report.Columns.Any(c => c.Heading == "Nights since"));

            // two events on different days, one of them a camp: no Scout nights to miss
            var noNights = Input(Event("camp", new DateTime(2026, 9, 12, 9, 0, 0), nights: 2, who: new[] { "N1" }));
            var report2 = AttendanceLayout.Build(AttendanceBuilder.Analyse(noNights), false, false);
            Assert.AreEqual(0, report2.NotSeen.Count);
            Assert.IsFalse(report2.Columns.Any(c => c.Heading == "Nights since"));
        }

        [TestMethod]
        public void ThePrintedReportHasASheetForWhoHasNotBeenSeenOnlyWhenThereIsSomeone()
        {
            SetEnvironment.SetSyncfusionLicense();
            var flagged = new Topo.Services.ReportService().GenerateAttendanceAnalysisWorkbook(AttendanceLayout.Build(AttendanceBuilder.Analyse(FiveNights()), false, false), "Sample Group", "scout");
            CollectionAssert.AreEqual(new[] { "Attendance", "Not seen lately" }, flagged.Worksheets.Select(w => w.Name).ToList());
            var sheet = flagged.Worksheets["Not seen lately"];
            Assert.AreEqual("Cat", sheet.Range[7, 1].Text);

            var quiet = AttendanceLayout.Build(AttendanceBuilder.Analyse(Input(Event("a", Wed(0), who: new[] { "N1", "N2", "N3", "N4" }))), false, false);
            var workbook = new Topo.Services.ReportService().GenerateAttendanceAnalysisWorkbook(quiet, "Sample Group", "scout");
            CollectionAssert.AreEqual(new[] { "Attendance" }, workbook.Worksheets.Select(w => w.Name).ToList());
        }

        [TestMethod]
        public void TheInvestitureUsesTerrainsOwnAwardDateBeforeTheImportedOneOrWhenItLastChanged()
        {
            var updated = new DateTime(2026, 3, 20, 9, 30, 0);
            var onlyTerrains = new Topo.Model.Progress.GetIntroductionResultsModel { results = new[]
                { new Topo.Model.Progress.Result { section = "scout", status = "awarded", date_awarded = "2025-06-01", status_updated = updated } } };
            var both = new Topo.Model.Progress.GetIntroductionResultsModel { results = new[]
                { new Topo.Model.Progress.Result { section = "scout", status = "awarded", date_awarded = "2025-06-01", imported = new Topo.Model.Progress.Imported { date_awarded = "2024-01-15" }, status_updated = updated } } };
            var onlyUpdated = new Topo.Model.Progress.GetIntroductionResultsModel { results = new[]
                { new Topo.Model.Progress.Result { section = "scout", status = "awarded", status_updated = updated } } };

            Assert.AreEqual(new DateTime(2025, 6, 1), AttendanceBuilder.InvestedOn(onlyTerrains, "scout"));
            Assert.AreEqual(new DateTime(2025, 6, 1), AttendanceBuilder.InvestedOn(both, "scout"));
            Assert.AreEqual(updated.Date, AttendanceBuilder.InvestedOn(onlyUpdated, "scout"));
        }

        [TestMethod]
        public void AUnitWithNoYouthOrNoEventsGivesAnEmptyReport()
        {
            var noEvents = AttendanceLayout.Build(AttendanceBuilder.Analyse(Input()), false, false);
            Assert.AreEqual(0, noEvents.Tables.Count);
            Assert.AreEqual(0, noEvents.Columns.Count);

            var adultsOnly = Input(Event("a", Wed(0), who: new[] { "N9" }));
            adultsOnly.Members = new List<MemberListModel> { Youth("9", "Adult", "Leader", adult: 1) };
            var report = AttendanceLayout.Build(AttendanceBuilder.Analyse(adultsOnly), true, false);
            Assert.AreEqual(0, report.Tables.Count);
            Assert.AreEqual(1, report.EventsCounted);
        }
    }
}
