using Topo.Model.BadgeOrder;
using Topo.Model.BadgeStore;
using Topo.Model.SectionReport;
using Topo.Model.UnitReport;

namespace TopoReportFunctionTest
{
    [TestClass]
    public class BadgeOrderTests
    {
        private static SectionReportModel Section(string section, Action<SectionBadges>? badges = null, int invested = 0)
        {
            var model = new SectionReportModel { Section = section, InvestedInPeriod = invested };
            badges?.Invoke(model.Badges);
            return model;
        }

        private static SectionReportSet Report(params SectionReportModel[] sections) => new SectionReportSet { Sections = sections.ToList() };

        private static UnitBadgeCount Count(string name, int count, string id = "") => new UnitBadgeCount { Name = name, Count = count, Id = id };

        [TestMethod]
        public void TheCatalogReadsEveryVariantAndFindsOneByProductAndOption()
        {
            Assert.IsTrue(ScoutShopCatalog.All.Count > 150);
            Assert.AreEqual(ScoutShopCatalog.All.Count, ScoutShopCatalog.All.Select(v => v.VariantId).Distinct().Count());
            var stage3 = ScoutShopCatalog.Find("oas-camping-badge", "Stage 3")!;
            Assert.AreEqual(43723152326749L, stage3.VariantId);
            Assert.AreEqual(1.2m, stage3.Price);
            Assert.IsNull(ScoutShopCatalog.Find("oas-camping-badge", "Stage 10"));
            Assert.IsNull(ScoutShopCatalog.Find("no-such-product", "Stage 1"));
        }

        [TestMethod]
        public void AProductPageLinkSelectsTheOption()
        {
            Assert.AreEqual("https://scoutshop.com.au/products/oas-camping-badge?Badge+Type=Stage+3", ScoutShopCatalog.Find("oas-camping-badge", "Stage 3")!.Url);
            Assert.AreEqual("https://scoutshop.com.au/products/first-aid-badge", ScoutShopCatalog.Find("first-aid-badge", "Default Title")!.Url);
        }

        [TestMethod]
        public void MilestonesAreTheProductOfTheirOwnSection()
        {
            var lines = BadgeOrderBuilder.Build(Report(Section("cub", b => b.Milestones.Add(Count("Milestone 2", 3))), Section("scout", b => b.Milestones.Add(Count("Milestone 2", 1)))));
            Assert.AreEqual(2, lines.Count);
            Assert.AreEqual(43723144757341L, lines.Single(l => l.Name == "Cub Milestone 2").Shop!.VariantId);
            Assert.AreEqual(43723145052253L, lines.Single(l => l.Name == "Scout Milestone 2").Shop!.VariantId);
            Assert.IsTrue(lines.All(l => l.Group == BadgeOrderBuilder.MilestonesGroup));
        }

        [TestMethod]
        public void AnOutdoorAdventureSkillStageIsOneLineAcrossSections()
        {
            var lines = BadgeOrderBuilder.Build(Report(
                Section("cub", b => b.OasStages.Add(new OasStageCount { Stream = "Camping", Stage = 3, Count = 1 })),
                Section("scout", b => b.OasStages.Add(new OasStageCount { Stream = "Camping", Stage = 3, Count = 2 }))));
            var line = lines.Single();
            Assert.AreEqual(3, line.Count);
            Assert.AreEqual("OAS Camping, Stage 3", line.Name);
            Assert.AreEqual(43723152326749L, line.Shop!.VariantId);
        }

        [TestMethod]
        public void SpecialInterestAreasMatchTheShopByNameForTheSection()
        {
            var lines = BadgeOrderBuilder.Build(Report(Section("scout", b =>
            {
                b.SiaCategories.Add(Count("Environment", 2));
                b.SiaCategories.Add(Count("Art & Literature", 1));
                b.SiaCategories.Add(Count("Creating a Better World", 1));
            })));
            Assert.AreEqual(43723178704989L, lines.Single(l => l.Name.EndsWith("Environment")).Shop!.VariantId);
            Assert.AreEqual(43723178672221L, lines.Single(l => l.Name.EndsWith("Art & Literature")).Shop!.VariantId);
            Assert.AreEqual(43723178639453L, lines.Single(l => l.Name.EndsWith("Creating a Better World")).Shop!.VariantId);
        }

