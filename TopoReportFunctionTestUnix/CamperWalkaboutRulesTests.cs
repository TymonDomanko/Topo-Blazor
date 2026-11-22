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
    using Topo.Model.BadgeStore;

    [TestClass]
    public class AwardCheckRowsTests
    {
        private static BadgeStoreRecord Rec(string member, string award, string awarded, string? presented = null) => new BadgeStoreRecord
        {
            MemberId = member, AwardId = award, Awarded = DateTime.Parse(awarded), Presented = presented == null ? null : DateTime.Parse(presented), EventId = "e1", CommentId = "c-" + member + award
        };

        private static List<AwardTier> Tiers() => CamperWalkaboutRules.Tiers(new[]
        {
            ("camper_award_10", "Camper Award - 10 Nights"), ("camper_award_25", "Camper Award - 25 Nights"), ("walkabout_10", "Walkabout Award - 10km")
        });

        [TestMethod]
        public void StoreRecordsBecomeToBePresentedOrPresentedRowsForTheUnitsMembersOnly()
        {
            var contents = new BadgeStoreContents { Records = { Rec("m1", "camper_award_10", "2026-08-26"), Rec("m2", "walkabout_10", "2026-08-26", "2026-09-02"), Rec("other", "camper_award_10", "2026-08-26") } };
            var rows = AwardCheckRows.FromStore(contents,
                new Dictionary<string, string> { ["m1"] = "Zed Zee", ["m2"] = "Amy Ay" },
                new Dictionary<string, string> { ["camper_award_10"] = "Camper Award - 10 Nights" });
            Assert.AreEqual(2, rows.Count);
            Assert.AreEqual("Amy Ay", rows[0].MemberName);
            Assert.AreEqual(AwardRowStatus.Presented, rows[0].Status);
            Assert.AreEqual("walkabout_10", rows[0].AwardName, "an award with no name in the list shows its id");
            Assert.AreEqual(AwardRowStatus.ToBePresented, rows[1].Status);
            Assert.AreEqual("Camper Award - 10 Nights", rows[1].AwardName);
            Assert.AreEqual(new DateTime(2026, 8, 26), rows[1].Earned);
            Assert.AreEqual("c-m1camper_award_10", rows[1].CommentId);
        }

        [TestMethod]
        public void TiersTheLogbookReachesWithNoRecordAnywhereAreMissing()
        {
            var members = new[] { new MemberTotals { MemberId = "m1", Name = "A", Nights = 26, Kilometres = 12 } };
            var missing = AwardCheckRows.Missing(members, Tiers(), new HashSet<string>(), new BadgeStoreContents());
            CollectionAssert.AreEquivalent(new[] { "camper_award_10", "camper_award_25", "walkabout_10" }, missing.Select(m => m.tier.Id).ToArray());
        }

        [TestMethod]
        public void ATierWithATerrainRecordOrAStoreRecordIsNotMissing()
        {
            var members = new[] { new MemberTotals { MemberId = "m1", Name = "A", Nights = 26, Kilometres = 12 } };
            var store = new BadgeStoreContents { Records = { Rec("m1", "camper_award_25", "2026-08-26") } };
            var missing = AwardCheckRows.Missing(members, Tiers(), new HashSet<string> { "m1|camper_award_10" }, store);
            CollectionAssert.AreEqual(new[] { "walkabout_10" }, missing.Select(m => m.tier.Id).ToArray());
        }

        [TestMethod]
        public void AMemberWhoHasNotReachedATierHasNothingMissingForIt()
        {
            var members = new[] { new MemberTotals { MemberId = "m1", Name = "A", Nights = 9, Kilometres = 9 } };
            Assert.AreEqual(0, AwardCheckRows.Missing(members, Tiers(), new HashSet<string>(), new BadgeStoreContents()).Count);
        }

        [TestMethod]
        public void RowsForFlaggingCarryTheDayEarnedWhenKnownAndNothingWhenNot()
        {
            var a = new MemberTotals { MemberId = "m1", Name = "Amy" };
            var tiers = Tiers();
            var missing = new[] { (a, tiers[0]), (a, tiers[2]) };
            var dates = new Dictionary<string, Dictionary<string, DateTime?>> { ["m1"] = new Dictionary<string, DateTime?> { ["camper_award_10"] = new DateTime(2026, 5, 4), ["walkabout_10"] = null } };
            var rows = AwardCheckRows.ForFlagging(missing, dates);
            Assert.AreEqual(new DateTime(2026, 5, 4), rows.Single(r => r.AwardId == "camper_award_10").Earned);
            Assert.IsNull(rows.Single(r => r.AwardId == "walkabout_10").Earned);
            Assert.IsTrue(rows.All(r => r.Status == AwardRowStatus.NotFlagged));
            Assert.AreEqual("m1|camper_award_10", rows.First(r => r.AwardId == "camper_award_10").Key);
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
            Assert.AreSame(list, list.WithAdditionalAwards(new[] { Flagged("m2", "Bea Brown", "Brown Bea", "camper_award_10", "Camper Award - 10 Nights", "2026-05-04", AwardRowStatus.Presented) }));
            Assert.AreSame(list, list.WithAdditionalAwards(Array.Empty<AwardRow>()));
        }

        [TestMethod]
        public void AFlaggedAwardJoinsTheSamePersonsOtherAwardsInAlphabeticalOrder()
        {
            var merged = Pending().WithAdditionalAwards(new[] { Flagged("m2", "Bea Brown", "Brown Bea", "camper_award_10", "Camper Award - 10 Nights", "2026-05-04") });
            var bea = merged.People.Single(p => p.MemberId == "m2");
            CollectionAssert.AreEqual(new[] { "Camper Award - 10 Nights", "Milestone 2", "OAS Camping 2" }, bea.Awards.Select(a => a.Item.AchievementName).ToArray());
            var camper = bea.Awards[0];
            Assert.IsTrue(camper.IsStoreAward);
            Assert.IsTrue(camper.CanAward);
            Assert.AreEqual("store:m2|camper_award_10", camper.Item.SubmissionId);
            Assert.AreEqual(new DateTime(2026, 5, 4), camper.Item.SubmissionDate);
            Assert.IsFalse(bea.Awards[1].IsStoreAward);
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
            Assert.AreEqual(1, selection.SelectedAwards().Count(a => a.IsStoreAward));
            selection.Clear();
            selection.Toggle("store:m2|camper_award_10");
            Assert.AreEqual(1, selection.Count);
        }
    }
}

