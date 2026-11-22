using Topo.Model.AdditionalAwards;

namespace TopoReportFunctionTest
{
    [TestClass]
    public class CamperWalkaboutRulesTests
    {
        private static List<AwardTier> Tiers() => CamperWalkaboutRules.Tiers(new[]
        {
            ("camper_award_10", "Camper Award - 10 Nights"), ("camper_award_25", "Camper Award - 25 Nights"),
            ("walkabout_10", "Walkabout Award - 10km"), ("walkabout_50", "Walkabout Award - 50km"),
            ("first_aid", "First Aid"), ("ses", "SES"), ("camper_award_x", "Broken"), ("walkabout_0", "Zero")
        });

        private static LogbookAwardEntry Camp(string start, string end) =>
            CamperWalkaboutRules.ToEntry(start, end, new[] { "camping" }, 0)!;

        private static LogbookAwardEntry Hike(string day, double metres) =>
            CamperWalkaboutRules.ToEntry(day, day, new[] { "walking_hike" }, metres)!;

        [TestMethod]
        public void OnlyCamperAndWalkaboutLevelsAreTiersAndTheyAreReadFromTheId()
        {
            CollectionAssert.AreEqual(new[] { "camper_award_10", "camper_award_25", "walkabout_10", "walkabout_50" }, Tiers().Select(t => t.Id).ToArray());
            CollectionAssert.AreEqual(new[] { 10, 25, 10, 50 }, Tiers().Select(t => t.Threshold).ToArray());
            Assert.IsTrue(Tiers()[0].IsCamper);
            Assert.IsFalse(Tiers()[2].IsCamper);
            Assert.AreEqual("Camper Award - 10 Nights", Tiers()[0].Name);
        }

        [TestMethod]
        public void ATierIsReachedWhenTheTotalIsAtLeastItsThreshold()
        {
            CollectionAssert.AreEqual(new[] { "camper_award_10", "walkabout_10" }, CamperWalkaboutRules.Reached(Tiers(), 10, 10).Select(t => t.Id).ToArray());
            CollectionAssert.AreEqual(new[] { "camper_award_10" }, CamperWalkaboutRules.Reached(Tiers(), 24, 9.9).Select(t => t.Id).ToArray());
            Assert.AreEqual(0, CamperWalkaboutRules.Reached(Tiers(), 9, 9).Count);
        }

        [TestMethod]
        public void ACampCountsANightForEachDayItRunsAndAHikeItsDistance()
        {
            var camp = Camp("2026-03-06", "2026-03-08");
            Assert.AreEqual(2, camp.Nights);
            Assert.AreEqual(0, camp.Kilometres);
            var hike = Hike("2026-04-01", 12500);
            Assert.AreEqual(12.5, hike.Kilometres);
            Assert.AreEqual(0, hike.Nights);
            // an entry with neither category counts for nothing, and a camp that is also a hike counts both
            Assert.AreEqual(0, CamperWalkaboutRules.ToEntry("2026-04-01", "2026-04-02", new[] { "service" }, 5000)!.Nights);
            var both = CamperWalkaboutRules.ToEntry("2026-04-01", "2026-04-03", new[] { "camping", "walking_hike" }, 8000)!;
            Assert.AreEqual(2, both.Nights);
            Assert.AreEqual(8, both.Kilometres);
        }

        [TestMethod]
        public void AnEntryWithABadStartDateIsLeftOutAndABadEndDateMakesItOneDay()
        {
            Assert.IsNull(CamperWalkaboutRules.ToEntry("not a date", "2026-04-01", new[] { "camping" }, 0));
            var oneDay = CamperWalkaboutRules.ToEntry("2026-04-01", "", new[] { "camping" }, 0)!;
            Assert.AreEqual(0, oneDay.Nights);
            Assert.AreEqual(new DateTime(2026, 4, 1), oneDay.End);
        }

        [TestMethod]
        public void ATierIsEarnedOnTheLastDayOfTheEntryThatTookTheTotalToIt()
        {
            var tiers = Tiers().Where(t => t.Id == "camper_award_10").ToList();
            var entries = new[] { Camp("2026-01-10", "2026-01-13"), Camp("2026-03-06", "2026-03-10"), Camp("2026-05-01", "2026-05-04"), Camp("2026-06-01", "2026-06-03") };
            // 3 + 4 + 3 = 10 nights, reached on the last day of the third camp
            var dates = CamperWalkaboutRules.EarnedDates(tiers, entries, 12, 0);
            Assert.AreEqual(new DateTime(2026, 5, 4), dates["camper_award_10"]);
        }

        [TestMethod]
        public void TheOrderOfTheEntriesDoesNotMatter()
        {
            var tiers = Tiers().Where(t => t.Id == "walkabout_10").ToList();
            var a = Hike("2026-02-01", 6000);
            var b = Hike("2026-03-01", 6000);
            Assert.AreEqual(new DateTime(2026, 3, 1), CamperWalkaboutRules.EarnedDates(tiers, new[] { b, a }, 0, 12)["walkabout_10"]);
            Assert.AreEqual(new DateTime(2026, 3, 1), CamperWalkaboutRules.EarnedDates(tiers, new[] { a, b }, 0, 12)["walkabout_10"]);
        }