        [TestMethod]
        public void YearsOfServiceUseTheServiceBadgeOfTheSection()
        {
            var lines = BadgeOrderBuilder.Build(Report(
                Section("scout", b => b.AdditionalAwards.Add(Count("Scout 3 Years of Service", 4, "yos_scout_3"))),
                Section("joey", b => b.AdditionalAwards.Add(Count("Joey 2 Years of Service", 1, "yos_joey_2"))),
                Section("rover", b => b.AdditionalAwards.Add(Count("Rover 1 Year of Service", 1, "yos_rover_1")))));
            Assert.AreEqual(43723173888093L, lines.Single(l => l.Name.StartsWith("Scout")).Shop!.VariantId);
            Assert.AreEqual(43723110645853L, lines.Single(l => l.Name.StartsWith("Joey")).Shop!.VariantId);
            Assert.AreEqual(43723166875741L, lines.Single(l => l.Name.StartsWith("Rover")).Shop!.VariantId);
            Assert.IsTrue(lines.All(l => l.Group == BadgeOrderBuilder.YearsOfServiceGroup));
        }

        [TestMethod]
        public void ACubCannotOrderAFourthYearBecauseTheShopHasNone()
        {
            var line = BadgeOrderBuilder.Build(Report(Section("cub", b => b.AdditionalAwards.Add(Count("Cub 4 Years of Service", 1, "yos_cub_4"))))).Single();
            Assert.IsFalse(line.InShop);
        }

        [TestMethod]
        public void CamperWalkaboutAndOtherAdditionalAwardsAreMatchedByTheirIds()
        {
            var lines = BadgeOrderBuilder.Build(Report(Section("scout", b =>
            {
                b.AdditionalAwards.Add(Count("Camper Award - 25 Nights", 2, "camper_award_25"));
                b.AdditionalAwards.Add(Count("Walkabout Award - 50km", 1, "walkabout_50"));
                b.AdditionalAwards.Add(Count("First Aid", 1, "first_aid"));
                b.AdditionalAwards.Add(Count("The Duke of Edinburgh's Award (Silver)", 1, "duke_of_edinburgh_silver"));
                b.AdditionalAwards.Add(Count("SES", 1, "ses"));
            })));
            Assert.AreEqual(43723086037085L, lines.Single(l => l.Name.StartsWith("Camper")).Shop!.VariantId);
            Assert.AreEqual(43723202461789L, lines.Single(l => l.Name.StartsWith("Walkabout")).Shop!.VariantId);
            Assert.AreEqual(43723099144285L, lines.Single(l => l.Name == "First Aid").Shop!.VariantId);
            Assert.AreEqual(43723096686685L, lines.Single(l => l.Name.Contains("Duke")).Shop!.VariantId);
            Assert.IsFalse(lines.Single(l => l.Name == "SES").InShop);
        }

        [TestMethod]
        public void IntroductionBadgesComeFromThoseInvestedInThePeriod()
        {
            var lines = BadgeOrderBuilder.Build(Report(Section("cub", invested: 5), Section("scout", invested: 0)));
            var line = lines.Single();
            Assert.AreEqual(5, line.Count);
            Assert.AreEqual(43723109466205L, line.Shop!.VariantId);
        }

        [TestMethod]
        public void PeakAwardsAreListedButNotMatchedToTheShop()
        {
            var line = BadgeOrderBuilder.Build(Report(Section("scout", b => b.PeakAwards = 2))).Single();
            Assert.AreEqual(BadgeOrderBuilder.PeakGroup, line.Group);
            Assert.AreEqual(2, line.Count);
            Assert.IsFalse(line.InShop);
        }

