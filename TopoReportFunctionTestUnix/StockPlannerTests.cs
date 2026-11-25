using Topo.Model.Approvals;
using Topo.Model.BadgeOrder;
using Topo.Model.StockPlan;
using Topo.Model.YearsOfService;

namespace TopoReportFunctionTest
{
    [TestClass]
    public class StockPlannerTests
    {
        private static readonly DateTime Today = new DateTime(2026, 11, 25);

        private static BadgeOrderLine Line(string group, string name, int count, ShopVariant? shop = null) => new BadgeOrderLine { Group = group, Name = name, Count = count, Shop = shop };

        private static Result Request(string type, int stage = 0, string stream = "", string area = "", string outcome = "") => new Result
        {
            achievement = new Achievement { type = type, achievement_meta = new Achievement_Meta { stage = stage, stream = stream, branch = stream, sia_area = area } },
            submission = new Submission { outcome = outcome }
        };

        [TestMethod]
        public void NothingAwardedAndNothingComingNeedsNoStock()
        {
            Assert.AreEqual(0, StockPlanner.Suggested(0, 0, 6));
        }

        [TestMethod]
        public void StockIsTheYearsAwardsSpreadOverTheMonthsWithASpareFifth()
        {
            // 12 in a year is 1 a month: 3 months is 3, and a fifth more is 3.6, so 4
            Assert.AreEqual(4, StockPlanner.Suggested(12, 0, 3));
            // 24 a year over 6 months is 12, and a fifth more is 14.4, so 15
            Assert.AreEqual(15, StockPlanner.Suggested(24, 0, 6));
        }

        [TestMethod]
        public void WhatIsKnownToBeComingCountsWhenItIsMoreThanTheHistorySuggests()
        {
            Assert.AreEqual(12, StockPlanner.Suggested(2, 10, 3));
            // and the history when that is more: not added together
            Assert.AreEqual(4, StockPlanner.Suggested(12, 2, 3));
        }

        [TestMethod]
        public void AnythingAwardedInTheLastYearKeepsAtLeastOne()
        {
            Assert.AreEqual(1, StockPlanner.Suggested(1, 0, 1));
            // a single badge coming, with the spare fifth on top, rounds up to two
            Assert.AreEqual(2, StockPlanner.Suggested(0, 1, 3));
        }

        [TestMethod]
        public void AMonthIsAtLeastOneAndMoreMonthsMeanMoreStock()
        {
            Assert.AreEqual(StockPlanner.Suggested(12, 0, 1), StockPlanner.Suggested(12, 0, 0));
            Assert.IsTrue(StockPlanner.Suggested(12, 0, 12) > StockPlanner.Suggested(12, 0, 3));
        }

        [TestMethod]
        public void HistoryAndComingForTheSameBadgeAreOneLine()
        {
            var shop = ScoutShopCatalog.Find("oas-camping-badge", "Stage 3");
            var plan = StockPlanner.Plan(
                new[] { Line(BadgeOrderBuilder.OasGroup, "OAS Camping, Stage 3", 12, shop) },
                new[] { Line(BadgeOrderBuilder.OasGroup, "OAS Camping, Stage 3", 2, shop) },
                Array.Empty<DatedBadge>(), Today, 3);
            var only = plan.Single();
            Assert.AreEqual(12, only.Awarded);
            Assert.AreEqual(2, only.Coming);
            Assert.AreEqual(4, only.Suggested);
            Assert.AreEqual(1.0, only.PerMonth, 0.001);
            Assert.AreEqual(4 * 1.2m, only.Value);
        }

        [TestMethod]
        public void ABadgeOnlyComingStillGetsALine()
        {
            var plan = StockPlanner.Plan(Array.Empty<BadgeOrderLine>(), new[] { Line(BadgeOrderBuilder.MilestonesGroup, "Scout Milestone 2", 3, ScoutShopCatalog.Find("milestone-badges-scout", "Scout Milestone 2")) }, Array.Empty<DatedBadge>(), Today, 3);
            Assert.AreEqual(0, plan.Single().Awarded);
            Assert.AreEqual(4, plan.Single().Suggested);
        }