        [TestMethod]
        public void NightsAndKilometresAreCountedSeparately()
        {
            var entries = new[] { Camp("2026-01-10", "2026-01-12"), Hike("2026-02-01", 11000) };
            var dates = CamperWalkaboutRules.EarnedDates(Tiers().Where(t => t.Id == "walkabout_10" || t.Id == "camper_award_10").ToList(), entries, 2, 11);
            Assert.AreEqual(new DateTime(2026, 2, 1), dates["walkabout_10"]);
            Assert.IsNull(dates["camper_award_10"]);
        }

        [TestMethod]
        public void TotalsBeyondWhatTheEntriesAddUpToCountAsComingFirst()
        {
            // Terrain says 14 nights but the entries only add to 4: ten were imported from before, so ten nights were reached before any entry
            var tiers = Tiers().Where(t => t.Id.StartsWith("camper")).ToList();
            var dates = CamperWalkaboutRules.EarnedDates(tiers, new[] { Camp("2026-03-06", "2026-03-10") }, 14, 0);
            Assert.IsNull(dates["camper_award_10"]);
        }

        [TestMethod]
        public void ATierReachedPartWayThroughTheEntriesAfterAnImportedStartHasADate()
        {
            // 7 nights imported, then 4 more in the entries: 10 is reached by the entry, 25 is not reached at all
            var tiers = Tiers().Where(t => t.Id == "camper_award_10").ToList();
            var dates = CamperWalkaboutRules.EarnedDates(tiers, new[] { Camp("2026-03-06", "2026-03-10") }, 11, 0);
            Assert.AreEqual(new DateTime(2026, 3, 10), dates["camper_award_10"]);
        }

        [TestMethod]
        public void NoEntriesGivesNoDates()
        {
            var tiers = Tiers().Where(t => t.Id == "camper_award_10").ToList();
            Assert.IsNull(CamperWalkaboutRules.EarnedDates(tiers, Array.Empty<LogbookAwardEntry>(), 10, 0)["camper_award_10"]);
        }

        [TestMethod]
        public void EveryReachedTierGetsAnAnswerAndEachFamilyUsesItsOwnTotal()
        {
            var tiers = CamperWalkaboutRules.Reached(Tiers(), 30, 60);
            var entries = new[] { Camp("2026-01-01", "2026-01-26"), Hike("2026-02-01", 60000) };
            var dates = CamperWalkaboutRules.EarnedDates(tiers, entries, 30, 60);
            CollectionAssert.AreEquivalent(new[] { "camper_award_10", "camper_award_25", "walkabout_10", "walkabout_50" }, dates.Keys.ToArray());
            // one 25 night camp reaches both camper tiers on its last day, and one 60 km hike reaches both walkabout tiers
            Assert.AreEqual(new DateTime(2026, 1, 26), dates["camper_award_10"]);
            Assert.AreEqual(new DateTime(2026, 1, 26), dates["camper_award_25"]);
            Assert.AreEqual(new DateTime(2026, 2, 1), dates["walkabout_10"]);
            Assert.AreEqual(new DateTime(2026, 2, 1), dates["walkabout_50"]);
        }
    }
}

namespace TopoReportFunctionTest
{
    [TestClass]
    public class AwardCheckRowsTests
    {
        private static readonly List<AwardTier> Tiers = CamperWalkaboutRules.Tiers(new[]
        {
            ("camper_award_10", "Camper Award - 10 Nights"), ("camper_award_25", "Camper Award - 25 Nights"), ("camper_award_50", "Camper Award - 50 Nights"),
            ("walkabout_10", "Walkabout Award - 10km"), ("walkabout_50", "Walkabout Award - 50km"), ("walkabout_100", "Walkabout Award - 100km")
        });

        private static MemberTotals Totals(string member, double nights, double km) => new MemberTotals { MemberId = member, Name = member, Nights = nights, Kilometres = km };

        private static MissingAward Missing(string member, string award, string name, string? earned = null) => new MissingAward
        {
            MemberId = member, MemberName = member, SortName = member, AwardId = award, AwardName = name, UnitId = "u1", Unit = "Scouts", Earned = earned == null ? null : DateTime.Parse(earned)
        };

        [TestMethod]
        public void EveryLevelTheTotalsReachThatTerrainHasNoRecordOfIsMissing()
        {
            var missing = AwardCheckRows.Missing(new[] { Totals("m1", 27, 12) }, Tiers, new HashSet<string>());
            CollectionAssert.AreEquivalent(new[] { "camper_award_10", "camper_award_25", "walkabout_10" }, missing.Select(m => m.tier.Id).ToArray());
        }

        [TestMethod]
        public void ALevelTerrainHoldsIsNotMissingAndNeitherIsOneBelowIt()
        {
            var terrain = new HashSet<string> { "m1|camper_award_25" };
            var missing = AwardCheckRows.Missing(new[] { Totals("m1", 27, 0) }, Tiers, terrain);
            Assert.AreEqual(0, missing.Count, "10 nights is below a level Terrain holds, so it was presented with it");
        }

        [TestMethod]
        public void ALevelAboveOneTerrainHoldsIsMissing()
        {
            var terrain = new HashSet<string> { "m1|camper_award_10" };
            var missing = AwardCheckRows.Missing(new[] { Totals("m1", 55, 0) }, Tiers, terrain);
            CollectionAssert.AreEquivalent(new[] { "camper_award_25", "camper_award_50" }, missing.Select(m => m.tier.Id).ToArray());
        }

