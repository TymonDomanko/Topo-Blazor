using Topo.Model.Milestone;
using Topo.Services;

namespace TopoReportFunctionTest
{
    [TestClass]
    public class MilestoneProgressTests
    {
        private static MilestoneProgressMember Scout(string id, string first, string last = "", string patrol = "") =>
            new MilestoneProgressMember { MemberId = id, FirstName = first, LastName = last, Patrol = patrol };

        private static MilestoneProgressRecord Record(string member, int stage, string status = "in_progress", string requirement = "", int community = 0, int outdoors = 0, int creative = 0, int growth = 0, int leads = 0) =>
            new MilestoneProgressRecord
            {
                Id = member + stage, MemberId = member, Section = "scout", Stage = stage, Status = status, RequirementStatus = requirement,
                Participant = new MilestoneAreaCounts { Community = community, Outdoors = outdoors, Creative = creative, PersonalGrowth = growth },
                Leader = new MilestoneAreaCounts { Community = leads }
            };

        private static MilestoneProgressReport Build(IEnumerable<MilestoneProgressMember> members, IEnumerable<MilestoneProgressRecord> records, int stage = 1, bool onlyReady = true, Dictionary<string, MilestoneForm>? forms = null) =>
            MilestoneProgressBuilder.Build(new MilestoneProgressInput
            {
                UnitName = "Scout Unit", Section = "scout", Stage = stage, OnlyReady = onlyReady, Members = members.ToList(), Records = records.ToList(),
                Forms = forms ?? new Dictionary<string, MilestoneForm>(), GeneratedAt = new DateTime(2026, 11, 25)
            });

        [TestMethod]
        public void TheTargetsAreWhatEachMilestoneAsksFor()
        {
            Assert.AreEqual(6, MilestoneTargets.Participates(1));
            Assert.AreEqual(5, MilestoneTargets.Participates(2));
            Assert.AreEqual(4, MilestoneTargets.Participates(3));
            Assert.AreEqual(2, MilestoneTargets.Leads(1));
            Assert.AreEqual(4, MilestoneTargets.Leads(2));
            Assert.AreEqual(6, MilestoneTargets.Leads(3));
        }

        [TestMethod]
        public void ReadyMeansTheRequirementsAreInAndTheMilestoneIsNotAwardedYet()
        {
            var records = new[]
            {
                Record("a", 1, "in_progress", "complete"), Record("b", 1, "in_progress", "incomplete"), Record("c", 1, "awarded", "complete"),
                Record("d", 1, "approved", "complete"), Record("e", 1, "pending_review", "complete"), Record("f", 1, "draft_review", "complete")
            };
            var ready = Build(new[] { Scout("a", "Amy"), Scout("b", "Ben"), Scout("c", "Cat"), Scout("d", "Dan"), Scout("e", "Eve"), Scout("f", "Fay") }, records);
            CollectionAssert.AreEqual(new[] { "Amy", "Eve", "Fay" }, ready.Pages.Select(p => p.Name).ToArray());
            Assert.AreEqual(6, ready.ScoutsWithTheMilestone);
        }

        [TestMethod]
        public void WithoutTheOptionEveryScoutWithTheMilestoneGetsAPage()
        {
            var records = new[] { Record("a", 1, "in_progress", "complete"), Record("b", 1, "in_progress", "incomplete"), Record("c", 2) };
            var all = Build(new[] { Scout("a", "Amy"), Scout("b", "Ben"), Scout("c", "Cat"), Scout("d", "Dan") }, records, 1, false);
            CollectionAssert.AreEqual(new[] { "Amy", "Ben" }, all.Pages.Select(p => p.Name).ToArray(), "Cat has no stage 1 and Dan no record");
        }

        [TestMethod]
        public void WhenTerrainGivesNoVerdictTheCountsDecideReadiness()
        {
            var enough = Record("a", 1, "in_progress", "", 6, 6, 6, 6, 2);
            var short1 = Record("b", 1, "in_progress", "", 6, 6, 6, 5, 2);
            var shortLeads = Record("c", 1, "in_progress", "", 6, 6, 6, 6, 1);
            Assert.IsTrue(MilestoneProgressBuilder.IsReady(enough));
            Assert.IsFalse(MilestoneProgressBuilder.IsReady(short1));
            Assert.IsFalse(MilestoneProgressBuilder.IsReady(shortLeads));
            Assert.IsTrue(MilestoneProgressBuilder.IsReady(Record("d", 3, "in_progress", "", 4, 5, 4, 7, 6)), "milestone 3 asks 4 and 6");
        }