namespace TopoReportFunctionTest
{
    using Topo.Model.BadgeStore;

    [TestClass]
    public class AwardFlagPlanTests
    {
        private static readonly DateTime Today = new DateTime(2026, 11, 22);

        private static List<AwardTier> Tiers() => CamperWalkaboutRules.Tiers(new[]
        {
            ("camper_award_10", "Camper Award - 10 Nights"), ("camper_award_25", "Camper Award - 25 Nights"), ("camper_award_50", "Camper Award - 50 Nights"),
            ("walkabout_10", "Walkabout Award - 10km"), ("walkabout_50", "Walkabout Award - 50km"), ("walkabout_100", "Walkabout Award - 100km"),
            ("walkabout_150", "Walkabout Award - 150km"), ("walkabout_300", "Walkabout Award - 300km")
        });

        private static AwardRow Missing(string award, string? earned = "2025-01-01", string member = "m1") => new AwardRow
        {
            MemberId = member, MemberName = "Amy Ay", SortName = "Ay Amy", AwardId = award, AwardName = award, Earned = earned == null ? null : DateTime.Parse(earned), Status = AwardRowStatus.NotFlagged
        };

        private static Dictionary<string, DateTime> Terrain(params (string award, string day)[] records) =>
            records.ToDictionary(r => "m1|" + r.award, r => DateTime.Parse(r.day));

        private static BadgeStoreContents Store(params BadgeStoreRecord[] records)
        {
            var contents = new BadgeStoreContents();
            contents.Records.AddRange(records);
            return contents;
        }

        private static BadgeStoreRecord Rec(string award, string awarded, string? presented = null, string superseded = "") => new BadgeStoreRecord
        {
            MemberId = "m1", AwardId = award, Awarded = DateTime.Parse(awarded), Presented = presented == null ? null : DateTime.Parse(presented), SupersededBy = superseded,
            EventId = "e1", CommentId = "c-" + award
        };

        private static List<PlannedAward> Plan(IEnumerable<AwardRow> missing, Dictionary<string, DateTime>? terrain = null, BadgeStoreContents? store = null) =>
            AwardFlagPlan.Build(missing, Tiers(), terrain ?? new Dictionary<string, DateTime>(), store ?? new BadgeStoreContents(), Today);

        private static PlannedAward Of(List<PlannedAward> plan, string award) => plan.Single(p => p.Row.AwardId == award);

        [TestMethod]
        public void ALevelBelowOneAlreadyPresentedIsPresentedOnTheSameDay()
        {
            var plan = Plan(new[] { Missing("walkabout_10") }, Terrain(("walkabout_50", "2024-05-08")));
            var p = Of(plan, "walkabout_10");
            Assert.AreEqual(PlannedKind.AlreadyPresented, p.Kind);
            Assert.AreEqual(new DateTime(2024, 5, 8), p.Awarded);
            Assert.AreEqual(new DateTime(2024, 5, 8), p.Presented);
            Assert.AreEqual("walkabout_50", p.CoveredBy!.Id);
            Assert.AreEqual("", p.SupersededBy);
        }