        [TestMethod]
        public void OnlyTheHighestMissingLevelOfAKindIsToBePresentedAndTheOthersAreNotTheHighest()
        {
            var rows = AwardCheckRows.Pending(new[]
            {
                Missing("m1", "camper_award_10", "Camper Award - 10 Nights"), Missing("m1", "camper_award_25", "Camper Award - 25 Nights"), Missing("m1", "camper_award_50", "Camper Award - 50 Nights"),
                Missing("m1", "walkabout_10", "Walkabout Award - 10km")
            }, Tiers, new HashSet<string>());
            Assert.AreEqual(AwardRowStatus.ToBePresented, rows.Single(r => r.AwardId == "camper_award_50").Status);
            Assert.AreEqual(AwardRowStatus.NotHighest, rows.Single(r => r.AwardId == "camper_award_25").Status);
            Assert.AreEqual("camper_award_50", rows.Single(r => r.AwardId == "camper_award_25").SupersededBy);
            Assert.AreEqual("Camper Award - 50 Nights", rows.Single(r => r.AwardId == "camper_award_10").SupersededByName);
            Assert.AreEqual(AwardRowStatus.ToBePresented, rows.Single(r => r.AwardId == "walkabout_10").Status, "walkabout is its own kind");
        }

        [TestMethod]
        public void ALevelBelowOneTerrainNowHoldsIsNoLongerListed()
        {
            var missing = new[] { Missing("m1", "walkabout_10", "Walkabout Award - 10km"), Missing("m1", "walkabout_50", "Walkabout Award - 50km") };
            var rows = AwardCheckRows.Pending(missing, Tiers, new HashSet<string> { "m1|walkabout_50" });
            Assert.AreEqual(0, rows.Count, "recorded since the check, so 10km is covered and 50km is done");
        }

        [TestMethod]
        public void ALevelTerrainRecordedSinceTheCheckLeavesTheLowerOnesToTheNextHighest()
        {
            var missing = new[] { Missing("m1", "camper_award_10", "Camper Award - 10 Nights"), Missing("m1", "camper_award_25", "Camper Award - 25 Nights"), Missing("m1", "camper_award_50", "Camper Award - 50 Nights") };
            var rows = AwardCheckRows.Pending(missing, Tiers, new HashSet<string> { "m1|camper_award_10" });
            CollectionAssert.AreEqual(new[] { "camper_award_25", "camper_award_50" }, rows.Select(r => r.AwardId).ToArray());
            Assert.AreEqual(AwardRowStatus.NotHighest, rows[0].Status);
            Assert.AreEqual(AwardRowStatus.ToBePresented, rows[1].Status);
        }

        [TestMethod]
        public void RowsAreInOrderOfMemberThenAward()
        {
            var a = Missing("m2", "walkabout_10", "Walkabout Award - 10km"); a.SortName = "Adams Al";
            var b = Missing("m1", "walkabout_10", "Walkabout Award - 10km"); b.SortName = "Brown Bea";
            CollectionAssert.AreEqual(new[] { "m2", "m1" }, AwardCheckRows.Pending(new[] { b, a }, Tiers, new HashSet<string>()).Select(r => r.MemberId).ToArray());
        }

        [TestMethod]
        public void PresentingTheHighestCoversTheLowerOnesOfTheSameMember()
        {
            var rows = AwardCheckRows.Pending(new[]
            {
                Missing("m1", "camper_award_10", "Camper Award - 10 Nights"), Missing("m1", "camper_award_25", "Camper Award - 25 Nights"),
                Missing("m2", "camper_award_10", "Camper Award - 10 Nights")
            }, Tiers, new HashSet<string>());
            var covered = AwardCheckRows.CoveredBy(rows.Single(r => r.MemberId == "m1" && r.AwardId == "camper_award_25"), rows);
            CollectionAssert.AreEqual(new[] { "camper_award_10" }, covered.Select(r => r.AwardId).ToArray());
            Assert.AreEqual(0, AwardCheckRows.CoveredBy(rows.Single(r => r.MemberId == "m2"), rows).Count);
        }
    }
}

namespace TopoReportFunctionTest
{
    using Topo.Model.Approvals;

    [TestClass]
    public class AwardListWithCamperAndWalkaboutTests
    {
        private static AwardRequest Pending(string id, string member, string name, string achievement, string submitted) => new AwardRequest
        {
            CanAward = true,
            Item = new PendingApprovalItem { SubmissionId = id, MemberId = member, MemberName = name, AchievementName = achievement, SubmissionType = "award", SubmissionDate = DateTime.Parse(submitted) }
        };

        private static AwardRequestList Pending() => new AwardRequestList
        {
            People =
            {
                new AwardPerson { MemberId = "m2", Name = "Bea Brown", SortName = "Brown Bea", Awards = { Pending("s1", "m2", "Bea Brown", "OAS Camping 2", "2026-10-01"), Pending("s2", "m2", "Bea Brown", "Milestone 2", "2026-10-02") } },
                new AwardPerson { MemberId = "m4", Name = "Dan Dunn", SortName = "Dunn Dan", Awards = { Pending("s3", "m4", "Dan Dunn", "OAS Cycling 1", "2026-10-03") } }
            },
            Hidden = { "hidden-1" }
        };

        private static AwardRow Flagged(string member, string name, string sort, string awardId, string awardName, string earned, AwardRowStatus status = AwardRowStatus.ToBePresented) => new AwardRow
        {
            MemberId = member, MemberName = name, SortName = sort, AwardId = awardId, AwardName = awardName, Earned = DateTime.Parse(earned), Status = status
        };

