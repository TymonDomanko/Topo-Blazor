using Topo.Model.OAS;

namespace TopoReportFunctionTest
{
    [TestClass]
    public class OasWorksheetSelectionTests
    {
        // A run of stages of one branch, first to last, the first of which follows on from the parent branch
        private static IEnumerable<OASStageListModel> Run(string stream, string branch, string parent, string title, int from, int to) =>
            Enumerable.Range(from, to - from + 1).Select(stage => new OASStageListModel
            {
                Stream = stream,
                Branch = branch,
                ParentBranch = stage == from ? parent : branch,
                StageTitle = title,
                Stage = stage,
                TemplateLink = $"{stream}/{branch}/{stage}"
            });

        // A cut-down copy of Terrain's stage tree, in the order Terrain lists it
        private static readonly List<OASStageListModel> Tree = new[]
        {
            Run("Bushcraft", "bushcraft", "", "Bushcraft", 1, 3),
            Run("Bushcraft", "pioneering", "bushcraft", "Pioneering", 4, 9),
            Run("Bushcraft", "survival-skills", "bushcraft", "Survival Skills", 4, 9),
            Run("Bushwalking", "bushwalking", "", "Bushwalking", 1, 9),
            Run("Camping", "camping", "", "Camping", 1, 9),
            Run("Aquatics", "aquatics", "", "Aquatics", 1, 3),
            Run("Aquatics", "life-saving", "aquatics", "Life Saving", 4, 6),
            Run("Aquatics", "swift-water-rescue", "life-saving", "Swift Water Rescue", 7, 9),
            Run("Aquatics", "scuba", "snorkelling", "Scuba", 7, 9),
            Run("Aquatics", "snorkelling", "aquatics", "Snorkelling", 4, 6),
            Run("Paddling", "paddling", "", "Paddling", 1, 3),
            Run("Paddling", "canoeing", "paddling", "Canoeing", 4, 9),
            Run("Paddling", "kayaking", "paddling", "Kayaking", 4, 6),
            Run("Paddling", "sea-kayaking", "paddling", "Sea Kayaking", 4, 9)
        }.SelectMany(x => x).ToList();

        private static readonly HashSet<string> None = new HashSet<string>();

        private static HashSet<string> Branches(params (string stream, string branch)[] chosen) =>
            chosen.Select(c => OasWorksheetSelection.BranchKey(c.stream, c.branch)).ToHashSet();

        private static string[] Names(IEnumerable<OASStageListModel> stages) => stages.Select(s => $"{s.Branch} {s.Stage}").ToArray();

        [TestMethod]
        public void ASkillIsItselfAndCoreIsBushcraftBushwalkingAndCamping()
        {
            CollectionAssert.AreEqual(new[] { "Paddling" }, OasWorksheetSelection.StreamsFor("Paddling"));
            CollectionAssert.AreEqual(new[] { "Bushcraft", "Bushwalking", "Camping" }, OasWorksheetSelection.StreamsFor(OasWorksheetSelection.Core));
            Assert.AreEqual(0, OasWorksheetSelection.StreamsFor("").Count);
            Assert.AreEqual(9, OasWorksheetSelection.HighestLevel(Tree));
        }

        [TestMethod]
        public void BranchesAreOfferedOnlyWhereASkillHasMoreThanOneAtThatLevel()
        {
            Assert.AreEqual(0, OasWorksheetSelection.BranchesAt(Tree, new[] { "Paddling" }, 2).Count, "not branched yet");
            CollectionAssert.AreEqual(new[] { "Canoeing", "Kayaking", "Sea Kayaking" }, OasWorksheetSelection.BranchesAt(Tree, new[] { "Paddling" }, 5).Select(c => c.Title).ToArray());
            CollectionAssert.AreEqual(new[] { "Canoeing", "Sea Kayaking" }, OasWorksheetSelection.BranchesAt(Tree, new[] { "Paddling" }, 8).Select(c => c.Title).ToArray(), "kayaking stops at 6");
            Assert.AreEqual(0, OasWorksheetSelection.BranchesAt(Tree, new[] { "Bushwalking" }, 7).Count, "a skill that never branches");
        }