        [TestMethod]
        public void TheNearestHigherPresentedLevelGivesTheDay()
        {
            var plan = Plan(new[] { Missing("walkabout_10") }, Terrain(("walkabout_100", "2026-08-26"), ("walkabout_50", "2024-05-08"), ("walkabout_150", "2026-09-02")));
            Assert.AreEqual(new DateTime(2024, 5, 8), Of(plan, "walkabout_10").Presented);
        }

        [TestMethod]
        public void ALevelPresentedAccordingToTheStoreCountsAsPresentedToo()
        {
            var plan = Plan(new[] { Missing("camper_award_10") }, store: Store(Rec("camper_award_25", "2025-03-01", "2025-03-05")));
            var p = Of(plan, "camper_award_10");
            Assert.AreEqual(PlannedKind.AlreadyPresented, p.Kind);
            Assert.AreEqual(new DateTime(2025, 3, 5), p.Presented);
        }

        [TestMethod]
        public void WithNoHigherLevelPresentedOnlyTheHighestMissingIsToBePresentedAndTheRestAreNotTheHighest()
        {
            var plan = Plan(new[] { Missing("walkabout_10"), Missing("walkabout_50"), Missing("walkabout_100") });
            Assert.AreEqual(PlannedKind.ToBePresented, Of(plan, "walkabout_100").Kind);
            Assert.IsNull(Of(plan, "walkabout_100").Presented);
            foreach (var low in new[] { "walkabout_10", "walkabout_50" })
            {
                Assert.AreEqual(PlannedKind.NotHighest, Of(plan, low).Kind, low);
                Assert.AreEqual("walkabout_100", Of(plan, low).SupersededBy, low);
                Assert.IsNull(Of(plan, low).Presented, low);
            }
        }

        [TestMethod]
        public void APresentedLevelInTheMiddleSplitsTheOthers()
        {
            // 150 was presented; 10 is below it (presented), 300 is above it (the one to present)
            var plan = Plan(new[] { Missing("walkabout_10"), Missing("walkabout_300") }, Terrain(("walkabout_150", "2026-09-02")));
            Assert.AreEqual(PlannedKind.AlreadyPresented, Of(plan, "walkabout_10").Kind);
            Assert.AreEqual(new DateTime(2026, 9, 2), Of(plan, "walkabout_10").Presented);
            Assert.AreEqual(PlannedKind.ToBePresented, Of(plan, "walkabout_300").Kind);
        }

        [TestMethod]
        public void ALevelAlreadyFlaggedBelowANewHigherOneIsRewrittenAsNotTheHighest()
        {
            var plan = Plan(new[] { Missing("walkabout_100") }, store: Store(Rec("walkabout_50", "2025-06-01")));
            Assert.AreEqual(PlannedKind.ToBePresented, Of(plan, "walkabout_100").Kind);
            var update = Of(plan, "walkabout_50");
            Assert.IsTrue(update.IsUpdate);
            Assert.AreEqual(PlannedKind.NotHighest, update.Kind);
            Assert.AreEqual("walkabout_100", update.SupersededBy);
            Assert.AreEqual(new DateTime(2025, 6, 1), update.Awarded, "its own day is kept");
        }

        [TestMethod]
        public void ALevelAlreadyCoveredByTheSameHigherOneIsLeftAlone()
        {
            var plan = Plan(new[] { Missing("walkabout_10") }, store: Store(Rec("walkabout_100", "2025-09-01"), Rec("walkabout_50", "2025-06-01", superseded: "walkabout_100")));
            Assert.IsFalse(plan.Any(p => p.Row.AwardId == "walkabout_50"));
            Assert.AreEqual("walkabout_100", Of(plan, "walkabout_10").SupersededBy);
        }

        [TestMethod]
        public void AFlaggedLevelBelowAPresentedOneIsRewrittenAsPresented()
        {
            var plan = Plan(new[] { Missing("walkabout_10") }, Terrain(("walkabout_100", "2026-08-26")), Store(Rec("walkabout_50", "2025-06-01")));
            var update = Of(plan, "walkabout_50");
            Assert.AreEqual(PlannedKind.AlreadyPresented, update.Kind);
            Assert.AreEqual(new DateTime(2026, 8, 26), update.Presented);
            Assert.IsTrue(update.IsUpdate);
        }

        [TestMethod]
        public void CamperAndWalkaboutAreSeparateKinds()
        {
            var plan = Plan(new[] { Missing("camper_award_10"), Missing("walkabout_10"), Missing("walkabout_50") }, Terrain(("camper_award_25", "2025-03-05")));
            Assert.AreEqual(PlannedKind.AlreadyPresented, Of(plan, "camper_award_10").Kind);
            Assert.AreEqual(PlannedKind.ToBePresented, Of(plan, "walkabout_50").Kind);
            Assert.AreEqual(PlannedKind.NotHighest, Of(plan, "walkabout_10").Kind);
        }