        [TestMethod]
        public void WithNothingFlaggedTheListIsReturnedAsItWas()
        {
            var list = Pending();
            Assert.AreSame(list, list.WithAdditionalAwards(new[] { Flagged("m2", "Bea Brown", "Brown Bea", "camper_award_10", "Camper Award - 10 Nights", "2026-05-04", AwardRowStatus.NotHighest) }));
            Assert.AreSame(list, list.WithAdditionalAwards(Array.Empty<AwardRow>()));
        }

        [TestMethod]
        public void AFlaggedAwardJoinsTheSamePersonsOtherAwardsInAlphabeticalOrder()
        {
            var merged = Pending().WithAdditionalAwards(new[] { Flagged("m2", "Bea Brown", "Brown Bea", "camper_award_10", "Camper Award - 10 Nights", "2026-05-04") });
            var bea = merged.People.Single(p => p.MemberId == "m2");
            CollectionAssert.AreEqual(new[] { "Camper Award - 10 Nights", "Milestone 2", "OAS Camping 2" }, bea.Awards.Select(a => a.Item.AchievementName).ToArray());
            var camper = bea.Awards[0];
            Assert.IsTrue(camper.IsTopoAward);
            Assert.IsTrue(camper.CanAward);
            Assert.AreEqual("topo:m2|camper_award_10", camper.Item.SubmissionId);
            Assert.AreEqual(new DateTime(2026, 5, 4), camper.Item.SubmissionDate);
            Assert.IsFalse(bea.Awards[1].IsTopoAward);
        }

        [TestMethod]
        public void SomeoneWithOnlyFlaggedAwardsIsAddedInTheirPlaceByLastName()
        {
            var merged = Pending().WithAdditionalAwards(new[] { Flagged("m3", "Cal Clark", "Clark Cal", "walkabout_10", "Walkabout Award - 10km", "2026-02-16"), Flagged("m1", "Al Adams", "Adams Al", "walkabout_10", "Walkabout Award - 10km", "2026-02-16") });
            CollectionAssert.AreEqual(new[] { "Al Adams", "Bea Brown", "Cal Clark", "Dan Dunn" }, merged.People.Select(p => p.Name).ToArray());
            Assert.AreEqual(5, merged.Total);
        }

        [TestMethod]
        public void TheHiddenRequestsAreKept()
        {
            var merged = Pending().WithAdditionalAwards(new[] { Flagged("m1", "Al Adams", "Adams Al", "walkabout_10", "Walkabout Award - 10km", "2026-02-16") });
            Assert.IsTrue(merged.Hidden.Contains("hidden-1"));
        }

        [TestMethod]
        public void TheOriginalListIsNotChanged()
        {
            var list = Pending();
            list.WithAdditionalAwards(new[] { Flagged("m2", "Bea Brown", "Brown Bea", "camper_award_10", "Camper Award - 10 Nights", "2026-05-04") });
            Assert.AreEqual(3, list.Total);
        }

        [TestMethod]
        public void FlaggedAwardsCanBeTickedAndAwardedLikeTheOthers()
        {
            var merged = Pending().WithAdditionalAwards(new[] { Flagged("m2", "Bea Brown", "Brown Bea", "camper_award_10", "Camper Award - 10 Nights", "2026-05-04") });
            var selection = new AwardSelection(merged);
            selection.SelectAll();
            Assert.AreEqual(4, selection.Count);
            Assert.AreEqual(1, selection.SelectedAwards().Count(a => a.IsTopoAward));
            selection.Clear();
            selection.Toggle("topo:m2|camper_award_10");
            Assert.AreEqual(1, selection.Count);
        }
    }
}

namespace TopoReportFunctionTest
{
    using Topo.Model.AdditionalAwards;

