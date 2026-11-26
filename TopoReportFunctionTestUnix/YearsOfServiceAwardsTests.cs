using Topo.Model.AdditionalAwards;
using Topo.Model.BadgeStore;
using Topo.Model.YearsOfService;

namespace TopoReportFunctionTest
{
    [TestClass]
    public class YearsOfServiceAwardsTests
    {
        private static ServiceMember Member(string id, DateTime? start, string first = "Sam", string last = "Smith") => new ServiceMember
        {
            MemberId = id, FirstName = first, LastName = last, MemberName = first + " " + last, UnitId = "u1", Unit = "Scout Unit", Start = start
        };

        private static BadgeStoreRecord Record(string member, string award, string awarded, string? presented = null) => new BadgeStoreRecord
        {
            MemberId = member, AwardId = award, Awarded = DateTime.Parse(awarded), Presented = presented == null ? null : DateTime.Parse(presented)
        };

        [TestMethod]
        public void AnAwardIdHoldsTheSectionAndTheYears()
        {
            Assert.AreEqual("yos_scout_3", YearsOfServiceAwards.AwardId("Scout", 3));
            Assert.IsTrue(YearsOfServiceAwards.TryParse("yos_cub_12", out var section, out var years));
            Assert.AreEqual("cub", section);
            Assert.AreEqual(12, years);
        }

        [TestMethod]
        public void OtherAwardIdsAreNotYearsOfService()
        {
            foreach (var id in new[] { "camper_award_10", "walkabout_50", "yos_start", "yos_scout_x", "yos_scout_0", "yos__3", "yos_scout_3_4", "" })
            {
                Assert.IsFalse(YearsOfServiceAwards.IsYearsOfService(id), id);
            }
        }

        [TestMethod]
        public void TheNameSaysTheSectionAndTheYears()
        {
            Assert.AreEqual("Scout 3 Years of Service", YearsOfServiceAwards.AwardName("scout", 3));
            Assert.AreEqual("Joey 1 Year of Service", YearsOfServiceAwards.AwardName("joey", 1));
        }

        [TestMethod]
        public void AnAnniversaryIsListedOnceItHasPassedAndNotBefore()
        {
            var start = new DateTime(2024, 3, 10);
            var before = YearsOfServiceAwards.Reached(new[] { Member("a", start) }, "scout", new DateTime(2024, 1, 1), new DateTime(2025, 3, 9));
            Assert.AreEqual(0, before.Count);
            var on = YearsOfServiceAwards.Reached(new[] { Member("a", start) }, "scout", new DateTime(2024, 1, 1), new DateTime(2025, 3, 10));
            Assert.AreEqual(1, on.Count);
            Assert.AreEqual("yos_scout_1", on[0].AwardId);
            Assert.AreEqual(new DateTime(2025, 3, 10), on[0].Earned);
        }

        [TestMethod]
        public void AnniversariesBeforeTheStartDateAreLeftOut()
        {
            // 3 years of service by today, but tracking started after the first two anniversaries
            var reached = YearsOfServiceAwards.Reached(new[] { Member("a", new DateTime(2023, 6, 1)) }, "scout", new DateTime(2025, 6, 1), new DateTime(2026, 11, 25));
            CollectionAssert.AreEqual(new[] { "yos_scout_2", "yos_scout_3" }, reached.Select(r => r.AwardId).ToArray());
        }

        [TestMethod]
        public void TheStartDateItselfCounts()
        {
            var reached = YearsOfServiceAwards.Reached(new[] { Member("a", new DateTime(2024, 11, 25)) }, "scout", new DateTime(2025, 11, 25), new DateTime(2025, 11, 25));
            Assert.AreEqual(1, reached.Count);
        }

        [TestMethod]
        public void AMemberWithNoStartDateHasNoYears()
        {
            Assert.AreEqual(0, YearsOfServiceAwards.Reached(new[] { Member("a", null) }, "scout", new DateTime(2020, 1, 1), new DateTime(2026, 11, 25)).Count);
        }

        [TestMethod]
        public void ALeapDayStartCountsOnTheLastDayOfFebruaryInOtherYears()
        {
            var day = YearsOfServiceAwards.Anniversary(new DateTime(2024, 2, 29), 1);
            Assert.AreEqual(new DateTime(2025, 2, 28), day);
        }