        [TestMethod]
        public void CoreOffersTheBranchesOfTheCoreSkillsThatBranch()
        {
            var choices = OasWorksheetSelection.BranchesAt(Tree, OasWorksheetSelection.StreamsFor(OasWorksheetSelection.Core), 4);
            CollectionAssert.AreEqual(new[] { "Pioneering", "Survival Skills" }, choices.Select(c => c.Title).ToArray());
            Assert.IsTrue(choices.All(c => c.Stream == "Bushcraft"));
        }

        [TestMethod]
        public void AloneTheLevelGivesOneStagePerChosenBranch()
        {
            var one = OasWorksheetSelection.Select(Tree, new[] { "Paddling" }, 5, false, Branches(("Paddling", "kayaking")), None);
            CollectionAssert.AreEqual(new[] { "kayaking 5" }, Names(one.Included));
            var two = OasWorksheetSelection.Select(Tree, new[] { "Paddling" }, 5, false, Branches(("Paddling", "kayaking"), ("Paddling", "canoeing")), None);
            CollectionAssert.AreEqual(new[] { "canoeing 5", "kayaking 5" }, Names(two.Included));
            Assert.AreEqual(0, OasWorksheetSelection.Select(Tree, new[] { "Paddling" }, 5, false, Branches(), None).Included.Count, "no branch ticked");
        }

        [TestMethod]
        public void ALevelBeforeTheSkillBranchesNeedsNoBranchTicked()
        {
            var result = OasWorksheetSelection.Select(Tree, new[] { "Paddling" }, 2, false, Branches(), None);
            CollectionAssert.AreEqual(new[] { "paddling 2" }, Names(result.Included));
        }

        [TestMethod]
        public void TheLevelsBelowFollowTheWayTheBranchWasReached()
        {
            var result = OasWorksheetSelection.Select(Tree, new[] { "Paddling" }, 5, true, Branches(("Paddling", "canoeing")), None);
            CollectionAssert.AreEqual(new[] { "paddling 1", "paddling 2", "paddling 3", "canoeing 4", "canoeing 5" }, Names(result.Included));
        }

        [TestMethod]
        public void ABranchThatCarriesOnFromAnotherIncludesTheBranchItGrewOutOf()
        {
            var result = OasWorksheetSelection.Select(Tree, new[] { "Aquatics" }, 8, true, Branches(("Aquatics", "swift-water-rescue")), None);
            CollectionAssert.AreEqual(new[] { "aquatics 1", "aquatics 2", "aquatics 3", "life-saving 4", "life-saving 5", "life-saving 6", "swift-water-rescue 7", "swift-water-rescue 8" }, Names(result.Included));
        }

        [TestMethod]
        public void TwoBranchesShareTheLevelsBeforeTheyForkOnce()
        {
            var result = OasWorksheetSelection.Select(Tree, new[] { "Paddling" }, 4, true, Branches(("Paddling", "canoeing"), ("Paddling", "kayaking")), None);
            CollectionAssert.AreEqual(new[] { "paddling 1", "paddling 2", "paddling 3", "canoeing 4", "kayaking 4" }, Names(result.Included));
        }

        [TestMethod]
        public void LevelsEveryoneHasBeenAwardedAreLeftOutBelowButNeverTheChosenLevel()
        {
            var everyone = new HashSet<string> { OasWorksheetSelection.StageKey("Paddling", "paddling", 1), OasWorksheetSelection.StageKey("Paddling", "paddling", 2), OasWorksheetSelection.StageKey("Paddling", "paddling", 3), OasWorksheetSelection.StageKey("Paddling", "paddling", 3) };
            var result = OasWorksheetSelection.Select(Tree, new[] { "Paddling" }, 3, true, Branches(), everyone);
            CollectionAssert.AreEqual(new[] { "paddling 3" }, Names(result.Included), "the level asked for stays");
            CollectionAssert.AreEqual(new[] { "paddling 1", "paddling 2" }, Names(result.LeftOut));
        }

