using Topo.Model.Members;
using Topo.Model.PatrolPlanner;

namespace TopoReportFunctionTest
{
    [TestClass]
    public class PatrolPush
    {
        private const string Group = "g1";
        private const string Unit = "u1";

        private static readonly UnitPatrolModel[] Patrols =
        {
            new UnitPatrolModel { id = "p-lyre", name = "Lyrebird (Blue)", unit_id = Unit },
            new UnitPatrolModel { id = "p-mag", name = "Magpie (Orange)", unit_id = Unit },
            new UnitPatrolModel { id = "p-old", name = "Old Patrol", unit_id = Unit }
        };

        // A Scout as Terrain shows them now
        private static Member InTerrain(string id, string patrolId = "", string patrolName = "", string patrolDuty = "member", string unitDuty = "member", bool council = false) => new Member
        {
            id = id,
            first_name = "First" + id,
            last_name = "Last" + id,
            unit = new Topo.Model.Members.Unit { id = Unit, duty = unitDuty, unit_council = council, group_id = Group },
            patrol = string.IsNullOrEmpty(patrolId) ? null! : new Topo.Model.Members.Patrol { id = patrolId, name = patrolName, duty = patrolDuty }
        };

        // A Scout on the board: loaded as (patrol, duty, council), now planned as the other values
        private static PatrolPlannerMember OnBoard(string id, string wasPatrol, string wasDuty, bool wasCouncil, string nowPatrol, string nowDuty, bool nowCouncil) => new PatrolPlannerMember
        {
            Id = id,
            FirstName = "First" + id,
            LastName = "Last" + id,
            OriginalPatrolName = wasPatrol,
            OriginalPatrolDuty = wasDuty,
            OriginalUnitCouncil = wasCouncil,
            PatrolName = nowPatrol,
            PatrolDuty = nowDuty,
            UnitCouncil = nowCouncil
        };

        private static PatrolPushPlan Plan(IEnumerable<PatrolPlannerMember> board, IEnumerable<Member> terrain, IEnumerable<string>? removed = null)
            => PatrolPushPlanner.Build(Unit, "fallback-group", board, terrain, Patrols, removed ?? new string[0]);

        [TestMethod]
        public void AMoveRemovesFromTheOldPatrolBeforeAddingToTheNewOne()
        {
            var plan = Plan(new[] { OnBoard("a", "Lyrebird (Blue)", "", false, "Magpie (Orange)", "", false) },
                new[] { InTerrain("a", "p-lyre", "Lyrebird (Blue)") });

            var steps = plan.Members.Single().Steps;
            Assert.AreEqual(2, steps.Count);
            Assert.AreEqual(PatrolPushStepKind.RemoveFromPatrol, steps[0].Kind);
            Assert.AreEqual("p-lyre", steps[0].PatrolId);
            Assert.AreEqual(PatrolPushStepKind.AddToPatrol, steps[1].Kind);
            Assert.AreEqual("p-mag", steps[1].PatrolId);
            Assert.AreEqual("member", steps[1].Duty);
            Assert.AreEqual("Lyrebird (Blue) → Magpie (Orange)", plan.Members.Single().Summary);
        }

        [TestMethod]
        public void AMemberWhoIsNotChangedIsNotWritten()
        {
            var plan = Plan(new[] { OnBoard("a", "Lyrebird (Blue)", "", false, "Lyrebird (Blue)", "", false) },
                new[] { InTerrain("a", "p-lyre", "Lyrebird (Blue)") });

            Assert.IsFalse(plan.HasWork);
            Assert.AreEqual(0, plan.WriteCount);
        }

        [TestMethod]
        public void OnlyTheDutyChangesSoOnlyTheDutyIsWrittenInThePatrolTheyAreAlreadyIn()
        {
            var plan = Plan(new[] { OnBoard("a", "Lyrebird (Blue)", "", false, "Lyrebird (Blue)", "PL", false) },
                new[] { InTerrain("a", "p-lyre", "Lyrebird (Blue)") });

            var step = plan.Members.Single().Steps.Single();
            Assert.AreEqual(PatrolPushStepKind.SetPatrolDuty, step.Kind);
            Assert.AreEqual("patrol_leader", step.Duty);
            Assert.AreEqual("p-lyre", step.PatrolId);
        }

        [TestMethod]
        public void PromotingToAplAlsoWritesTheUnitCouncilTheBoardTurnedOn()
        {
            var plan = Plan(new[] { OnBoard("a", "Lyrebird (Blue)", "", false, "Lyrebird (Blue)", "APL", true) },
                new[] { InTerrain("a", "p-lyre", "Lyrebird (Blue)") });

            var steps = plan.Members.Single().Steps;
            Assert.AreEqual(2, steps.Count);
            Assert.AreEqual(PatrolPushStepKind.SetPatrolDuty, steps[0].Kind);
            Assert.AreEqual("assistant_patrol_leader", steps[0].Duty);
            Assert.AreEqual(PatrolPushStepKind.SetUnitDuty, steps[1].Kind);
            Assert.AreEqual("member", steps[1].Duty);
            Assert.IsTrue(steps[1].UnitCouncil);
        }