    [TestClass]
    public class AutoAwardTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 11, 22, 9, 0, 0, DateTimeKind.Utc);

        private static (ActivityMonitor monitor, Action<TimeSpan> advance) Monitor()
        {
            var now = T0;
            return (new ActivityMonitor(() => now), by => now = now.Add(by));
        }

        [TestMethod]
        public void NothingHappeningForLongEnoughIsIdle()
        {
            var (monitor, advance) = Monitor();
            Assert.IsFalse(monitor.IsIdleFor(TimeSpan.FromSeconds(45)));
            advance(TimeSpan.FromSeconds(46));
            Assert.IsTrue(monitor.IsIdleFor(TimeSpan.FromSeconds(45)));
        }

        [TestMethod]
        public void ARunningRequestIsNeverIdleAndTheQuietTimeRestartsWhenItEnds()
        {
            var (monitor, advance) = Monitor();
            advance(TimeSpan.FromMinutes(5));
            monitor.RequestStarted();
            advance(TimeSpan.FromMinutes(5));
            Assert.IsFalse(monitor.IsIdleFor(TimeSpan.FromSeconds(45)));
            monitor.RequestEnded();
            Assert.IsFalse(monitor.IsIdleFor(TimeSpan.FromSeconds(45)));
            advance(TimeSpan.FromSeconds(46));
            Assert.IsTrue(monitor.IsIdleFor(TimeSpan.FromSeconds(45)));
        }

        [TestMethod]
        public void GoingToAnotherPageIsActivityAndChangesTheVersion()
        {
            var (monitor, advance) = Monitor();
            advance(TimeSpan.FromMinutes(5));
            var version = monitor.Version;
            monitor.UserActivity();
            Assert.AreNotEqual(version, monitor.Version);
            Assert.IsFalse(monitor.IsIdleFor(TimeSpan.FromSeconds(45)));
        }

        [TestMethod]
        public void RequestsMadeInsideTheBackgroundScopeAreNotCounted()
        {
            var (monitor, advance) = Monitor();
            advance(TimeSpan.FromMinutes(5));
            var version = monitor.Version;
            using (monitor.EnterBackground())
            {
                Assert.IsTrue(monitor.InBackground);
                monitor.RequestStarted();
                monitor.RequestEnded();
            }
            Assert.IsFalse(monitor.InBackground);
            Assert.AreEqual(version, monitor.Version);
            Assert.IsTrue(monitor.IsIdleFor(TimeSpan.FromSeconds(45)));
        }

        [TestMethod]
        public async Task TheBackgroundScopeFollowsAwaitedWorkButNotOtherWork()
        {
            var (monitor, _) = Monitor();
            bool inside;
            using (monitor.EnterBackground())
            {
                await Task.Delay(1);
                inside = monitor.InBackground;
            }
            Assert.IsTrue(inside);
            await Task.Delay(1);
            Assert.IsFalse(monitor.InBackground);
        }

        [TestMethod]
        public void OffMeansNeverDue()
        {
            Assert.IsFalse(AutoAwardSchedule.IsDue(new AutoAwardSettings { Enabled = false }, T0));
        }

        [TestMethod]
        public void OnWithNoRunYetIsDueAndThenOncePerDay()
        {
            var settings = new AutoAwardSettings { Enabled = true };
            Assert.IsTrue(AutoAwardSchedule.IsDue(settings, T0));
            settings.LastRunUtc = T0;
            settings.LastAttemptUtc = T0;
            Assert.IsFalse(AutoAwardSchedule.IsDue(settings, T0.AddHours(23)));
            Assert.IsTrue(AutoAwardSchedule.IsDue(settings, T0.AddHours(24).AddMinutes(1)));
        }

        [TestMethod]
        public void ARunThatStoppedIsRetriedAfterFiveMinutesNotBefore()
        {
            var settings = new AutoAwardSettings { Enabled = true, LastRunUtc = T0.AddDays(-2), LastAttemptUtc = T0 };
            Assert.IsFalse(AutoAwardSchedule.IsDue(settings, T0.AddMinutes(4)));
            Assert.IsTrue(AutoAwardSchedule.IsDue(settings, T0.AddMinutes(6)));
        }
    }
}

namespace TopoReportFunctionTest
{
    using Topo.Model.AdditionalAwards;

    [TestClass]
    public class PagesFirstTests
    {
        [TestMethod]
        public async Task APageRequestGoesStraightThroughWhenNoProfileIsTakenOn()
        {
            var monitor = new ActivityMonitor();
            await monitor.PageRequestStartingAsync();
            Assert.IsFalse(monitor.AbortRequested);
        }

        [TestMethod]
        public async Task APageRequestAsksBackgroundWorkToStopAndWaitsForTheProfileToBeHandedBack()
        {
            var monitor = new ActivityMonitor();
            monitor.ProfileTakenOn();
            var page = monitor.PageRequestStartingAsync();
            await Task.Delay(50);
            Assert.IsTrue(monitor.AbortRequested);
            Assert.IsFalse(page.IsCompleted, "the page request is held until the profile is handed back");
            Assert.ThrowsExactly<OperationCanceledException>(() => monitor.ThrowIfAbortRequested());
            monitor.ProfileReturned();
            await page;
            Assert.IsFalse(monitor.AbortRequested);
            monitor.ThrowIfAbortRequested();
        }

        [TestMethod]
        public async Task APageRequestIsNotHeldForeverIfTheProfileIsNeverHandedBack()
        {
            var monitor = new ActivityMonitor();
            monitor.ProfileTakenOn();
            var started = DateTime.UtcNow;
            await monitor.PageRequestStartingAsync();
            Assert.IsTrue(DateTime.UtcNow - started < ActivityMonitor.MaxHold + TimeSpan.FromSeconds(2));
        }

        [TestMethod]
        public async Task BackgroundRequestsNeverAskAnythingToStop()
        {
            var monitor = new ActivityMonitor();
            monitor.ProfileTakenOn();
            using (monitor.EnterBackground())
                await monitor.PageRequestStartingAsync();
            Assert.IsFalse(monitor.AbortRequested);
        }

        [TestMethod]
        public async Task WaitingForAGapEndsOnceNothingHasHappenedForTheQuietTime()
        {
            var now = new DateTime(2026, 11, 22, 9, 0, 0, DateTimeKind.Utc);
            var monitor = new ActivityMonitor(() => now);
            now = now.AddSeconds(1);
            var wait = monitor.WaitForQuietAsync(TimeSpan.FromSeconds(20), CancellationToken.None);
            await Task.Delay(700);
            Assert.IsFalse(wait.IsCompleted);
            now = now.AddSeconds(25);
            await wait;
        }

        [TestMethod]
        public async Task WaitingForAGapStopsWhenCancelled()
        {
            var monitor = new ActivityMonitor();
            using var cancel = new CancellationTokenSource();
            var wait = monitor.WaitForQuietAsync(TimeSpan.FromHours(1), cancel.Token);
            cancel.Cancel();
            await Assert.ThrowsAsync<TaskCanceledException>(async () => await wait);
        }

        [TestMethod]
        public void TheCheckIsOnUnlessSwitchedOffAndItsRetryAndQuietTimesAreShort()
        {
            Assert.IsTrue(new AutoAwardSettings().Enabled);
            Assert.IsTrue(AutoAwardSchedule.RetryAfter <= TimeSpan.FromMinutes(10));
        }
    }
}