        [TestMethod]
        public void AlevelTheLogbookCannotDateIsToBePresentedWithTodayAsTheDay()
        {
            var plan = Plan(new[] { Missing("camper_award_10", earned: null) });
            Assert.AreEqual(Today, Of(plan, "camper_award_10").Awarded);
            Assert.AreEqual(PlannedKind.ToBePresented, Of(plan, "camper_award_10").Kind);
        }

        [TestMethod]
        public void EachMemberIsPlannedOnTheirOwn()
        {
            var plan = Plan(new[] { Missing("walkabout_10", member: "m1"), Missing("walkabout_50", member: "m2") });
            Assert.AreEqual(PlannedKind.ToBePresented, plan.Single(p => p.Row.MemberId == "m1").Kind);
            Assert.AreEqual(PlannedKind.ToBePresented, plan.Single(p => p.Row.MemberId == "m2").Kind);
        }

        [TestMethod]
        public void TheNoteSaysWhatWillBeRecorded()
        {
            var plan = Plan(new[] { Missing("walkabout_10"), Missing("walkabout_50") });
            StringAssert.Contains(Of(plan, "walkabout_10").Note, "not the highest");
            StringAssert.Contains(Of(plan, "walkabout_10").Note, "Walkabout Award - 50km");
            Assert.AreEqual("to be presented", Of(plan, "walkabout_50").Note);
            var presented = Plan(new[] { Missing("walkabout_10") }, Terrain(("walkabout_50", "2024-05-08")));
            StringAssert.Contains(Of(presented, "walkabout_10").Note, "08/05/2024");
        }

        [TestMethod]
        public void TheSupersededFieldIsWrittenAndReadAndNeedsADifferentWrite()
        {
            var text = BadgeStoreReader.AwardText("m1", "walkabout_10", new DateTime(2025, 1, 1), null, "walkabout_50");
            Assert.AreEqual("{\"v\":1,\"t\":\"award\",\"m\":\"m1\",\"a\":\"walkabout_10\",\"d\":\"2025-01-01\",\"x\":\"walkabout_50\"}", text);
            var record = BadgeStoreReader.Resolve(new[] { new EventComment { id = "c", text = text, status = "active", member = new EventCommentMember { id = "l" } } }, _ => true).Records.Single();
            Assert.AreEqual("walkabout_50", record.SupersededBy);
            var contents = new BadgeStoreContents();
            contents.Records.Add(record);
            Assert.IsTrue(BadgeStoreWrites.IsAlreadyRecorded(contents, "m1", "walkabout_10", new DateTime(2025, 1, 1), null, "walkabout_50"));
            Assert.IsFalse(BadgeStoreWrites.IsAlreadyRecorded(contents, "m1", "walkabout_10", new DateTime(2025, 1, 1), null, ""));
            Assert.IsFalse(BadgeStoreWrites.IsAlreadyRecorded(contents, "m1", "walkabout_10", new DateTime(2025, 1, 1), null, "walkabout_100"));
        }
    }
}

namespace TopoReportFunctionTest
{
    using Topo.Model.BadgeStore;

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
    using Topo.Model.BadgeStore;

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
        private static AwardRow Row(AwardRowStatus status, string earned, string? presented = null, string superseded = "", string supersededName = "") => new AwardRow
        {
            MemberId = "m1", MemberName = "Amy Ay", FirstName = "Amy", LastName = "Ay", AwardId = "walkabout_10", AwardName = "Walkabout Award - 10km", Status = status,
            Earned = DateTime.Parse(earned), Presented = presented == null ? null : DateTime.Parse(presented), SupersededBy = superseded, SupersededByName = supersededName
        };

        [TestMethod]
        public void AFlaggedBadgeIsAnAwardRowWithNoPresentedDateAndSaysToAwardIt()
        {
            var row = AwardCheckRows.ToApprovalRows(new[] { Row(AwardRowStatus.ToBePresented, "2026-02-16") }).Single();
            Assert.AreEqual("Amy Ay", row.member_display_name);
            Assert.AreEqual("Walkabout Award - 10km", row.achievement_name);
            Assert.AreEqual("Award", row.submission_type);
            Assert.IsNull(row.presented_date);
            Assert.AreEqual(new DateTime(2026, 2, 16), row.awarded_date);
            Assert.AreEqual("Award", row.to_award);
            Assert.AreEqual("additional_award", row.achievement_type);
            StringAssert.StartsWith(row.achievement_id, "store:");
        }