        [TestMethod]
        public void LinesAreInGroupOrderAndNothingAwardedMakesNoLines()
        {
            Assert.AreEqual(0, BadgeOrderBuilder.Build(Report(Section("scout"))).Count);
            var lines = BadgeOrderBuilder.Build(Report(Section("scout", b =>
            {
                b.PeakAwards = 1;
                b.Milestones.Add(Count("Milestone 1", 1));
                b.AdditionalAwards.Add(Count("Scout 1 Year of Service", 1, "yos_scout_1"));
                b.OasStages.Add(new OasStageCount { Stream = "Camping", Stage = 1, Count = 1 });
            })));
            CollectionAssert.AreEqual(
                new[] { BadgeOrderBuilder.YearsOfServiceGroup, BadgeOrderBuilder.MilestonesGroup, BadgeOrderBuilder.OasGroup, BadgeOrderBuilder.PeakGroup },
                lines.Select(l => l.Group).ToArray());
        }

        [TestMethod]
        public void TheCartLinkHasEachBadgeAndQuantityAndLeavesOutWhatTheShopDoesNotSell()
        {
            var lines = BadgeOrderBuilder.Build(Report(Section("scout", b =>
            {
                b.OasStages.Add(new OasStageCount { Stream = "Camping", Stage = 3, Count = 2 });
                b.AdditionalAwards.Add(Count("Scout 1 Year of Service", 8, "yos_scout_1"));
                b.AdditionalAwards.Add(Count("SES", 1, "ses"));
            })));
            var links = BadgeOrderBuilder.CartLinks(lines);
            Assert.AreEqual(1, links.Count);
            Assert.AreEqual("https://scoutshop.com.au/cart/43723173822557:8,43723152326749:2", links[0]);
        }

        [TestMethod]
        public void ALineWithNoneLeftIsNotInTheCartAndALongOrderIsSplit()
        {
            var lines = ScoutShopCatalog.All.Take(45).Select(v => new BadgeOrderLine { Group = "x", Name = v.Option, Count = 1, Shop = v }).ToList();
            lines[0].Count = 0;
            var links = BadgeOrderBuilder.CartLinks(lines);
            Assert.AreEqual(2, links.Count);
            Assert.AreEqual(40, links[0].Substring("https://scoutshop.com.au/cart/".Length).Split(',').Length);
            Assert.AreEqual(4, links[1].Substring("https://scoutshop.com.au/cart/".Length).Split(',').Length);
            Assert.IsFalse(links[0].Contains(lines[0].Shop!.VariantId.ToString() + ":"));
        }

        [TestMethod]
        public void TheCopiedListSaysWhatToOrderAndWhatIsNotInTheShop()
        {
            var lines = BadgeOrderBuilder.Build(Report(Section("scout", b =>
            {
                b.OasStages.Add(new OasStageCount { Stream = "Camping", Stage = 3, Count = 2 });
                b.AdditionalAwards.Add(Count("SES", 1, "ses"));
            })));
            // Outdoor Adventure Skills come before additional awards in the order
            Assert.AreEqual("2 x OAS Camping Badge: Stage 3\n1 x SES (not in the shop)\n", BadgeOrderBuilder.AsText(lines));
        }

        [TestMethod]
        public void TheLastOrderIsTheLatestRecordAgainstTheGroup()
        {
            var contents = new BadgeStoreContents
            {
                Records =
                {
                    new BadgeStoreRecord { MemberId = "group-1", AwardId = "order_last", Awarded = new DateTime(2026, 8, 14) },
                    new BadgeStoreRecord { MemberId = "someone", AwardId = "order_last", Awarded = new DateTime(2020, 1, 1) },
                    new BadgeStoreRecord { MemberId = "group-1", AwardId = "yos_start", Awarded = new DateTime(2026, 1, 25) }
                }
            };
            Assert.AreEqual(new DateTime(2026, 8, 14), BadgeOrderRecords.LastOrder(contents, "group-1"));
            Assert.IsNull(BadgeOrderRecords.LastOrder(contents, "another-group"));
            Assert.IsNull(BadgeOrderRecords.LastOrder(new BadgeStoreContents(), "group-1"));
        }

        [TestMethod]
        public void TheReportCountsCarryTheAwardIdForTheOrder()
        {
            var counts = AdditionalAwardCounts.Sorted(new Dictionary<string, int> { ["Scout 3 Years of Service"] = 2 }, new Dictionary<string, string> { ["Scout 3 Years of Service"] = "yos_scout_3" });
            Assert.AreEqual("yos_scout_3", counts.Single().Id);
        }
    }
}