        [TestMethod]
        public void OnlyTheNewestYearIsTheBadgeToHandOver()
        {
            var reached = YearsOfServiceAwards.Reached(new[] { Member("a", new DateTime(2023, 6, 1)) }, "scout", new DateTime(2025, 1, 1), new DateTime(2026, 11, 25));
            var rows = YearsOfServiceAwards.Pending(reached, new HashSet<string>());
            Assert.AreEqual(2, rows.Count);
            var two = rows.Single(r => r.AwardId == "yos_scout_2");
            var three = rows.Single(r => r.AwardId == "yos_scout_3");
            Assert.AreEqual(AwardRowStatus.NotHighest, two.Status);
            Assert.AreEqual("yos_scout_3", two.SupersededBy);
            Assert.AreEqual(AwardRowStatus.ToBePresented, three.Status);
            Assert.AreEqual("Scout 3 Years of Service", three.AwardName);
        }

        [TestMethod]
        public void ARecordedYearIsNotListedAgainAndCoversTheYearsBelowIt()
        {
            var reached = YearsOfServiceAwards.Reached(new[] { Member("a", new DateTime(2023, 6, 1)) }, "scout", new DateTime(2025, 1, 1), new DateTime(2026, 11, 25));
            Assert.AreEqual(0, YearsOfServiceAwards.Pending(reached, new HashSet<string> { "a|yos_scout_3" }).Count);
            var onlyTwo = YearsOfServiceAwards.Pending(reached, new HashSet<string> { "a|yos_scout_2" });
            Assert.AreEqual(1, onlyTwo.Count);
            Assert.AreEqual("yos_scout_3", onlyTwo[0].AwardId);
            Assert.AreEqual(AwardRowStatus.ToBePresented, onlyTwo[0].Status);
        }

        [TestMethod]
        public void EachSectionAndMemberIsSeparate()
        {
            var reached = YearsOfServiceAwards.Reached(new[] { Member("a", new DateTime(2024, 6, 1)), Member("b", new DateTime(2024, 6, 1)) }, "cub", new DateTime(2025, 1, 1), new DateTime(2026, 11, 25));
            var rows = YearsOfServiceAwards.Pending(reached, new HashSet<string> { "a|yos_cub_2" });
            // a has year 2 recorded, so only b is left, with both its years (year 2 is the badge)
            Assert.IsTrue(rows.All(r => r.MemberId == "b"));
            Assert.AreEqual(AwardRowStatus.ToBePresented, rows.Single(r => r.AwardId == "yos_cub_2").Status);
        }

        [TestMethod]
        public void TheStoreSaysWhereTrackingStartedAndWhichYearsWereRecorded()
        {
            var contents = new BadgeStoreContents
            {
                Records =
                {
                    Record("group-1", "yos_start", "2026-11-25"),
                    Record("a", "yos_scout_3", "2026-06-01", "2026-06-10"),
                    Record("a", "camper_award_10", "2026-01-01"),
                    Record("b", "yos_cub_1", "2026-03-01")
                }
            };
            Assert.AreEqual(new DateTime(2026, 11, 25), YearsOfServiceAwards.StartedFrom(contents, "group-1"));
            Assert.IsNull(YearsOfServiceAwards.StartedFrom(contents, "another-group"));
            CollectionAssert.AreEquivalent(new[] { "a|yos_scout_3", "b|yos_cub_1" }, YearsOfServiceAwards.RecordKeys(contents).ToArray());
        }

        [TestMethod]
        public void NothingIsStartedUntilTheStoreHasAStartRecord()
        {
            Assert.IsNull(YearsOfServiceAwards.StartedFrom(new BadgeStoreContents(), "group-1"));
        }

        [TestMethod]
        public void RecordedYearsAreListedWithTheDayTheyWerePresented()
        {
            var contents = new BadgeStoreContents
            {
                Records = { Record("a", "yos_scout_3", "2026-06-01", "2026-06-10"), Record("b", "yos_scout_1", "2026-03-01"), Record("z", "yos_scout_2", "2026-03-01") }
            };
            var recorded = YearsOfServiceAwards.Recorded(contents, new[] { Member("a", null), Member("b", null) });
            Assert.AreEqual(2, recorded.Count);
            Assert.AreEqual(new DateTime(2026, 6, 10), recorded.Single(r => r.MemberId == "a").Day);
            Assert.AreEqual(new DateTime(2026, 3, 1), recorded.Single(r => r.MemberId == "b").Day);
            Assert.AreEqual("Scout 3 Years of Service", recorded.Single(r => r.MemberId == "a").AwardName);
        }