        [TestMethod]
        public void ALevelOnlySomeHaveDoneIsKept()
        {
            var everyone = new HashSet<string> { OasWorksheetSelection.StageKey("Paddling", "paddling", 1) };
            var result = OasWorksheetSelection.Select(Tree, new[] { "Paddling" }, 3, true, Branches(), everyone);
            CollectionAssert.AreEqual(new[] { "paddling 2", "paddling 3" }, Names(result.Included));
        }

        [TestMethod]
        public void CoreMakesTheThreeCoreSkillsInTheirOrder()
        {
            var result = OasWorksheetSelection.Select(Tree, OasWorksheetSelection.StreamsFor(OasWorksheetSelection.Core), 4, true, Branches(("Bushcraft", "pioneering")), None);
            var streams = result.Included.Select(s => s.Stream).Distinct().ToArray();
            CollectionAssert.AreEqual(new[] { "Bushcraft", "Bushwalking", "Camping" }, streams);
            CollectionAssert.AreEqual(new[] { "bushcraft 1", "bushcraft 2", "bushcraft 3", "pioneering 4" }, Names(result.Included.Where(s => s.Stream == "Bushcraft")));
            CollectionAssert.AreEqual(new[] { "bushwalking 1", "bushwalking 2", "bushwalking 3", "bushwalking 4" }, Names(result.Included.Where(s => s.Stream == "Bushwalking")));
        }

        [TestMethod]
        public void NothingIsMadeBeforeALevelIsChosen()
        {
            Assert.AreEqual(0, OasWorksheetSelection.Select(Tree, new[] { "Paddling" }, 0, true, Branches(), None).Included.Count);
        }

        private static Result Awarded(string member, string stream, string branch, int stage, string status = "awarded") =>
            new Result { member_id = member, status = status, achievement_meta = new Achievement_Meta { stream = stream, branch = branch, stage = stage } };

        [TestMethod]
        public void OnlyWhatEveryMemberHasBeenAwardedCountsAsDoneByEveryone()
        {
            var records = new[]
            {
                Awarded("a", "paddling", "paddling", 1), Awarded("b", "paddling", "paddling", 1),
                Awarded("a", "paddling", "paddling", 2), Awarded("b", "paddling", "paddling", 2, "approved"),
                Awarded("a", "paddling", "paddling", 3), Awarded("b", "paddling", "paddling", 3, "draft_review"),
                Awarded("a", "paddling", "canoeing", 4)
            };
            var done = OasWorksheetSelection.AwardedToEveryone(new[] { "a", "b" }, records);
            Assert.IsTrue(done.Contains(OasWorksheetSelection.StageKey("Paddling", "paddling", 1)));
            Assert.IsTrue(done.Contains(OasWorksheetSelection.StageKey("Paddling", "paddling", 2)), "approved counts");
            Assert.IsFalse(done.Contains(OasWorksheetSelection.StageKey("Paddling", "paddling", 3)), "still in progress");
            Assert.IsFalse(done.Contains(OasWorksheetSelection.StageKey("Paddling", "canoeing", 4)), "only one of them");
        }

        [TestMethod]
        public void AMemberWithNoRecordsMeansNothingIsDoneByEveryone()
        {
            var records = new[] { Awarded("a", "paddling", "paddling", 1) };
            Assert.AreEqual(0, OasWorksheetSelection.AwardedToEveryone(new[] { "a", "b" }, records).Count);
            Assert.AreEqual(0, OasWorksheetSelection.AwardedToEveryone(new string[0], records).Count, "no members, nothing to leave out");
        }