        [TestMethod]
        public void ABadgeNotYetPresentedIsDatedFromWhenItWasFlaggedNotWhenItWasEarned()
        {
            var flagged = Row(AwardRowStatus.ToBePresented, "2024-05-08");
            flagged.FlaggedOn = new DateTime(2026, 11, 22);
            Assert.AreEqual(new DateTime(2026, 11, 22), AwardCheckRows.ToApprovalRows(new[] { flagged }).Single().submission_date);
            Assert.AreEqual(new DateTime(2024, 5, 8), AwardCheckRows.ToApprovalRows(new[] { flagged }).Single().awarded_date);
        }

        [TestMethod]
        public void TheFlaggedDayComesFromWhenTheStoreRecordWasWritten()
        {
            var contents = new Topo.Model.BadgeStore.BadgeStoreContents();
            contents.Records.Add(new Topo.Model.BadgeStore.BadgeStoreRecord { MemberId = "m1", AwardId = "walkabout_10", Awarded = new DateTime(2024, 5, 8), RecordedAt = new DateTime(2026, 11, 22, 12, 0, 0, DateTimeKind.Unspecified) });
            var row = AwardCheckRows.FromStore(contents, new Dictionary<string, string> { ["m1"] = "Amy Ay" }, new Dictionary<string, string>()).Single();
            Assert.AreEqual(new DateTime(2026, 11, 22), row.FlaggedOn);
        }

        [TestMethod]
        public void ABadgeCoveredByAHigherOneSaysWhichToAwardInstead()
        {
            var row = AwardCheckRows.ToApprovalRows(new[] { Row(AwardRowStatus.NotHighest, "2026-02-16", superseded: "walkabout_50", supersededName: "Walkabout Award - 50km") }).Single();
            Assert.AreEqual("Not the highest - award Walkabout Award - 50km", row.to_award);
            Assert.IsNull(row.presented_date);
        }

        [TestMethod]
        public void APresentedBadgeHasItsPresentedDateAndNothingToAward()
        {
            var row = AwardCheckRows.ToApprovalRows(new[] { Row(AwardRowStatus.Presented, "2026-02-16", "2026-03-01") }).Single();
            Assert.AreEqual(new DateTime(2026, 3, 1), row.presented_date);
            Assert.AreEqual("", row.to_award);
            Assert.AreEqual("Awarded", row.submission_outcome);
            Assert.AreEqual(new DateTime(2026, 3, 1), row.submission_date, "the date range looks at the day it was presented");
        }

        private static List<ApprovalsListModel> Mixed() => AwardCheckRows.ToApprovalRows(new[]
        {
            Row(AwardRowStatus.ToBePresented, "2026-10-01"),
            Row(AwardRowStatus.Presented, "2026-09-01", "2026-09-01"),
            Row(AwardRowStatus.NotHighest, "2026-10-02", superseded: "walkabout_50", supersededName: "Walkabout Award - 50km")
        }).Concat(new[]
        {
            new ApprovalsListModel { achievement_name = "OAS Camping 2", submission_type = "Award", submission_outcome = "Awarded", submission_date = new DateTime(2026, 10, 3), awarded_date = new DateTime(2026, 10, 3) },
            new ApprovalsListModel { achievement_name = "Milestone 1", submission_type = "Review", submission_outcome = "Rejected", submission_date = new DateTime(2026, 10, 4) }
        }).ToList();

        [TestMethod]
        public void TheDateRangeFilterAppliesToTheBadgesToo()
        {
            var rows = ApprovalsFilter.Apply(Mixed(), new DateTime(2026, 10, 1), new DateTime(2026, 10, 31), false, false, true);
            Assert.IsFalse(rows.Any(r => r.presented_date.HasValue && r.achievement_type == "additional_award"), "the badge presented in September is outside the range");
            Assert.AreEqual(4, rows.Count);
        }

        [TestMethod]
        public void ToBePresentedListsTheBadgesNotYetPresentedIncludingThoseNotTheHighest()
        {
            var rows = ApprovalsFilter.Apply(Mixed(), new DateTime(2026, 8, 1), new DateTime(2026, 10, 31), true, false, true);
            Assert.AreEqual(3, rows.Count(r => r.achievement_type == "additional_award") + rows.Count(r => r.achievement_name == "OAS Camping 2"));
            Assert.IsTrue(rows.Any(r => r.to_award == "Award"));
            Assert.IsTrue(rows.Any(r => r.to_award.StartsWith("Not the highest")));
            Assert.IsFalse(rows.Any(r => r.presented_date.HasValue && r.achievement_type == "additional_award"));
        }

        [TestMethod]
        public void HasBeenPresentedListsAPresentedBadgeEvenWhenItsAwardedDayIsTheSame()
        {
            var rows = ApprovalsFilter.Apply(Mixed(), new DateTime(2026, 8, 1), new DateTime(2026, 10, 31), false, true, true);
            Assert.AreEqual(1, rows.Count);
            Assert.AreEqual("additional_award", rows[0].achievement_type);
        }

