using Topo.Model.Approvals;
using Topo.Model.Program;

namespace TopoReportFunctionTest
{
    [TestClass]
    public class MassAwardTests
    {
        private static Result Pending(string submissionId, string memberId, string first, string last, string stage = "1", string type = "award", params string[] actionedBy)
        {
            return new Result
            {
                achievement = new Achievement { id = "ach-" + submissionId, type = "milestone", achievement_meta = new Topo.Model.Approvals.Achievement_Meta { stage = int.Parse(stage) } },
                submission = new Submission
                {
                    id = submissionId,
                    type = type,
                    date = new DateTime(2026, 11, 1).AddDays(submissionId.Length),
                    status = "pending",
                    actioned_by = actionedBy.Select(m => new Actioned_By { member_id = m, outcome = "approved" }).ToArray()
                },
                member = new Member { id = memberId, first_name = first, last_name = last }
            };
        }

        private static AwardRequestList List(params Result[] results) => AwardRequestList.FromPending(results, "leader");

        // ---- the list ----

        [TestMethod]
        public void OnlyAwardRequestsAreListed()
        {
            var list = List(Pending("s1", "m1", "Amy", "Brown"), Pending("s2", "m1", "Amy", "Brown", type: "review"), Pending("s3", "m2", "Ben", "Green", type: "approval"));
            Assert.AreEqual(1, list.Total);
            Assert.AreEqual("s1", list.People.Single().Awards.Single().Item.SubmissionId);
        }

        [TestMethod]
        public void PeopleAreSortedByLastNameThenFirstName()
        {
            var list = List(
                Pending("s1", "m1", "Zed", "Adams"),
                Pending("s2", "m2", "Amy", "Young"),
                Pending("s3", "m3", "Ben", "Young"),
                Pending("s4", "m4", "Cal", "brown"));
            CollectionAssert.AreEqual(new[] { "Zed Adams", "Cal brown", "Amy Young", "Ben Young" }, list.People.Select(p => p.Name).ToArray());
        }

        [TestMethod]
        public void AllOfAPersonsAwardsAreGroupedTogetherAndSortedByAchievement()
        {
            var list = List(
                Pending("s1", "m1", "Amy", "Brown", stage: "3"),
                Pending("s2", "m2", "Ben", "Green", stage: "1"),
                Pending("s3", "m1", "Amy", "Brown", stage: "1"));
            Assert.AreEqual(2, list.People.Count);
            CollectionAssert.AreEqual(new[] { "s3", "s1" }, list.People[0].Awards.Select(a => a.Item.SubmissionId).ToArray());
            Assert.AreEqual(3, list.Total);
        }

        [TestMethod]
        public void AwardsTheLeaderHasAlreadyActionedAreNotListed()
        {
            var list = List(
                Pending("s1", "m1", "Amy", "Brown", actionedBy: "leader"),
                Pending("s2", "m1", "Amy", "Brown", stage: "2", actionedBy: new[] { "someone-else", "leader" }),
                Pending("s3", "m2", "Ben", "Green", actionedBy: "someone-else"));
            CollectionAssert.AreEqual(new[] { "s3" }, list.People.SelectMany(p => p.Awards).Select(a => a.Item.SubmissionId).ToArray());
            Assert.AreEqual("Ben Green", list.People.Single().Name, "a person with nothing left is not listed");
            CollectionAssert.AreEquivalent(new[] { "s1", "s2" }, list.Hidden.ToArray());
        }

        [TestMethod]
        public void AwardsWithAnAwardedResponseRecordedAreNotListedWhoeverAwardedThem()
        {
            var awardedByOther = Pending("s1", "m1", "Amy", "Brown", actionedBy: "someone-else");
            awardedByOther.submission.actioned_by[0].outcome = "awarded";
            var list = List(awardedByOther, Pending("s2", "m2", "Ben", "Green", actionedBy: "someone-else"));
            CollectionAssert.AreEqual(new[] { "s2" }, list.People.SelectMany(p => p.Awards).Select(a => a.Item.SubmissionId).ToArray());
            CollectionAssert.AreEqual(new[] { "s1" }, list.Hidden.ToArray());
        }