        [TestMethod]
        public void AssistsCountAsLeads()
        {
            var record = Record("a", 1, "in_progress", "", 6, 6, 6, 6, 1);
            record.Assistant = new MilestoneAreaCounts { Creative = 1 };
            Assert.AreEqual(2, MilestoneProgressBuilder.Leads(record));
            Assert.IsTrue(MilestoneProgressBuilder.IsReady(record));
        }

        [TestMethod]
        public void AScoutWhoSkippedTheMilestoneHasNoPage()
        {
            var records = new[] { Record("a", 1, "not_required", "complete") };
            Assert.AreEqual(0, Build(new[] { Scout("a", "Amy") }, records, 1, false).Pages.Count);
        }

        [TestMethod]
        public void WhenThereAreTwoRecordsForAStageTheAwardedOneIsUsedThenTheLatest()
        {
            var old = Record("a", 1, "in_progress"); old.StatusUpdated = new DateTime(2026, 1, 1);
            var recent = Record("a", 1, "in_progress"); recent.StatusUpdated = new DateTime(2026, 6, 1); recent.Id = "recent";
            var awarded = Record("a", 1, "awarded"); awarded.StatusUpdated = new DateTime(2025, 1, 1); awarded.Id = "awarded";
            Assert.AreEqual("recent", MilestoneProgressBuilder.RecordFor(new[] { old, recent }, "a", "scout", 1)!.Id);
            Assert.AreEqual("awarded", MilestoneProgressBuilder.RecordFor(new[] { old, recent, awarded }, "a", "scout", 1)!.Id);
            Assert.IsNull(MilestoneProgressBuilder.RecordFor(new[] { old }, "a", "cub", 1), "another section's");
        }

        [TestMethod]
        public void APageShowsEachAreaAgainstItsTargetAndTheLeads()
        {
            var page = Build(new[] { Scout("a", "Amy") }, new[] { Record("a", 2, "in_progress", "incomplete", 5, 3, 6, 4, 2) }, 2, false).Pages.Single();
            CollectionAssert.AreEqual(new[] { "Community", "Outdoors", "Creative", "Personal growth" }, page.Areas.Select(a => a.Area).ToArray());
            CollectionAssert.AreEqual(new[] { 5, 3, 6, 4 }, page.Areas.Select(a => a.Count).ToArray());
            Assert.IsTrue(page.Areas.All(a => a.Target == 5));
            CollectionAssert.AreEqual(new[] { true, false, true, false }, page.Areas.Select(a => a.Met).ToArray());
            Assert.AreEqual(2, page.Leads.Count);
            Assert.AreEqual(4, page.Leads.Target);
            Assert.IsFalse(page.Leads.Met);
            Assert.AreEqual("In progress", page.Status);
        }

        [TestMethod]
        public void StatusIsPutInWords()
        {
            Assert.AreEqual("Awarded", MilestoneProgressBuilder.StatusText(Record("a", 1, "awarded")));
            Assert.AreEqual("Approved, waiting to be presented", MilestoneProgressBuilder.StatusText(Record("a", 1, "approved")));
            Assert.AreEqual("In review", MilestoneProgressBuilder.StatusText(Record("a", 1, "pending_review")));
            Assert.AreEqual("Review drafted", MilestoneProgressBuilder.StatusText(Record("a", 1, "draft_review", "incomplete")));
            Assert.AreEqual("Ready", MilestoneProgressBuilder.StatusText(Record("a", 1, "in_progress", "complete")));
        }

        [TestMethod]
        public void EventsAreListedByDateWithTheirAreaAndHowTheyCounted()
        {
            var record = Record("a", 1, "in_progress", "complete");
            record.Events.Add(new MilestoneEventCredit { Credit = "leader", Area = "outdoors", EventId = "e2", EventName = "Bush camp", Date = new DateTime(2026, 5, 2) });
            record.Events.Add(new MilestoneEventCredit { Credit = "participant", Area = "personal_growth", EventId = "e1", EventName = "Pizza night", Date = new DateTime(2026, 3, 1) });
            var events = Build(new[] { Scout("a", "Amy") }, new[] { record }).Pages.Single().Events;
            CollectionAssert.AreEqual(new[] { "Pizza night", "Bush camp" }, events.Select(e => e.Event).ToArray());
            CollectionAssert.AreEqual(new[] { "Personal growth", "Outdoors" }, events.Select(e => e.Area).ToArray());
            CollectionAssert.AreEqual(new[] { "Participated", "Led" }, events.Select(e => e.Credit).ToArray());
        }