        [TestMethod]
        public void RejectedRowsAreLeftOutUnlessShown()
        {
            Assert.IsFalse(ApprovalsFilter.Apply(Mixed(), new DateTime(2026, 8, 1), new DateTime(2026, 10, 31), false, false, false).Any(r => r.submission_outcome == "Rejected"));
            Assert.IsTrue(ApprovalsFilter.Apply(Mixed(), new DateTime(2026, 8, 1), new DateTime(2026, 10, 31), false, false, true).Any(r => r.submission_outcome == "Rejected"));
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
    [TestClass]
    public class AdditionalAwardsWithTopoRecordsTests
    {
        private static AwardRow Stored(string award, AwardRowStatus status, string earned, string? presented = null, string member = "m1") => new AwardRow
        {
            MemberId = member, MemberName = "Amy Ay", AwardId = award, AwardName = award, Status = status, Earned = DateTime.Parse(earned), Presented = presented == null ? null : DateTime.Parse(presented)
        };

        private static Dictionary<string, AdditionalAwardSpecificationListModel> Specs() => new Dictionary<string, AdditionalAwardSpecificationListModel>
        {
            ["walkabout_10"] = new AdditionalAwardSpecificationListModel { id = "walkabout_10", name = "Walkabout Award - 10km", additionalAwardSortIndex = 5, additionalAwardValue = 10 },
            ["walkabout_50"] = new AdditionalAwardSpecificationListModel { id = "walkabout_50", name = "Walkabout Award - 50km", additionalAwardSortIndex = 6, additionalAwardValue = 50 }
        };

        private static Dictionary<string, (string name, int nights, int kms)> Totals() => new Dictionary<string, (string name, int nights, int kms)> { ["m1"] = ("Amy Ay (3 Nights, 60 KMs)", 3, 60) };

        [TestMethod]
        public void StoredAwardsBecomeReportRowsWithTheirDatesAndTheMembersTotals()
        {
            var rows = AdditionalAwardReportRows.FromStore(new[] { Stored("walkabout_50", AwardRowStatus.ToBePresented, "2026-05-04"), Stored("walkabout_10", AwardRowStatus.Presented, "2026-02-01", "2026-03-01") },
                new HashSet<string>(), Specs(), Totals());
            var flagged = rows.Single(r => r.AwardId == "walkabout_50");
            Assert.AreEqual(new DateTime(2026, 5, 4), flagged.AwardDate);
            Assert.IsNull(flagged.PresentedDate);
            Assert.AreEqual(6, flagged.AwardSortIndex);
            Assert.AreEqual(50, flagged.AwardUnits);
            Assert.AreEqual("Amy Ay (3 Nights, 60 KMs)", flagged.MemberName);
            Assert.AreEqual(60, flagged.KMsHiked);
            Assert.AreEqual(new DateTime(2026, 3, 1), rows.Single(r => r.AwardId == "walkabout_10").PresentedDate);
            Assert.IsFalse(rows.Any(r => r.NotHighest));
        }

        [TestMethod]
        public void AnAwardTerrainAlreadyHasARecordOfIsLeftToThatRecord()
        {
            var rows = AdditionalAwardReportRows.FromStore(new[] { Stored("walkabout_10", AwardRowStatus.ToBePresented, "2026-02-01") }, new HashSet<string> { "m1|walkabout_10" }, Specs(), Totals());
            Assert.AreEqual(0, rows.Count);
        }

        [TestMethod]
        public void ALevelCoveredByAHigherOneIsMarkedNotTheHighest()
        {
            var rows = AdditionalAwardReportRows.FromStore(new[] { Stored("walkabout_10", AwardRowStatus.NotHighest, "2026-02-01") }, new HashSet<string>(), Specs(), Totals());
            Assert.IsTrue(rows.Single().NotHighest);
            Assert.IsNull(rows.Single().PresentedDate);
        }

        [TestMethod]
        public void AnAwardWithNoSpecificationKeepsItsOwnNameAndAMemberWithNoTotalsKeepsTheirName()
        {
            var rows = AdditionalAwardReportRows.FromStore(new[] { Stored("mystery", AwardRowStatus.ToBePresented, "2026-02-01") }, new HashSet<string>(), Specs(), new Dictionary<string, (string name, int nights, int kms)>());
            Assert.AreEqual("mystery", rows.Single().AwardName);
            Assert.AreEqual("Amy Ay", rows.Single().MemberName);
        }

        [TestMethod]
        public void TheReportColoursNotYetPresentedOrangePresentedGreenAndCoveredLevelsKhaki()
        {
            var specs = new List<AdditionalAwardSpecificationListModel>(Specs().Values);
            var rows = new List<AdditionalAwardListModel>
            {
                new AdditionalAwardListModel { MemberName = "Amy", AwardId = "walkabout_10", AwardName = "W10", AwardDate = new DateTime(2026, 2, 1), NotHighest = true, AwardUnits = 10 },
                new AdditionalAwardListModel { MemberName = "Amy", AwardId = "walkabout_50", AwardName = "W50", AwardDate = new DateTime(2026, 5, 4), AwardUnits = 50 },
                new AdditionalAwardListModel { MemberName = "Bea", AwardId = "walkabout_50", AwardName = "W50", AwardDate = new DateTime(2026, 5, 4), PresentedDate = new DateTime(2026, 6, 1), AwardUnits = 50 }
            };
            SetEnvironment.SetSyncfusionLicense();
            var workbook = new Topo.Services.ReportService().GenerateAdditionalAwardsWorkbook(specs, rows, new List<string> { "walkabout_10", "walkabout_50" }, "Group", "scout", "Scout Unit");
            var sheet = workbook.Worksheets[0];
            // find each member's row by name in column A
            int RowOf(string name) { for (var r = 1; r < 30; r++) if (sheet.Range[r, 1].Text == name) return r; throw new AssertFailedException(name); }
            var amy = RowOf("Amy");
            var bea = RowOf("Bea");
            Assert.AreEqual(System.Drawing.Color.Khaki.ToArgb(), sheet.Range[amy, 3].CellStyle.Color.ToArgb(), "covered level");
            Assert.AreEqual(System.Drawing.Color.Orange.ToArgb(), sheet.Range[amy, 4].CellStyle.Color.ToArgb(), "awarded, not presented");
            Assert.AreEqual(System.Drawing.Color.LightGreen.ToArgb(), sheet.Range[bea, 4].CellStyle.Color.ToArgb(), "presented");
            var legend = Enumerable.Range(1, 40).Select(r => sheet.Range[r, 1].Text).ToList();
            CollectionAssert.Contains(legend, "Not the highest");
            workbook.Close();
        }
    }
}

namespace TopoReportFunctionTest
{
    [TestClass]
    public class PersonalProgressAwardsTests
    {
        private static AwardRow Row(string member, string award, AwardRowStatus status, string earned, string? presented = null, string superseded = "", string supersededName = "") => new AwardRow
        {
            MemberId = member, AwardId = award, AwardName = award + " name", Status = status, Earned = DateTime.Parse(earned), Presented = presented == null ? null : DateTime.Parse(presented),
            SupersededBy = superseded, SupersededByName = supersededName
        };