        [TestMethod]
        public void TheLeadersOwnAwardIsListedButCannotBeAwarded()
        {
            var award = List(Pending("s1", "leader", "Lee", "Leader")).People.Single().Awards.Single();
            Assert.IsFalse(award.CanAward);
            Assert.AreEqual("Your own award", award.Reason);
            Assert.IsTrue(List(Pending("s3", "m3", "Ben", "Green", actionedBy: "someone-else")).People.Single().Awards.Single().CanAward);
        }

        [TestMethod]
        public void WhenTheLeadersIdIsUnknownNothingCanBeAwarded()
        {
            var list = AwardRequestList.FromPending(new[] { Pending("s1", "m1", "Amy", "Brown") }, null);
            Assert.AreEqual(1, list.Total);
            Assert.IsFalse(list.People.Single().Awards.Single().CanAward);
        }

        [TestMethod]
        public void NoPendingAwardsGivesAnEmptyList()
        {
            var list = List(Pending("s1", "m1", "Amy", "Brown", type: "review"));
            Assert.AreEqual(0, list.People.Count);
            Assert.AreEqual(0, list.Total);
        }

        // ---- selecting ----

        // Amy has two awards (a third, already actioned by the leader, is not listed); Ben has two; the leader has one of their own
        private static AwardRequestList Sample() => List(
            Pending("a1", "amy", "Amy", "Brown", stage: "1"),
            Pending("a2", "amy", "Amy", "Brown", stage: "2"),
            Pending("a3", "amy", "Amy", "Brown", stage: "3", actionedBy: "leader"),
            Pending("b1", "ben", "Ben", "Green", stage: "1"),
            Pending("b2", "ben", "Ben", "Green", stage: "2"),
            Pending("l1", "leader", "Lee", "Leader"));

        [TestMethod]
        public void NothingIsSelectedAtFirst()
        {
            var selection = new AwardSelection(Sample());
            Assert.AreEqual(0, selection.Count);
            Assert.AreEqual(0, selection.SelectedAwards().Count);
        }

        [TestMethod]
        public void OneAwardCanBeTickedAndUntickedOnItsOwn()
        {
            var selection = new AwardSelection(Sample());
            selection.Toggle("a2");
            Assert.IsTrue(selection.IsSelected("a2"));
            Assert.IsFalse(selection.IsSelected("a1"));
            Assert.AreEqual((1, 2), selection.ForPerson("amy"));
            selection.Toggle("a2");
            Assert.AreEqual(0, selection.Count);
        }

        [TestMethod]
        public void TickingAPersonTicksAllTheirAwardsThatCanBeAwarded()
        {
            var selection = new AwardSelection(Sample());
            selection.TogglePerson("amy");
            Assert.IsTrue(selection.IsSelected("a1"));
            Assert.IsTrue(selection.IsSelected("a2"));
            Assert.IsFalse(selection.IsSelected("a3"), "already actioned by the leader, so not listed");
            Assert.IsTrue(selection.IsPersonFullySelected("amy"));
            Assert.IsFalse(selection.IsSelected("b1"), "other people are not touched");
        }

        [TestMethod]
        public void TickingAPersonWhoIsPartlyTickedCompletesThemAndTickingAgainClearsThem()
        {
            var selection = new AwardSelection(Sample());
            selection.Toggle("b1");
            Assert.IsFalse(selection.IsPersonFullySelected("ben"));
            selection.TogglePerson("ben");
            Assert.IsTrue(selection.IsPersonFullySelected("ben"));
            Assert.AreEqual(2, selection.Count);
            selection.TogglePerson("ben");
            Assert.AreEqual(0, selection.Count);
        }