        private static MilestoneForm Form3() => new MilestoneForm
        {
            Questions =
            {
                new MilestoneFormQuestion { Id = "favourite_adventure", Label = "What was your favourite adventure?" },
                new MilestoneFormQuestion { Id = "best_challenge", Label = "What was the best Challenge?" },
                new MilestoneFormQuestion { Id = "spices_selection", Label = "Which SPICES objectives?", IsChoice = true,
                    Choices = { new MilestoneChoice { Id = "social", Title = "Social" }, new MilestoneChoice { Id = "physical", Title = "Physical" }, new MilestoneChoice { Id = "character", Title = "Character" } } }
            }
        };

        private static Dictionary<string, MilestoneForm> Forms() => new Dictionary<string, MilestoneForm>
        {
            ["milestone/scout/review/1|3"] = Form3(),
            [MilestoneProgressBuilder.LatestKey("scout", 1)] = Form3()
        };

        [TestMethod]
        public void TheFormShowsEveryQuestionInOrderFilledInWhereAnsweredAndBlankWhereNot()
        {
            var record = Record("a", 1, "pending_review", "complete");
            record.Template = "milestone/scout/review/1"; record.Version = 3;
            record.Answers["spices_selection"] = "social, character";
            record.Answers["favourite_adventure"] = "The kayak day.";
            record.Answers["best_challenge"] = "  ";
            var form = Build(new[] { Scout("a", "Amy") }, new[] { record }, 1, true, Forms()).Pages.Single().Form;
            CollectionAssert.AreEqual(new[] { "What was your favourite adventure?", "What was the best Challenge?", "Which SPICES objectives?" }, form.Select(r => r.Question).ToArray());
            Assert.AreEqual("The kayak day.", form[0].Answer);
            Assert.IsFalse(form[0].Blank);
            Assert.IsTrue(form[1].Blank, "written as spaces is not an answer");
            Assert.IsTrue(form[2].IsChoice);
            CollectionAssert.AreEqual(new[] { true, false, true }, form[2].Choices.Select(c => c.Chosen).ToArray());
            CollectionAssert.AreEqual(new[] { "Social", "Physical", "Character" }, form[2].Choices.Select(c => c.Title).ToArray());
            Assert.IsFalse(form[2].Blank);
        }

        [TestMethod]
        public void AScoutWhoHasWrittenNothingIsGivenTheCurrentFormBlankToFillIn()
        {
            var record = Record("a", 1, "in_progress", "complete");
            var form = Build(new[] { Scout("a", "Amy") }, new[] { record }, 1, true, Forms()).Pages.Single().Form;
            Assert.AreEqual(3, form.Count);
            Assert.IsTrue(form.All(e => e.Blank));
            Assert.IsTrue(form[2].Choices.All(c => !c.Chosen), "the options are there to tick");
        }

        [TestMethod]
        public void AnAnswerOnAnOlderFormIsShownWithThatFormsWording()
        {
            var forms = Forms();
            forms["milestone/scout/review/1|1"] = new MilestoneForm { Questions = { new MilestoneFormQuestion { Id = "favourite_adventure", Label = "Old wording of the question" } } };
            var record = Record("a", 1, "pending_review", "complete");
            record.Template = "milestone/scout/review/1"; record.Version = 1;
            record.Answers["favourite_adventure"] = "Camp.";
            var form = Build(new[] { Scout("a", "Amy") }, new[] { record }, 1, true, forms).Pages.Single().Form;
            Assert.AreEqual(1, form.Count, "that version only had the one question");
            Assert.AreEqual("Old wording of the question", form[0].Question);
            Assert.AreEqual("Camp.", form[0].Answer);
        }

        [TestMethod]
        public void WithNoKnownFormTheAnswersAreStillShownUnderTheirOwnNamesAndHousekeepingIsLeftOut()
        {
            var record = Record("a", 1, "pending_review", "complete");
            record.Answers["favourite_adventure"] = "Camp.";
            record.Answers["skipped_by"] = "someone";
            record.Answers["supporting_files"] = "abc";
            var form = Build(new[] { Scout("a", "Amy") }, new[] { record }).Pages.Single().Form;
            Assert.AreEqual(1, form.Count);
            Assert.AreEqual("favourite adventure", form[0].Question);
        }