        [TestMethod]
        public void OnlyTheCouncilChangesSoOnlyTheUnitIsWritten()
        {
            var plan = Plan(new[] { OnBoard("a", "Lyrebird (Blue)", "", false, "Lyrebird (Blue)", "", true) },
                new[] { InTerrain("a", "p-lyre", "Lyrebird (Blue)") });

            var step = plan.Members.Single().Steps.Single();
            Assert.AreEqual(PatrolPushStepKind.SetUnitDuty, step.Kind);
            Assert.IsTrue(step.UnitCouncil);
            Assert.AreEqual("joins Unit Council", plan.Members.Single().Summary);
        }

        [TestMethod]
        public void BecomingUnitLeaderChangesTheUnitDutyAndLeavesThePatrolDutyAlone()
        {
            var plan = Plan(new[] { OnBoard("a", "Lyrebird (Blue)", "PL", false, "Lyrebird (Blue)", "UL", true) },
                new[] { InTerrain("a", "p-lyre", "Lyrebird (Blue)", "patrol_leader") });

            var step = plan.Members.Single().Steps.Single();
            Assert.AreEqual(PatrolPushStepKind.SetUnitDuty, step.Kind);
            Assert.AreEqual("unit_leader", step.Duty);
            Assert.IsTrue(step.UnitCouncil);
        }

        [TestMethod]
        public void NoLongerBeingUnitLeaderPutsTheUnitDutyBackToMemberAndClearsAHiddenPatrolDuty()
        {
            // as Unit Leader the board shows UL, but Terrain still holds the patrol duty underneath
            var plan = Plan(new[] { OnBoard("a", "Lyrebird (Blue)", "UL", true, "Lyrebird (Blue)", "", true) },
                new[] { InTerrain("a", "p-lyre", "Lyrebird (Blue)", "patrol_leader", "unit_leader", true) });

            var steps = plan.Members.Single().Steps;
            Assert.AreEqual(2, steps.Count);
            Assert.AreEqual(PatrolPushStepKind.SetPatrolDuty, steps[0].Kind);
            Assert.AreEqual("member", steps[0].Duty);
            Assert.AreEqual(PatrolPushStepKind.SetUnitDuty, steps[1].Kind);
            Assert.AreEqual("member", steps[1].Duty);
        }

        [TestMethod]
        public void SomeoneWithNoPatrolJustJoinsOne()
        {
            var plan = Plan(new[] { OnBoard("a", "", "", false, "Magpie (Orange)", "", false) }, new[] { InTerrain("a") });

            var step = plan.Members.Single().Steps.Single();
            Assert.AreEqual(PatrolPushStepKind.AddToPatrol, step.Kind);
            Assert.AreEqual("join Magpie (Orange)", plan.Members.Single().Summary);
        }

        [TestMethod]
        public void SomeoneMovedToNoPatrolIsJustRemoved()
        {
            var plan = Plan(new[] { OnBoard("a", "Lyrebird (Blue)", "", false, "", "", false) },
                new[] { InTerrain("a", "p-lyre", "Lyrebird (Blue)") });

            var step = plan.Members.Single().Steps.Single();
            Assert.AreEqual(PatrolPushStepKind.RemoveFromPatrol, step.Kind);
            Assert.AreEqual("leave Lyrebird (Blue)", plan.Members.Single().Summary);
        }

        [TestMethod]
        public void APatrolTerrainHasntGotIsCreatedOnlyBecauseSomeoneIsMovingIntoIt()
        {
            var plan = Plan(new[]
            {
                OnBoard("a", "Lyrebird (Blue)", "", false, "Brand New", "", false),
                OnBoard("b", "Lyrebird (Blue)", "", false, "Brand New", "", false)
            }, new[] { InTerrain("a", "p-lyre", "Lyrebird (Blue)"), InTerrain("b", "p-lyre", "Lyrebird (Blue)") });

            CollectionAssert.AreEqual(new[] { "Brand New" }, plan.PatrolsToCreate);
            Assert.IsTrue(plan.Members.All(m => m.Steps.Single(s => s.Kind == PatrolPushStepKind.AddToPatrol).PatrolId == ""));
            Assert.AreEqual(1 + 4, plan.WriteCount);
        }

        [TestMethod]
        public void AMemberChangedInTerrainSinceTheBoardLoadedIsLeftAloneAndSaid()
        {
            // the board loaded them in Lyrebird, but Terrain now has them in Magpie
            var plan = Plan(new[] { OnBoard("a", "Lyrebird (Blue)", "", false, "Magpie (Orange)", "PL", false) },
                new[] { InTerrain("a", "p-mag", "Magpie (Orange)") });

            Assert.IsFalse(plan.HasWork);
            Assert.AreEqual(1, plan.Skipped.Count);
            StringAssert.Contains(plan.Skipped[0].Reason, "changed in Terrain");
            StringAssert.Contains(plan.Skipped[0].Reason, "Magpie (Orange)");
        }