        [TestMethod]
        public void UnticklingOneAwardAfterTickingAPersonLeavesThemPartlySelected()
        {
            var selection = new AwardSelection(Sample());
            selection.TogglePerson("ben");
            selection.Toggle("b2");
            Assert.AreEqual((1, 2), selection.ForPerson("ben"));
            Assert.IsFalse(selection.IsPersonFullySelected("ben"));
        }

        [TestMethod]
        public void AwardsThatCannotBeAwardedCannotBeTicked()
        {
            var selection = new AwardSelection(Sample());
            selection.Toggle("a3");
            selection.Toggle("l1");
            selection.Toggle("no-such-award");
            Assert.AreEqual(0, selection.Count);
            selection.TogglePerson("leader");
            Assert.AreEqual(0, selection.Count);
            Assert.IsFalse(selection.IsPersonFullySelected("leader"));
            Assert.AreEqual((0, 0), selection.ForPerson("leader"));
        }

        [TestMethod]
        public void SelectAllTicksEveryAwardThatCanBeAwardedAndClearUnticksThem()
        {
            var selection = new AwardSelection(Sample());
            selection.SelectAll();
            Assert.AreEqual(4, selection.Count);
            selection.Clear();
            Assert.AreEqual(0, selection.Count);
        }

        [TestMethod]
        public void TheSelectedAwardsComeBackInThePagesOrder()
        {
            var selection = new AwardSelection(Sample());
            selection.Toggle("b2");
            selection.Toggle("a2");
            selection.Toggle("b1");
            CollectionAssert.AreEqual(new[] { "a2", "b1", "b2" }, selection.SelectedAwards().Select(a => a.Item.SubmissionId).ToArray());
        }

        // ---- only people who attended ----

        [TestMethod]
        public void OnlyTheChosenPeopleAreListedAndTheOrderIsKept()
        {
            var list = Sample().OnlyMembers(new HashSet<string> { "ben", "amy" });
            CollectionAssert.AreEqual(new[] { "Amy Brown", "Ben Green" }, list.People.Select(p => p.Name).ToArray());
            Assert.AreEqual(4, list.Total);
            Assert.AreEqual(0, Sample().OnlyMembers(new HashSet<string>()).People.Count);
        }

        [TestMethod]
        public void TheSelectionOfAFilteredListOnlyCoversWhoIsListed()
        {
            var selection = new AwardSelection(Sample().OnlyMembers(new HashSet<string> { "ben" }));
            selection.SelectAll();
            CollectionAssert.AreEqual(new[] { "b1", "b2" }, selection.SelectedAwards().Select(a => a.Item.SubmissionId).ToArray());
            selection.Toggle("a1");
            Assert.AreEqual(2, selection.Count, "Amy is not on the filtered page");
        }

        private static Event Ev(string id, string title, DateTime start, string type = "unit", string inviteeId = "unit-1") =>
            new() { id = id, title = title, start_datetime = start, end_datetime = start.AddHours(2), invitee_type = type, invitee_id = inviteeId };

        [TestMethod]
        public void EventsOnTheDayForTheUnitAndTheGroupAreOfferedEarliestFirst()
        {
            var day = new DateTime(2026, 11, 20);
            var events = new[]
            {
                Ev("late", "Ceremony", day.AddHours(19)),
                Ev("early", "Group night", day.AddHours(18), "group", "group-1"),
                Ev("other-unit", "Cub night", day.AddHours(18), "unit", "unit-2"),
                Ev("patrol", "Patrol time", day.AddHours(17), "patrol", "patrol-1"),
                Ev("project", "Project", day.AddHours(17), "member", "m1"),
                Ev("next-day", "Tomorrow", day.AddDays(1).AddHours(19)),
                Ev("day-before", "Yesterday", day.AddDays(-1).AddHours(19)),
            };
            var choices = AwardEventChoice.On(AwardEventChoice.ForUnit(events, "unit-1"), day.AddHours(10));
            CollectionAssert.AreEqual(new[] { "early", "late" }, choices.Select(c => c.Id).ToArray());
            Assert.AreEqual("Group", choices[0].Scope);
            Assert.AreEqual("Unit", choices[1].Scope);
        }