namespace TopoReportFunctionTest
{
    using Topo.Model.Approvals;

    [TestClass]
    public class ApprovalsListWithBadgesTests
    {
        private static AwardRow Row(AwardRowStatus status, string earned, string superseded = "", string supersededName = "") => new AwardRow
        {
            MemberId = "m1", MemberName = "Amy Ay", FirstName = "Amy", LastName = "Ay", AwardId = "walkabout_10", AwardName = "Walkabout Award - 10km", Status = status,
            Earned = DateTime.Parse(earned), SupersededBy = superseded, SupersededByName = supersededName
        };

        [TestMethod]
        public void ABadgeToHandOverIsAnAwardRowWithNoPresentedDateAndSaysToAwardIt()
        {
            var row = AwardCheckRows.ToApprovalRows(new[] { Row(AwardRowStatus.ToBePresented, "2026-02-16") }).Single();
            Assert.AreEqual("Amy Ay", row.member_display_name);
            Assert.AreEqual("Walkabout Award - 10km", row.achievement_name);
            Assert.AreEqual("Award", row.submission_type);
            Assert.IsNull(row.presented_date);
            Assert.AreEqual(new DateTime(2026, 2, 16), row.awarded_date);
            Assert.AreEqual(new DateTime(2026, 2, 16), row.submission_date, "the date range looks at the day it was earned");
            Assert.AreEqual("Award", row.to_award);
            Assert.AreEqual("additional_award", row.achievement_type);
            StringAssert.StartsWith(row.achievement_id, "topo:");
        }

        [TestMethod]
        public void ABadgeCoveredByAHigherOneSaysWhichToAwardInstead()
        {
            var row = AwardCheckRows.ToApprovalRows(new[] { Row(AwardRowStatus.NotHighest, "2026-02-16", "walkabout_50", "Walkabout Award - 50km") }).Single();
            Assert.AreEqual("Not the highest - award Walkabout Award - 50km", row.to_award);
            Assert.IsNull(row.presented_date);
        }

        private static List<ApprovalsListModel> Mixed() => AwardCheckRows.ToApprovalRows(new[]
        {
            Row(AwardRowStatus.ToBePresented, "2026-10-01"),
            Row(AwardRowStatus.NotHighest, "2026-10-02", "walkabout_50", "Walkabout Award - 50km"),
            Row(AwardRowStatus.NotHighest, "2026-06-02", "walkabout_50", "Walkabout Award - 50km")
        }).Concat(new[]
        {
            new ApprovalsListModel { achievement_name = "OAS Camping 2", submission_type = "Award", submission_outcome = "Awarded", submission_date = new DateTime(2026, 10, 3), awarded_date = new DateTime(2026, 10, 3) },
            new ApprovalsListModel { achievement_name = "Milestone 1", submission_type = "Review", submission_outcome = "Rejected", submission_date = new DateTime(2026, 10, 4) }
        }).ToList();

        [TestMethod]
        public void TheDateRangeFilterAppliesToTheBadgesToo()
        {
            var rows = ApprovalsFilter.Apply(Mixed(), new DateTime(2026, 10, 1), new DateTime(2026, 10, 31), false, false, true);
            Assert.AreEqual(4, rows.Count, "the badge earned in June is outside the range");
        }

        [TestMethod]
        public void ToBePresentedListsTheBadgesToHandOverIncludingThoseNotTheHighest()
        {
            var rows = ApprovalsFilter.Apply(Mixed(), new DateTime(2026, 6, 1), new DateTime(2026, 10, 31), true, false, true);
            Assert.AreEqual(3, rows.Count(r => r.achievement_type == "additional_award"));
            Assert.IsTrue(rows.Any(r => r.to_award == "Award"));
            Assert.IsTrue(rows.Any(r => r.to_award.StartsWith("Not the highest")));
        }

        [TestMethod]
        public void HasBeenPresentedListsNoneOfThemBecauseTerrainOnlyHoldsPresentedOnes()
        {
            var rows = ApprovalsFilter.Apply(Mixed(), new DateTime(2026, 6, 1), new DateTime(2026, 10, 31), false, true, true);
            Assert.AreEqual(0, rows.Count(r => r.achievement_type == "additional_award"));
        }

        [TestMethod]
        public void RejectedRowsAreLeftOutUnlessShown()
        {
            Assert.IsFalse(ApprovalsFilter.Apply(Mixed(), new DateTime(2026, 6, 1), new DateTime(2026, 10, 31), false, false, false).Any(r => r.submission_outcome == "Rejected"));
            Assert.IsTrue(ApprovalsFilter.Apply(Mixed(), new DateTime(2026, 6, 1), new DateTime(2026, 10, 31), false, false, true).Any(r => r.submission_outcome == "Rejected"));
        }

        [TestMethod]
        public void NothingToFilterGivesAnEmptyList()
        {
            Assert.AreEqual(0, ApprovalsFilter.Apply(null, DateTime.Today.AddMonths(-2), DateTime.Today, false, false, false).Count);
        }

        [TestMethod]
        public void AMemberNameWithoutSeparateNamesIsSplitForTheNameColumn()
        {
            var row = AwardCheckRows.ToApprovalRows(new[] { new AwardRow { MemberId = "m", MemberName = "Amy Mae Ay", AwardId = "x", AwardName = "X", Status = AwardRowStatus.ToBePresented, Earned = DateTime.Today } }).Single();
            Assert.AreEqual("Amy Mae Ay", row.member_display_name);
        }
    }
}