        [TestMethod]
        public void TheFormParserReadsTextQuestionsAndOptionsAndLeavesOutTheUploaderAndTheLink()
        {
            var json = @"{ ""template"": ""milestone/scout/review/1"", ""version"": 3, ""document"": [ { ""input_groups"": [ { ""inputs"": [
                { ""id"": ""favourite_adventure"", ""type"": ""text_input"", ""label"": ""What was your favourite adventure?"" },
                { ""id"": ""spices_selection"", ""type"": ""single_tab_selector"", ""multi_select"": true, ""label"": ""Which SPICES objectives? *"", ""selections"": [ { ""id"": ""social"", ""title"": ""Social"" }, { ""id"": ""physical"", ""title"": ""Physical"" } ] },
                { ""id"": ""by_tab"", ""type"": ""multi_tab_selector"", ""label"": ""Tabbed"", ""tabs"": [ { ""title"": ""One"", ""selections"": [ { ""id"": ""a"", ""title"": ""Option A"" } ] } ] },
                { ""id"": ""spices_text_link"", ""type"": ""text_end_link"", ""label"": ""The SPICES i-statements may help"" },
                { ""id"": ""supporting_files"", ""type"": ""file_uploader"", ""label"": ""Upload supporting files"" } ] } ] } ] }";
            var form = MilestoneFormParser.Parse(json);
            CollectionAssert.AreEqual(new[] { "favourite_adventure", "spices_selection", "by_tab" }, form.Questions.Select(q => q.Id).ToArray());
            Assert.IsFalse(form.Questions[0].IsChoice);
            Assert.IsTrue(form.Questions[1].IsChoice);
            Assert.AreEqual("Which SPICES objectives?", form.Questions[1].Label);
            CollectionAssert.AreEqual(new[] { "Social", "Physical" }, form.Questions[1].Choices.Select(c => c.Title).ToArray());
            CollectionAssert.AreEqual(new[] { "a" }, form.Questions[2].Choices.Select(c => c.Id).ToArray());
            Assert.AreEqual(0, MilestoneFormParser.Parse("not json").Questions.Count);
        }

        [TestMethod]
        public void NotesOnEventsAreNamedAfterTheEvent()
        {
            var record = Record("a", 1, "pending_review", "complete");
            record.Events.Add(new MilestoneEventCredit { Credit = "leader", Area = "outdoors", EventId = "ev-1", EventName = "Bush camp", Date = new DateTime(2026, 5, 2) });
            record.Answers["lead_review_ev-1"] = "I ran the cooking.";
            record.Answers["assist_review_ev-9"] = "I helped.";
            record.Answers["lead_review_ev-2"] = "";
            var notes = Build(new[] { Scout("a", "Amy") }, new[] { record }).Pages.Single().EventReviews;
            CollectionAssert.AreEqual(new[] { "Assist review: an event", "Lead review: Bush camp" }, notes.Select(n => n.Question).ToArray());
            Assert.AreEqual("I ran the cooking.", notes.Single(n => n.Question.StartsWith("Lead review")).Answer);
        }

        [TestMethod]
        public void ScoutsKeepTheOrderTheUnitIsListedInAndAreNamedLikeTheOtherReports()
        {
            var records = new[] { Record("1", 1, "in_progress", "complete"), Record("2", 1, "in_progress", "complete"), Record("3", 1, "in_progress", "complete") };
            var pages = Build(new[] { Scout("1", "Zoe", "Zed", "Wombats"), Scout("2", "Amy", "Ant", "Wombats"), Scout("3", "Amy", "Bee", "Eagles") }, records).Pages;
            CollectionAssert.AreEqual(new[] { "Zoe", "Amy A", "Amy B" }, pages.Select(p => p.Name).ToArray());
            Assert.AreEqual("Wombats", pages[0].Patrol);
        }

        [TestMethod]
        public void TheParserReadsCountsEventsAndAnswersFromTerrainsJson()
        {
            var json = @"{ ""results"": [ {
                ""id"": ""r1"", ""member_id"": ""m1"", ""section"": ""Scout"", ""type"": ""milestone"", ""status"": ""pending_review"", ""milestone_requirement_status"": ""complete"",
                ""status_updated"": ""2026-10-01T02:00:00Z"", ""template"": ""milestone/scout/review/1"", ""version"": 3,
                ""achievement_meta"": { ""stage"": 1 },
                ""answers"": { ""favourite_adventure"": ""Camp"", ""spices_selection"": [""social"", ""character""], ""flag"": true },
                ""event_count"": { ""participant"": { ""community"": 6, ""outdoors"": 6.0, ""creative"": 6, ""personal_growth"": 5 }, ""assistant"": { ""creative"": 1 }, ""leader"": { ""outdoors"": 1 } },
                ""event_log"": [ { ""credit_type"": ""leader"", ""challenge_area"": ""outdoors"", ""event_id"": ""e1"", ""event_name"": ""Bush camp"", ""event_start_datetime"": ""2026-05-02T00:00:00Z"" } ]
            } ] }";
            var record = MilestoneProgressParser.Parse(json).Single();
            Assert.AreEqual("m1", record.MemberId);
            Assert.AreEqual("scout", record.Section);
            Assert.AreEqual(1, record.Stage);
            Assert.AreEqual("milestone/scout/review/1|3", record.FormKey);
            Assert.AreEqual("social, character", record.Answers["spices_selection"]);
            Assert.AreEqual("true", record.Answers["flag"]);
            Assert.AreEqual(5, record.Participant.PersonalGrowth);
            Assert.AreEqual(6, record.Participant.Outdoors);
            Assert.AreEqual(2, MilestoneProgressBuilder.Leads(record));
            Assert.AreEqual("Bush camp", record.Events.Single().EventName);
        }

        [TestMethod]
        public void AnImportedRecordHasNoFormAndTheParserSurvivesBadJson()
        {
            var imported = MilestoneProgressParser.Parse(@"{ ""results"": [ { ""id"": ""r"", ""member_id"": ""m"", ""status"": ""awarded"", ""achievement_meta"": { ""stage"": 2 } } ] }").Single();
            Assert.AreEqual("", imported.FormKey);
            Assert.AreEqual(0, MilestoneProgressParser.Parse("not json").Count);
            Assert.AreEqual(0, MilestoneProgressParser.Parse("{}").Count);
        }

        private static MilestoneProgressReport ReportWith(params MilestoneScoutPage[] pages) =>
            new MilestoneProgressReport { UnitName = "Scout Unit", Section = "scout", Stage = 1, GeneratedAt = new DateTime(2026, 11, 25), OnlyReady = true, Pages = pages.ToList() };

        private static MilestoneScoutPage Page(string name, string patrol = "") =>
            new MilestoneScoutPage
            {
                MemberId = name, Name = name, Patrol = patrol, Stage = 1, Status = "Ready", Ready = true,
                Areas = { new MilestoneAreaProgress { Area = "Community", Count = 6, Target = 6 }, new MilestoneAreaProgress { Area = "Outdoors", Count = 4, Target = 6 },
                          new MilestoneAreaProgress { Area = "Creative", Count = 6, Target = 6 }, new MilestoneAreaProgress { Area = "Personal growth", Count = 6, Target = 6 } },
                Leads = new MilestoneAreaProgress { Area = "Leads", Count = 2, Target = 2 },
                Events = { new MilestoneEventLine { Date = new DateTime(2026, 3, 1), Event = "Pizza night", Area = "Community", Credit = "Participated" } },
                Form = { new MilestoneFormEntry { Question = "What was your favourite adventure?", Answer = "The kayak day." } }
            };

        private static string Cell(Syncfusion.XlsIO.IWorksheet sheet, int row, int column) => sheet.Range[row, column].Text ?? "";

        [TestMethod]
        public void TheWorkbookHasASheetForEachScoutNamedAfterThem()
        {
            var workbook = new ReportService().GenerateMilestoneProgressWorkbook(ReportWith(Page("Amy"), Page("Ben")), "Group", "scout", "Scout Unit");
            Assert.AreEqual(2, workbook.Worksheets.Count);
            Assert.AreEqual("Amy", workbook.Worksheets[0].Name);
            Assert.AreEqual("Ben", workbook.Worksheets[1].Name);
        }

        [TestMethod]
        public void SheetNamesAreKeptWithinWhatASpreadsheetAllowsAndDifferentFromEachOther()
        {
            var long1 = Page("A very long name that goes on and on and on [x]");
            var long2 = Page("A very long name that goes on and on and on [y]");
            var workbook = new ReportService().GenerateMilestoneProgressWorkbook(ReportWith(Page("Amy"), Page("Amy"), long1, long2), "Group", "scout", "Scout Unit");
            var names = workbook.Worksheets.Select(w => w.Name).ToList();
            Assert.AreEqual(4, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.IsTrue(names.All(n => n.Length <= 31 && n.IndexOfAny(new[] { ':', '\\', '/', '?', '*', '[', ']' }) < 0), string.Join("|", names));
        }

        [TestMethod]
        public void AScoutsSheetHasTheirTitleStatusCountsEventsAndReview()
        {
            var sheet = new ReportService().GenerateMilestoneProgressWorkbook(ReportWith(Page("Amy", "Eagles")), "Group", "scout", "Scout Unit").Worksheets[0];
            Assert.AreEqual("Scout Unit", Cell(sheet, 2, 2));
            StringAssert.Contains(Cell(sheet, 3, 2), "Milestone 1: Amy (Eagles)");
            Assert.AreEqual("Status", Cell(sheet, 5, 1));
            Assert.AreEqual("Ready", Cell(sheet, 5, 2));
            Assert.AreEqual("Counted so far", Cell(sheet, 7, 1));
            Assert.AreEqual("Community Participates", Cell(sheet, 8, 1));
            Assert.AreEqual(6, sheet.Range[8, 2].Number);
            Assert.AreEqual(6, sheet.Range[8, 3].Number);
            Assert.AreEqual("Outdoors Participates", Cell(sheet, 9, 1));
            Assert.AreEqual(4, sheet.Range[9, 2].Number);
            Assert.AreEqual("Leads (any area)", Cell(sheet, 12, 1));
            Assert.AreEqual("Date", Cell(sheet, 14, 1));
            Assert.AreEqual("Pizza night", Cell(sheet, 15, 2));
            Assert.AreEqual("Participated", Cell(sheet, 15, 4));
            Assert.AreEqual("Milestone 1 review: Amy", Cell(sheet, 17, 1));
            Assert.AreEqual("What was your favourite adventure?", Cell(sheet, 19, 1));
            Assert.AreEqual("The kayak day.", Cell(sheet, 20, 1));
        }

        [TestMethod]
        public void ARequirementThatIsMetIsShadedAndOneThatIsNotIsNot()
        {
            var sheet = new ReportService().GenerateMilestoneProgressWorkbook(ReportWith(Page("Amy")), "Group", "scout", "Scout Unit").Worksheets[0];
            Assert.AreNotEqual(sheet.Range[8, 1].CellStyle.Color, sheet.Range[9, 1].CellStyle.Color, "Community is met and Outdoors is not");
        }

        [TestMethod]
        public void AQuestionNotAnsweredYetIsABoxTallEnoughToWriteIn()
        {
            var blank = Page("Amy");
            blank.Form[0].Answer = "";
            blank.Form[0].Blank = true;
            var sheet = new ReportService().GenerateMilestoneProgressWorkbook(ReportWith(blank), "Group", "scout", "Scout Unit").Worksheets[0];
            Assert.AreEqual("What was your favourite adventure?", Cell(sheet, 19, 1));
            Assert.AreEqual("", Cell(sheet, 20, 1));
            Assert.IsTrue(sheet.GetRowHeight(20) >= 70, "room for handwriting");
        }

        [TestMethod]
        public void AQuestionOfOptionsListsThemWithTheOnesPickedMarked()
        {
            var page = Page("Amy");
            page.Form.Add(new MilestoneFormEntry { Question = "Which SPICES objectives?", IsChoice = true, Choices = { new MilestoneChoiceState { Title = "Social", Chosen = true }, new MilestoneChoiceState { Title = "Physical" } } });
            var sheet = new ReportService().GenerateMilestoneProgressWorkbook(ReportWith(page), "Group", "scout", "Scout Unit").Worksheets[0];
            Assert.AreEqual("Which SPICES objectives?", Cell(sheet, 22, 1));
            StringAssert.Contains(Cell(sheet, 23, 1), "[x] Social");
            StringAssert.Contains(Cell(sheet, 23, 1), "[ ] Physical");
        }

        [TestMethod]
        public void TheFormStartsOnAPageOfItsOwn()
        {
            var sheet = new ReportService().GenerateMilestoneProgressWorkbook(ReportWith(Page("Amy")), "Group", "scout", "Scout Unit").Worksheets[0];
            Assert.AreEqual(1, sheet.HPageBreaks.Count);
            Assert.AreEqual(17, sheet.HPageBreaks[0].Location.Row);
        }

        [TestMethod]
        public void LongAnswersGetARowTallEnoughForTheirLines()
        {
            var page = Page("Amy");
            page.Form[0].Answer = new string('x', 400);
            var sheet = new ReportService().GenerateMilestoneProgressWorkbook(ReportWith(page), "Group", "scout", "Scout Unit").Worksheets[0];
            Assert.IsTrue(sheet.GetRowHeight(20) >= 15 * 4, "400 characters across about 86 take five lines");
        }

        [TestMethod]
        public void NoPagesStillMakeASheetThatSaysWhy()
        {
            var report = ReportWith();
            var sheet = new ReportService().GenerateMilestoneProgressWorkbook(report, "Group", "scout", "Scout Unit").Worksheets[0];
            StringAssert.Contains(Cell(sheet, 5, 1), "No Scouts are ready for Milestone 1");
        }

        [TestMethod]
        public void AllMilestonesMakesPagesForEachOfTheThreeInOrderOfMilestone()
        {
            var records = new[]
            {
                Record("a", 1, "in_progress", "complete"), Record("b", 1, "awarded", "complete"), Record("a", 2, "in_progress", "incomplete"), Record("b", 2, "in_progress", "complete"),
                Record("b", 3, "in_progress", "complete"), Record("a", 3, "in_progress", "complete")
            };
            var members = new[] { Scout("a", "Amy"), Scout("b", "Ben") };
            var report = Build(members, records, MilestoneProgressBuilder.AllMilestones, true);
            CollectionAssert.AreEqual(new[] { "Amy 1", "Ben 2", "Amy 3", "Ben 3" }, report.Pages.Select(p => p.Name + " " + p.Stage).ToArray());
            Assert.AreEqual(6, report.ScoutsWithTheMilestone, "a Scout with three milestones counts three");
            Assert.AreEqual(MilestoneProgressBuilder.AllMilestones, report.Stage);
        }

        [TestMethod]
        public void AllMilestonesWithoutTheOptionShowsEveryScoutsEveryMilestone()
        {
            var records = new[] { Record("a", 1, "awarded"), Record("a", 2, "in_progress"), Record("b", 3, "in_progress") };
            var pages = Build(new[] { Scout("a", "Amy"), Scout("b", "Ben") }, records, MilestoneProgressBuilder.AllMilestones, false).Pages;
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, pages.Select(p => p.Stage).ToArray());
        }

        [TestMethod]
        public void APageKnowsItsMilestoneWhenOneIsChosen()
        {
            Assert.AreEqual(2, Build(new[] { Scout("a", "Amy") }, new[] { Record("a", 2, "in_progress", "complete") }, 2).Pages.Single().Stage);
        }

        [TestMethod]
        public void TheWorkbookForAllMilestonesNamesEachSheetWithItsMilestoneAndTitlesItSo()
        {
            var one = Page("Amy"); one.Stage = 1;
            var two = Page("Amy"); two.Stage = 2;
            var report = ReportWith(one, two);
            report.Stage = MilestoneProgressBuilder.AllMilestones;
            var workbook = new ReportService().GenerateMilestoneProgressWorkbook(report, "Group", "scout", "Scout Unit");
            Assert.AreEqual("Amy M1", workbook.Worksheets[0].Name);
            Assert.AreEqual("Amy M2", workbook.Worksheets[1].Name);
            StringAssert.Contains(Cell(workbook.Worksheets[1], 3, 2), "Milestone 2: Amy");
        }

        [TestMethod]
        public void TheEmptyWorkbookForAllMilestonesSaysNoMilestone()
        {
            var report = ReportWith();
            report.Stage = MilestoneProgressBuilder.AllMilestones;
            var sheet = new ReportService().GenerateMilestoneProgressWorkbook(report, "Group", "scout", "Scout Unit").Worksheets[0];
            StringAssert.Contains(Cell(sheet, 5, 1), "No Scouts are ready for any milestone");
        }
    }
}