        [TestMethod]
        public void TheDaysWithAnEventForTheUnitAreTheOnesThatCanBeChosen()
        {
            var events = new[]
            {
                Ev("a", "Meeting", new DateTime(2026, 11, 18, 19, 0, 0)),
                Ev("b", "Second meeting", new DateTime(2026, 11, 18, 20, 0, 0)),
                Ev("c", "Group night", new DateTime(2026, 11, 4, 18, 0, 0), "group", "group-1"),
                Ev("d", "Cub night", new DateTime(2026, 11, 11, 18, 0, 0), "unit", "unit-2"),
                Ev("e", "Patrol time", new DateTime(2026, 11, 12, 18, 0, 0), "patrol", "patrol-1"),
            };
            var dates = AwardEventChoice.Dates(AwardEventChoice.ForUnit(events, "unit-1"));
            CollectionAssert.AreEquivalent(new[] { new DateTime(2026, 11, 4), new DateTime(2026, 11, 18) }, dates.ToArray());
            Assert.IsFalse(dates.Contains(new DateTime(2026, 11, 11)), "another unit's night is not the unit's");
        }

        [TestMethod]
        public void AnEventDisplaysItsTitleScopeAndTimes()
        {
            var choice = AwardEventChoice.ForUnit(new[] { Ev("e", "Ceremony", new DateTime(2026, 11, 20, 19, 0, 0)) }, "unit-1").Single();
            Assert.AreEqual("Ceremony (Unit, 7:00 pm - 9:00 pm)", choice.Display);
        }

        [TestMethod]
        public void WhoAttendedIsTheEventsAttendeeList()
        {
            var ev = new GetEventResultModel();
            ev.attendance.attendee_members = new[] { new Attendance_Members { id = "m1" }, new Attendance_Members { id = "m2" }, new Attendance_Members { id = "" } };
            ev.attendance.participant_members = new[] { new Attendance_Members { id = "m3" } };
            CollectionAssert.AreEquivalent(new[] { "m1", "m2" }, AwardEventChoice.Attended(ev).ToArray());
            Assert.AreEqual(0, AwardEventChoice.Attended(null).Count);
        }

        // ---- awarding ----

        private static readonly DateTime Today = new(2026, 11, 21);

        private static List<AwardRequest> Ticked(AwardRequestList list, params string[] ids)
        {
            var selection = new AwardSelection(list);
            foreach (var id in ids) selection.Toggle(id);
            return selection.SelectedAwards();
        }

        [TestMethod]
        public void AnAwardSendsTheOutcomeAndADateWithNoTime()
        {
            var body = ApprovalAction.Award(new DateTime(2026, 3, 7, 22, 15, 0)).ToRequestBody();
            Assert.AreEqual("awarded", body["outcome"]);
            Assert.AreEqual("2026-03-07", body["date_awarded"]);
            Assert.IsFalse(body.ContainsKey("comment"));
        }

        [TestMethod]
        public void AnAwardNeedsADateThatIsNotInTheFutureAndAnAwardRequest()
        {
            var today = new DateTime(2026, 11, 21, 15, 0, 0);
            Assert.IsNotNull(ApprovalAction.Award(null).Problem("award", today));
            Assert.IsNotNull(ApprovalAction.Award(new DateTime(2026, 11, 22)).Problem("award", today));
            Assert.IsNull(ApprovalAction.Award(new DateTime(2026, 11, 21)).Problem("award", today));
            Assert.IsNull(ApprovalAction.Award(new DateTime(2025, 1, 1)).Problem("award", today));
            Assert.IsNotNull(ApprovalAction.Award(Today).Problem("review", today));
        }