namespace TopoReportFunctionTest
{
    using Topo.Model.Approvals;

    [TestClass]
    public class OasToAwardTests
    {
        private static ApprovalsListModel Oas(string member, string line, int stage, string? presented = null, string type = "Award", string outcome = "Awarded") => new ApprovalsListModel
        {
            member_id = member, achievement_type = "outdoor_adventure_skill", achievement_name = $"Oas {line} {stage}", oas_line = line, oas_stage = stage,
            submission_type = type, submission_outcome = outcome, presented_date = presented == null ? null : DateTime.Parse(presented)
        };

        [TestMethod]
        public void ALoneStageToBePresentedIsToAward()
        {
            var rows = new[] { Oas("m1", "camping", 2) };
            ApprovalsToAward.ApplyOas(rows);
            Assert.AreEqual("Award", rows[0].to_award);
        }

        [TestMethod]
        public void WithSeveralStagesToBePresentedOnlyTheHighestIsAwardedAndTheRestAreNotTheHighest()
        {
            var rows = new[] { Oas("m1", "camping", 2), Oas("m1", "camping", 4), Oas("m1", "camping", 3) };
            ApprovalsToAward.ApplyOas(rows);
            Assert.AreEqual("Award", rows[1].to_award);
            Assert.AreEqual("Not the highest - award Oas camping 4", rows[0].to_award);
            Assert.AreEqual("Not the highest - award Oas camping 4", rows[2].to_award);
        }

        [TestMethod]
        public void AnotherLineOrAnotherMemberIsSeparate()
        {
            var rows = new[] { Oas("m1", "camping", 2), Oas("m1", "paddling", 1), Oas("m2", "camping", 3) };
            ApprovalsToAward.ApplyOas(rows);
            Assert.IsTrue(rows.All(r => r.to_award == "Award"));
        }

        [TestMethod]
        public void AStageBelowOneAlreadyPresentedSaysSo()
        {
            var rows = new[] { Oas("m1", "camping", 2), Oas("m1", "camping", 3, presented: "2026-10-01") };
            ApprovalsToAward.ApplyOas(rows);
            Assert.AreEqual("Not the highest - Oas camping 3 was already presented", rows[0].to_award);
            Assert.AreEqual("", rows[1].to_award, "a presented stage has nothing to award");
        }

        [TestMethod]
        public void PresentedReviewAndRejectedRowsAreLeftAlone()
        {
            var rows = new[] { Oas("m1", "camping", 1, presented: "2026-09-01"), Oas("m1", "camping", 2, type: "Review"), Oas("m1", "camping", 3, outcome: "Rejected") };
            ApprovalsToAward.ApplyOas(rows);
            Assert.IsTrue(rows.All(r => r.to_award == ""));
        }

        [TestMethod]
        public void OtherKindsOfAchievementAreNotTouched()
        {
            var rows = new[] { new ApprovalsListModel { member_id = "m1", achievement_type = "milestone", submission_type = "Award", achievement_name = "Milestone 2" } };
            ApprovalsToAward.ApplyOas(rows);
            Assert.AreEqual("", rows[0].to_award);
        }
    }
}

namespace TopoReportFunctionTest
{
    using Topo.Model.Wallchart;

    [TestClass]
    public class ApprovalsReportCamperWalkaboutTests
    {
        private static Topo.Model.Approvals.ApprovalsListModel Row(string name, string toAward, string type = "additional_award") => new Topo.Model.Approvals.ApprovalsListModel
        {
            member_first_name = "Amy", member_last_name = "Ay", achievement_type = type, achievement_name = name, submission_type = "Award",
            awarded_date = new DateTime(2026, 5, 4), to_award = toAward
        };

        [TestMethod]
        public void AnAwardToHandOverIsRedAndHasItsToAwardColumn()
        {
            SetEnvironment.SetSyncfusionLicense();
            var rows = new List<Topo.Model.Approvals.ApprovalsListModel>
            {
                Row("A Walkabout 50", "Award"),
                Row("B Walkabout 10", "Not the highest - award Walkabout Award - 50km"),
                Row("D OAS Camping 2", "Award", "outdoor_adventure_skill")
            };
            var workbook = new Topo.Services.ReportService().GenerateApprovalsWorkbook(rows, "Group", "scout", "Scout Unit", new DateTime(2026, 1, 1), new DateTime(2026, 12, 31), false, false);
            var sheet = workbook.Worksheets[0];
            int First(string text) => Enumerable.Range(1, 20).First(r => sheet.Range[r, 1].Text == text) + 1;
            var red = System.Drawing.ColorTranslator.FromHtml("#ff0000").ToArgb();
            Assert.AreEqual("To award", sheet.Range[First("A Walkabout 50") - 1, 8].Text);
            Assert.AreEqual("Award", sheet.Range[First("A Walkabout 50"), 8].Text);
            Assert.AreEqual(red, sheet.Range[First("A Walkabout 50"), 2].CellStyle.Color.ToArgb());
            Assert.AreNotEqual(red, sheet.Range[First("B Walkabout 10"), 2].CellStyle.Color.ToArgb(), "a level that is not the highest is not handed over");
            Assert.AreNotEqual(red, sheet.Range[First("D OAS Camping 2"), 2].CellStyle.Color.ToArgb());
            workbook.Close();
        }
    }