        [TestMethod]
        public void OnlyAnniversariesInsideTheMonthsToCoverAreComing()
        {
            var anniversaries = new[]
            {
                new DatedBadge { Day = new DateTime(2026, 12, 10), AwardId = "yos_scout_2" },
                new DatedBadge { Day = new DateTime(2027, 2, 25), AwardId = "yos_scout_2" },
                new DatedBadge { Day = new DateTime(2027, 2, 26), AwardId = "yos_scout_2" },
                new DatedBadge { Day = new DateTime(2026, 11, 20), AwardId = "yos_scout_2" }
            };
            var three = StockPlanner.Plan(Array.Empty<BadgeOrderLine>(), Array.Empty<BadgeOrderLine>(), anniversaries, Today, 3).Single();
            // Dec 10 and Feb 25 are inside three months; Feb 26 is not, and 20 Nov has gone
            Assert.AreEqual(2, three.Coming);
            Assert.AreEqual("Scout 2 Years of Service", three.Name);
            Assert.AreEqual(BadgeOrderBuilder.YearsOfServiceGroup, three.Group);
            Assert.IsNotNull(three.Shop);
            var six = StockPlanner.Plan(Array.Empty<BadgeOrderLine>(), Array.Empty<BadgeOrderLine>(), anniversaries, Today, 6).Single();
            Assert.AreEqual(3, six.Coming);
        }

        [TestMethod]
        public void UpcomingAnniversariesIgnoreTheDayTrackingStartedFrom()
        {
            var member = new ServiceMember { MemberId = "a", Start = new DateTime(2023, 12, 5) };
            var upcoming = YearsOfServiceAwards.Upcoming(new[] { member }, "scout", Today, new DateTime(2027, 12, 31));
            CollectionAssert.AreEqual(new[] { "yos_scout_3", "yos_scout_4" }, upcoming.Select(u => u.AwardId).ToArray());
            Assert.AreEqual(new DateTime(2026, 12, 5), upcoming[0].Day);
            Assert.AreEqual(0, YearsOfServiceAwards.Upcoming(new[] { new ServiceMember { MemberId = "b" } }, "scout", Today, new DateTime(2030, 1, 1)).Count);
        }

        [TestMethod]
        public void RequestsWaitingForApprovalBecomeTheBadgesTheyWillNeed()
        {
            var lines = StockPlanner.FromRequests(new[]
            {
                Request("milestone", 2), Request("milestone", 2), Request("outdoor_adventure_skill", 3, "camping"),
                Request("special_interest_area", area: "sia_environment"), Request("intro_section"),
                Request("peak_award"), Request("milestone", 1, outcome: "rejected")
            }, "scout");
            Assert.AreEqual(2, lines.Single(l => l.Name == "Scout Milestone 2").Count);
            Assert.AreEqual(43723152326749L, lines.Single(l => l.Name == "OAS Camping, Stage 3").Shop!.VariantId);
            Assert.AreEqual(43723178704989L, lines.Single(l => l.Name == "Scout SIA, Environment").Shop!.VariantId);
            Assert.AreEqual(1, lines.Single(l => l.Group == BadgeOrderBuilder.IntroGroup).Count);
            // a peak award is not a badge to stock, and a rejected request is not coming
            Assert.AreEqual(4, lines.Count);
        }

        [TestMethod]
        public void TheOrderOfLinesFollowsTheBadgeOrderGroups()
        {
            var plan = StockPlanner.Plan(
                new[] { Line(BadgeOrderBuilder.IntroGroup, "Introduction to Scout", 3), Line(BadgeOrderBuilder.YearsOfServiceGroup, "Scout 1 Year of Service", 3), Line(BadgeOrderBuilder.OasGroup, "OAS Camping, Stage 1", 3) },
                Array.Empty<BadgeOrderLine>(), Array.Empty<DatedBadge>(), Today, 3);
            CollectionAssert.AreEqual(new[] { BadgeOrderBuilder.YearsOfServiceGroup, BadgeOrderBuilder.OasGroup, BadgeOrderBuilder.IntroGroup }, plan.Select(l => l.Group).ToArray());
        }
    }
}