        [TestMethod]
        public async Task EachTickedAwardIsSentOnceInOrderWithTheDate()
        {
            var sent = new List<string>();
            var result = await AwardBatch.RunAsync(Ticked(Sample(), "b2", "a1", "b1"), new DateTime(2026, 11, 18), Today,
                (id, action) => { sent.Add($"{id}:{action.Outcome}:{action.ToRequestBody()["date_awarded"]}"); return Task.FromResult<string?>(null); });
            CollectionAssert.AreEqual(new[] { "a1:awarded:2026-11-18", "b1:awarded:2026-11-18", "b2:awarded:2026-11-18" }, sent);
            Assert.IsNull(result.Problem);
            Assert.AreEqual(3, result.Done.Count);
            Assert.AreEqual(0, result.Failed.Count);
        }

        [TestMethod]
        public async Task AFailedAwardIsNotedAndTheRestAreStillSent()
        {
            var sent = new List<string>();
            var result = await AwardBatch.RunAsync(Ticked(Sample(), "a1", "a2", "b1"), new DateTime(2026, 11, 18), Today,
                (id, action) => { sent.Add(id); return Task.FromResult<string?>(id == "a2" ? "Terrain said no." : null); });
            CollectionAssert.AreEqual(new[] { "a1", "a2", "b1" }, sent);
            CollectionAssert.AreEqual(new[] { "a1", "b1" }, result.Done.Select(o => o.Award.Item.SubmissionId).ToArray());
            Assert.AreEqual("a2", result.Failed.Single().Award.Item.SubmissionId);
            Assert.AreEqual("Terrain said no.", result.Failed.Single().Error);
        }

        [TestMethod]
        public async Task AThrownErrorCountsAsAFailureAndTheRunCarriesOn()
        {
            var result = await AwardBatch.RunAsync(Ticked(Sample(), "a1", "a2"), new DateTime(2026, 11, 18), Today,
                (id, action) => id == "a1" ? throw new InvalidOperationException("boom") : Task.FromResult<string?>(null));
            Assert.AreEqual("boom", result.Failed.Single().Error);
            Assert.AreEqual("a2", result.Done.Single().Award.Item.SubmissionId);
        }

        [TestMethod]
        public async Task NothingIsSentWithNoAwardsTickedNoDateOrADateInTheFuture()
        {
            var calls = 0;
            Task<string?> Send(string id, ApprovalAction action) { calls++; return Task.FromResult<string?>(null); }

            var none = await AwardBatch.RunAsync(new List<AwardRequest>(), new DateTime(2026, 11, 18), Today, Send);
            var noDate = await AwardBatch.RunAsync(Ticked(Sample(), "a1"), null, Today, Send);
            var future = await AwardBatch.RunAsync(Ticked(Sample(), "a1"), Today.AddDays(1), Today, Send);

            Assert.IsNotNull(none.Problem);
            Assert.IsNotNull(noDate.Problem);
            Assert.IsNotNull(future.Problem);
            Assert.AreEqual(0, calls);
            Assert.AreEqual(0, none.Outcomes.Count + noDate.Outcomes.Count + future.Outcomes.Count);
        }

        [TestMethod]
        public async Task AwardsThatCannotBeAwardedAreNeverSent()
        {
            var all = Sample().People.SelectMany(p => p.Awards).ToList();   // includes the leader's own and an already actioned one
            var sent = new List<string>();
            await AwardBatch.RunAsync(all, new DateTime(2026, 11, 18), Today, (id, action) => { sent.Add(id); return Task.FromResult<string?>(null); });
            CollectionAssert.AreEquivalent(new[] { "a1", "a2", "b1", "b2" }, sent);
        }

        [TestMethod]
        public async Task ProgressIsReportedFromZeroToTheTotal()
        {
            var steps = new List<string>();
            await AwardBatch.RunAsync(Ticked(Sample(), "a1", "a2"), new DateTime(2026, 11, 18), Today,
                (id, action) => Task.FromResult<string?>(null), (tried, total) => steps.Add($"{tried}/{total}"));
            CollectionAssert.AreEqual(new[] { "0/2", "1/2", "2/2" }, steps);
        }
    }
}