    [TestClass]
    public class WallchartCamperWalkaboutTests
    {
        private static WallchartItemModel Scout(int camper, int walkabout) => new WallchartItemModel { MemberName = "Amy Ay", CamperAward = camper, WalkaboutAward = walkabout };

        [TestMethod]
        public void TheWallchartHasCamperAndWalkaboutColumnsAtTheEndUnderTheirOwnHeading()
        {
            CollectionAssert.AreEqual(new[] { "PEAK AWARD", "Camper Award", "Walkabout Award" }, WallchartLayout.Columns.TakeLast(3).ToArray());
            Assert.AreEqual(("AWARDS", 2), WallchartLayout.Groups.Last());
        }

        [TestMethod]
        public void TheHighestLevelTerrainHoldsIsShownInGreen()
        {
            var row = WallchartLayout.Build(new[] { Scout(25, 100) }, false).Single().Rows.Single();
            Assert.AreEqual(25, row.Cells[^2].Count);
            Assert.AreEqual("#90ee90", row.Cells[^2].Colour);
            Assert.AreEqual(100, row.Cells[^1].Count);
            Assert.AreEqual("#90ee90", row.Cells[^1].Colour);
        }

        [TestMethod]
        public void NoLevelLeavesTheCellsUnshaded()
        {
            var row = WallchartLayout.Build(new[] { Scout(0, 0) }, false).Single().Rows.Single();
            Assert.IsNull(row.Cells[^2].Colour);
            Assert.IsNull(row.Cells[^1].Colour);
            Assert.AreEqual(42, row.Cells.Sum(c => c.Span));
        }

        [TestMethod]
        public void TheSheetHasTheNewHeadingsAndKeepsPeakAwardWhereItWas()
        {
            SetEnvironment.SetSyncfusionLicense();
            var workbook = new Topo.Services.ReportService().GenerateWallchartWorkbook(new List<WallchartItemModel> { Scout(25, 100) }, "Group", "scout", "Scout Unit", false);
            var sheet = workbook.Worksheets[0];
            // the column headings row is the one with " Name" in column 1
            int heading = Enumerable.Range(1, 15).First(r => sheet.Range[r, 1].Text == " Name");
            Assert.AreEqual(" PEAK AWARD", sheet.Range[heading, 41].Text);
            Assert.AreEqual(" Camper Award", sheet.Range[heading, 42].Text);
            Assert.AreEqual(" Walkabout Award", sheet.Range[heading, 43].Text);
            Assert.AreEqual("AWARDS", sheet.Range[heading - 2, 42].Text);
            var data = heading + 1;
            Assert.AreEqual("25", sheet.Range[data, 42].DisplayText);
            Assert.AreEqual("100", sheet.Range[data, 43].DisplayText);
            Assert.AreEqual(System.Drawing.ColorTranslator.FromHtml("#90ee90").ToArgb(), sheet.Range[data, 42].CellStyle.Color.ToArgb());
            workbook.Close();
        }
    }
}

namespace TopoReportFunctionTest
{
    [TestClass]
    public class HighestAwardLevelsTests
    {
        [TestMethod]
        public void TheHighestLevelOfEachKindInTerrainsRecordsIsUsed()
        {
            var levels = HighestAwardLevels.For(new[] { "camper_award_10", "camper_award_50", "camper_award_25", "walkabout_10", "walkabout_150", "first_aid" });
            Assert.AreEqual((50, 150), levels);
        }

        [TestMethod]
        public void NoCamperOrWalkaboutRecordsMeansNoLevel()
        {
            Assert.AreEqual((0, 0), HighestAwardLevels.For(new[] { "first_aid" }));
            Assert.AreEqual((0, 0), HighestAwardLevels.For(Array.Empty<string>()));
        }
    }
}

namespace TopoReportFunctionTest
{
    [TestClass]
    public class AdditionalAwardCountsOrderTests
    {
        [TestMethod]
        public void CamperAndWalkaboutAwardsGoFromTheFewestNightsAndShortestDistanceUp()
        {
            var counts = new Dictionary<string, int>
            {
                ["Walkabout Award - 100km"] = 1, ["Walkabout Award - 10km"] = 4, ["Walkabout Award - 50km"] = 2, ["Walkabout Award - 200km"] = 1,
                ["Camper Award - 25 Nights"] = 3, ["Camper Award - 10 Nights"] = 5, ["Camper Award - 75 Nights"] = 1, ["First Aid"] = 6, ["SES"] = 1
            };
            var ids = new Dictionary<string, string>
            {
                ["Walkabout Award - 100km"] = "walkabout_100", ["Walkabout Award - 10km"] = "walkabout_10", ["Walkabout Award - 50km"] = "walkabout_50", ["Walkabout Award - 200km"] = "walkabout_200",
                ["Camper Award - 25 Nights"] = "camper_award_25", ["Camper Award - 10 Nights"] = "camper_award_10", ["Camper Award - 75 Nights"] = "camper_award_75", ["First Aid"] = "first_aid", ["SES"] = "ses"
            };
            var names = Topo.Model.UnitReport.AdditionalAwardCounts.Sorted(counts, ids).Select(c => c.Name).ToArray();
            CollectionAssert.AreEqual(new[]
            {
                "Camper Award - 10 Nights", "Camper Award - 25 Nights", "Camper Award - 75 Nights", "First Aid", "SES",
                "Walkabout Award - 10km", "Walkabout Award - 50km", "Walkabout Award - 100km", "Walkabout Award - 200km"
            }, names);
        }
    }
}