        [TestMethod]
        public void OnlyTheMembersOwnAwardsAreListedLowestLevelFirstWithTheirStatus()
        {
            var rows = new[]
            {
                Row("m1", "walkabout_50", AwardRowStatus.ToBePresented, "2026-05-04"),
                Row("m2", "walkabout_10", AwardRowStatus.Presented, "2026-01-01", "2026-02-01"),
                Row("m1", "walkabout_10", AwardRowStatus.NotHighest, "2026-01-01", superseded: "walkabout_50", supersededName: "Walkabout Award - 50km"),
                Row("m1", "camper_award_10", AwardRowStatus.Presented, "2025-01-01", "2025-02-01")
            };
            var mine = AwardRecordSummaries.ForMember(rows, "m1");
            CollectionAssert.AreEqual(new[] { "camper_award_10 name", "walkabout_10 name", "walkabout_50 name" }, mine.Select(a => a.Name).ToArray());
            CollectionAssert.AreEqual(new[] { "Presented", "Not the highest - award Walkabout Award - 50km", "To be presented" }, mine.Select(a => a.Status).ToArray());
            Assert.AreEqual(new DateTime(2025, 2, 1), mine[0].Presented);
        }

        [TestMethod]
        public void AMemberWithNoRecordsHasAnEmptyList()
        {
            Assert.AreEqual(0, AwardRecordSummaries.ForMember(new[] { Row("m2", "walkabout_10", AwardRowStatus.Presented, "2026-01-01") }, "m1").Count);
        }
    }
}

namespace TopoReportFunctionTest
{
    using Topo.Model.Wallchart;

    [TestClass]
    public class WallchartCamperWalkaboutTests
    {
        private static WallchartItemModel Scout(int camper, bool camperPresented, int walkabout, bool walkaboutPresented) => new WallchartItemModel
        {
            MemberName = "Amy Ay", CamperAward = camper, CamperAwardPresented = camperPresented, WalkaboutAward = walkabout, WalkaboutAwardPresented = walkaboutPresented
        };

        [TestMethod]
        public void TheWallchartHasCamperAndWalkaboutColumnsAtTheEndUnderTheirOwnHeading()
        {
            CollectionAssert.AreEqual(new[] { "PEAK AWARD", "Camper Award", "Walkabout Award" }, WallchartLayout.Columns.TakeLast(3).ToArray());
            Assert.AreEqual(("ADDITIONAL AWARDS", 2), WallchartLayout.Groups.Last());
        }