        [TestMethod]
        public void ADutyOrCouncilChangedInTerrainCountsAsChangedToo()
        {
            var dutyChanged = Plan(new[] { OnBoard("a", "Lyrebird (Blue)", "", false, "Magpie (Orange)", "", false) },
                new[] { InTerrain("a", "p-lyre", "Lyrebird (Blue)", "patrol_leader") });
            var councilChanged = Plan(new[] { OnBoard("a", "Lyrebird (Blue)", "", false, "Magpie (Orange)", "", false) },
                new[] { InTerrain("a", "p-lyre", "Lyrebird (Blue)", council: true) });

            Assert.AreEqual(1, dutyChanged.Skipped.Count);
            Assert.AreEqual(1, councilChanged.Skipped.Count);
        }

        [TestMethod]
        public void AMemberNoLongerInTheUnitIsLeftAlone()
        {
            var plan = Plan(new[] { OnBoard("gone", "Lyrebird (Blue)", "", false, "Magpie (Orange)", "", false) },
                new[] { InTerrain("someone-else") });

            Assert.IsFalse(plan.HasWork);
            StringAssert.Contains(plan.Skipped.Single().Reason, "isn't in this unit");
        }

        [TestMethod]
        public void TheConflictOfOneMemberDoesNotStopTheOthers()
        {
            var plan = Plan(new[]
            {
                OnBoard("a", "Lyrebird (Blue)", "", false, "Magpie (Orange)", "", false),
                OnBoard("b", "Lyrebird (Blue)", "", false, "Magpie (Orange)", "", false)
            }, new[] { InTerrain("a", "p-mag", "Magpie (Orange)"), InTerrain("b", "p-lyre", "Lyrebird (Blue)") });

            Assert.AreEqual("Firstb Lastb", plan.Members.Single().MemberName);
            Assert.AreEqual("Firsta Lasta", plan.Skipped.Single().MemberName);
        }

        [TestMethod]
        public void APatrolRemovedOnTheBoardIsNotDeletedAndTheLeaderIsTold()
        {
            var plan = Plan(new[] { OnBoard("a", "Old Patrol", "", false, "", "", false) },
                new[] { InTerrain("a", "p-old", "Old Patrol") }, removed: new[] { "Old Patrol", "Never Existed" });

            Assert.IsTrue(plan.Notes.Any(n => n.StartsWith("Old Patrol was removed on the board")));
            Assert.IsFalse(plan.Notes.Any(n => n.Contains("Never Existed")));
            Assert.IsFalse(plan.Members.SelectMany(m => m.Steps).Any(s => s.Kind != PatrolPushStepKind.RemoveFromPatrol));
        }

        [TestMethod]
        public void TheWriteGoesToTheGroupTheUnitIsIn()
        {
            var plan = Plan(new[] { OnBoard("a", "Lyrebird (Blue)", "", false, "Magpie (Orange)", "", false) },
                new[] { InTerrain("a", "p-lyre", "Lyrebird (Blue)") });
            Assert.AreEqual(Group, plan.GroupId);

            var noGroup = PatrolPushPlanner.Build(Unit, "fallback-group", new[] { OnBoard("a", "", "", false, "Magpie (Orange)", "", false) },
                new[] { new Member { id = "a", unit = new Topo.Model.Members.Unit { id = Unit } } }, Patrols, new string[0]);
            Assert.AreEqual("fallback-group", noGroup.GroupId);
        }

        [TestMethod]
        public void TerrainsWordsMapToTheBoardsWords()
        {
            Assert.AreEqual("UL", PatrolPushPlanner.BoardDuty("unit_leader", "patrol_leader"));
            Assert.AreEqual("PL", PatrolPushPlanner.BoardDuty("member", "patrol_leader"));
            Assert.AreEqual("APL", PatrolPushPlanner.BoardDuty("member", "assistant_patrol_leader"));
            Assert.AreEqual("", PatrolPushPlanner.BoardDuty("member", "member"));
            Assert.AreEqual("", PatrolPushPlanner.BoardDuty("member", ""));
        }

        [TestMethod]
        public void TheResultOfAWriteSaysWhatWentWrong()
        {
            Assert.IsTrue(new TerrainWriteResult { StatusCode = 204 }.Success);
            Assert.IsTrue(new TerrainWriteResult { StatusCode = 403 }.NotAllowed);
            StringAssert.Contains(new TerrainWriteResult { StatusCode = 403 }.Message, "permission");
            StringAssert.Contains(new TerrainWriteResult { StatusCode = 0 }.Message, "could not be reached");
            StringAssert.Contains(new TerrainWriteResult { StatusCode = 400 }.Message, "400");
            Assert.IsFalse(new TerrainWriteResult { StatusCode = 400 }.NotAllowed);
        }
    }
}