        private static Dictionary<string, int> Highest(params (string stream, int stage)[] highest) => highest.ToDictionary(h => h.stream, h => h.stage);

        [TestMethod]
        public void OneAboveTheHighestIsTheStageAfterTheBestAnyoneHasInThatSkill()
        {
            var highest = Highest(("paddling", 5), ("bushcraft", 3));
            Assert.AreEqual(6, OasWorksheetSelection.LevelFor("Paddling", OasWorksheetSelection.OneAboveHighest, highest, 9));
            Assert.AreEqual(4, OasWorksheetSelection.LevelFor("Bushcraft", OasWorksheetSelection.OneAboveHighest, highest, 9));
            Assert.AreEqual(1, OasWorksheetSelection.LevelFor("Camping", OasWorksheetSelection.OneAboveHighest, highest, 9), "nobody has any");
            Assert.AreEqual(9, OasWorksheetSelection.LevelFor("Paddling", OasWorksheetSelection.OneAboveHighest, Highest(("paddling", 9)), 9), "never above the top");
            Assert.AreEqual(4, OasWorksheetSelection.LevelFor("Paddling", 4, highest, 9), "a chosen level is just that level");
            Assert.AreEqual(0, OasWorksheetSelection.LevelFor("Paddling", 0, highest, 9));
        }

        [TestMethod]
        public void TheHighestInTheUnitIsTheBestAwardedOrApprovedInAnyBranchByAnyMember()
        {
            var records = new[]
            {
                Awarded("a", "paddling", "paddling", 3), Awarded("a", "paddling", "canoeing", 5), Awarded("b", "paddling", "kayaking", 4),
                Awarded("b", "paddling", "kayaking", 6, "approved"), Awarded("b", "paddling", "sea-kayaking", 9, "draft_review"),
                Awarded("zz", "paddling", "canoeing", 8), Awarded("a", "camping", "camping", 2)
            };
            var highest = OasWorksheetSelection.HighestAwarded(new[] { "a", "b" }, records);
            Assert.AreEqual(6, highest["paddling"], "approved counts, a draft and a stranger do not");
            Assert.AreEqual(2, highest["camping"]);
            Assert.IsFalse(highest.ContainsKey("boating"));
        }

        [TestMethod]
        public void SelectingOneAboveTheHighestMakesEachSkillsOwnLevel()
        {
            var highest = Highest(("bushcraft", 3), ("bushwalking", 6));
            var result = OasWorksheetSelection.Select(Tree, OasWorksheetSelection.StreamsFor(OasWorksheetSelection.Core), OasWorksheetSelection.OneAboveHighest, false,
                Branches(("Bushcraft", "pioneering"), ("Bushcraft", "survival-skills")), None, highest);
            CollectionAssert.AreEqual(new[] { "pioneering 4", "survival-skills 4", "bushwalking 7", "camping 1" }, Names(result.Included).ToArray());
        }

        [TestMethod]
        public void OneAboveTheHighestStillTakesTheLevelsBelowAndTheBranchesAtThatLevel()
        {
            var highest = Highest(("paddling", 4));
            var choices = OasWorksheetSelection.BranchesAt(Tree, new[] { "Paddling" }, OasWorksheetSelection.OneAboveHighest, highest);
            CollectionAssert.AreEqual(new[] { "Canoeing", "Kayaking", "Sea Kayaking" }, choices.Select(c => c.Title).ToArray(), "the branches at level 5");
            var result = OasWorksheetSelection.Select(Tree, new[] { "Paddling" }, OasWorksheetSelection.OneAboveHighest, true, Branches(("Paddling", "canoeing")), None, highest);
            CollectionAssert.AreEqual(new[] { "paddling 1", "paddling 2", "paddling 3", "canoeing 4", "canoeing 5" }, Names(result.Included));
        }
    }
}