        [TestMethod]
        public void ThePresentedLevelIsGreenAndOneStillToBePresentedIsOrange()
        {
            var row = WallchartLayout.Build(new[] { Scout(25, true, 100, false) }, false).Single().Rows.Single();
            var camper = row.Cells[^2];
            var walkabout = row.Cells[^1];
            Assert.AreEqual(25, camper.Count);
            Assert.AreEqual("#90ee90", camper.Colour);
            Assert.AreEqual(100, walkabout.Count);
            Assert.AreEqual("#ffa500", walkabout.Colour);
        }

        [TestMethod]
        public void NoLevelLeavesTheCellsUnshaded()
        {
            var row = WallchartLayout.Build(new[] { Scout(0, false, 0, false) }, false).Single().Rows.Single();
            Assert.IsNull(row.Cells[^2].Colour);
            Assert.IsNull(row.Cells[^1].Colour);
            Assert.AreEqual(42, row.Cells.Sum(c => c.Span));
        }

        [TestMethod]
        public void TheSheetHasTheNewHeadingsAndKeepsPeakAwardWhereItWas()
        {
            SetEnvironment.SetSyncfusionLicense();
            var workbook = new Topo.Services.ReportService().GenerateWallchartWorkbook(new List<WallchartItemModel> { Scout(25, true, 100, false) }, "Group", "scout", "Scout Unit", false);
            var sheet = workbook.Worksheets[0];
            // the column headings row is the one with " Name" in column 1
            int heading = Enumerable.Range(1, 15).First(r => sheet.Range[r, 1].Text == " Name");
            Assert.AreEqual(" PEAK AWARD", sheet.Range[heading, 41].Text);
            Assert.AreEqual(" Camper Award", sheet.Range[heading, 42].Text);
            Assert.AreEqual(" Walkabout Award", sheet.Range[heading, 43].Text);
            Assert.AreEqual("ADDITIONAL AWARDS", sheet.Range[heading - 2, 42].Text);
            var data = heading + 1;
            Assert.AreEqual("25", sheet.Range[data, 42].DisplayText);
            Assert.AreEqual("100", sheet.Range[data, 43].DisplayText);
            Assert.AreEqual(System.Drawing.ColorTranslator.FromHtml("#90ee90").ToArgb(), sheet.Range[data, 42].CellStyle.Color.ToArgb());
            Assert.AreEqual(System.Drawing.ColorTranslator.FromHtml("#ffa500").ToArgb(), sheet.Range[data, 43].CellStyle.Color.ToArgb());
            workbook.Close();
        }
    }
}

namespace TopoReportFunctionTest
{
    [TestClass]
    public class HighestAwardLevelsTests
    {
        private static AwardRow Stored(string member, string award, AwardRowStatus status) => new AwardRow { MemberId = member, AwardId = award, Status = status };

        [TestMethod]
        public void TerrainsOwnRecordsCountAsPresentedAndTheHighestOfEachKindIsUsed()
        {
            var levels = HighestAwardLevels.For("m1", new[] { "camper_award_10", "camper_award_25", "walkabout_10", "walkabout_100", "first_aid" }, Array.Empty<AwardRow>());
            Assert.AreEqual((25, true, 100, true), levels);
        }

        [TestMethod]
        public void ATopoRecordHigherThanTerrainsIsUsedAndIsPresentedOnlyIfItSaysSo()
        {
            var levels = HighestAwardLevels.For("m1", new[] { "walkabout_50" }, new[] { Stored("m1", "walkabout_100", AwardRowStatus.ToBePresented), Stored("m1", "camper_award_10", AwardRowStatus.Presented) });
            Assert.AreEqual((10, true, 100, false), levels);
        }

        [TestMethod]
        public void ALevelCoveredByAHigherOneNeverBeatsTheHigherOne()
        {
            var levels = HighestAwardLevels.For("m1", Array.Empty<string>(), new[] { Stored("m1", "walkabout_10", AwardRowStatus.NotHighest), Stored("m1", "walkabout_50", AwardRowStatus.ToBePresented) });
            Assert.AreEqual((0, false, 50, false), levels);
        }

        [TestMethod]
        public void OtherMembersRecordsAndNoRecordsGiveNothing()
        {
            Assert.AreEqual((0, false, 0, false), HighestAwardLevels.For("m1", Array.Empty<string>(), new[] { Stored("m2", "walkabout_50", AwardRowStatus.Presented) }));
        }

        [TestMethod]
        public void AnAwardInBothTerrainAndTheStoreCountsAsPresented()
        {
            Assert.AreEqual((0, false, 50, true), HighestAwardLevels.For("m1", new[] { "walkabout_50" }, new[] { Stored("m1", "walkabout_50", AwardRowStatus.ToBePresented) }));
        }
    }
}