        [TestMethod]
        public void TheFirstCheckIsDueAsSoonAsItIsTurnedOnAndThenOnceADay()
        {
            var now = new DateTime(2026, 11, 25, 9, 0, 0, DateTimeKind.Utc);
            Assert.IsFalse(YearsOfServiceSchedule.IsDue(new YearsOfServiceScanCache(), false, now));
            Assert.IsTrue(YearsOfServiceSchedule.IsDue(new YearsOfServiceScanCache(), true, now));
            Assert.IsFalse(YearsOfServiceSchedule.IsDue(new YearsOfServiceScanCache { ScannedAtUtc = now.AddHours(-23) }, true, now));
            Assert.IsTrue(YearsOfServiceSchedule.IsDue(new YearsOfServiceScanCache { ScannedAtUtc = now.AddHours(-25) }, true, now));
        }

        [TestMethod]
        public void ACheckThatStoppedIsTriedAgainAfterAFewMinutesNotAtOnce()
        {
            var now = new DateTime(2026, 11, 25, 9, 0, 0, DateTimeKind.Utc);
            Assert.IsFalse(YearsOfServiceSchedule.IsDue(new YearsOfServiceScanCache { AttemptedAtUtc = now.AddMinutes(-2) }, true, now));
            Assert.IsTrue(YearsOfServiceSchedule.IsDue(new YearsOfServiceScanCache { AttemptedAtUtc = now.AddMinutes(-6) }, true, now));
        }

        [TestMethod]
        public void TheKeptCheckSurvivesBeingSavedInTheBrowserAndGivesTheSameList()
        {
            var cache = new YearsOfServiceScanCache
            {
                ScannedAtUtc = new DateTime(2026, 11, 25, 1, 0, 0, DateTimeKind.Utc),
                Members = { Member("a", new DateTime(2023, 6, 1)) },
                Store = new BadgeStoreContents { StoreEvents = 1, Records = { Record("group-1", "yos_start", "2025-01-01"), Record("a", "yos_scout_2", "2025-06-01", "2025-06-10") } }
            };
            var json = System.Text.Json.JsonSerializer.Serialize(cache);
            var back = System.Text.Json.JsonSerializer.Deserialize<YearsOfServiceScanCache>(json)!;
            Assert.AreEqual(new DateTime(2025, 1, 1), YearsOfServiceAwards.StartedFrom(back.Store!, "group-1"));
            var rows = YearsOfServiceAwards.Pending(YearsOfServiceAwards.Reached(back.Members, "scout", new DateTime(2025, 1, 1), new DateTime(2026, 11, 25)), YearsOfServiceAwards.RecordKeys(back.Store!));
            // year 2 is recorded, so the newest unrecorded year, 3, is the badge to hand over
            Assert.AreEqual(1, rows.Count);
            Assert.AreEqual("yos_scout_3", rows[0].AwardId);
            Assert.AreEqual(1, back.Store!.StoreEvents);
        }

        [TestMethod]
        public void AYearsOfServiceBadgeInTheMassAwardListIsToldApartFromACamperAward()
        {
            AwardRow Row(string award, string name) => new AwardRow { MemberId = "a", MemberName = "Sam Smith", SortName = "Smith Sam", AwardId = award, AwardName = name, Status = AwardRowStatus.ToBePresented, Earned = new DateTime(2026, 5, 1) };
            var list = new Topo.Model.Approvals.AwardRequestList().WithAdditionalAwards(new[] { Row("yos_scout_3", "Scout 3 Years of Service"), Row("camper_award_10", "Camper Award - 10 Nights") });
            var awards = list.People.Single().Awards;
            var years = awards.Single(a => a.Item.AchievementId == "yos_scout_3");
            var camper = awards.Single(a => a.Item.AchievementId == "camper_award_10");
            Assert.IsTrue(years.IsTopoAward);
            Assert.IsTrue(years.IsYearsOfServiceAward);
            Assert.IsFalse(years.IsAdditionalAward);
            Assert.IsTrue(camper.IsTopoAward);
            Assert.IsFalse(camper.IsYearsOfServiceAward);
            Assert.IsTrue(camper.IsAdditionalAward);
        }
    }
}
