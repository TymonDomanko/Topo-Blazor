using Topo.Model.SIA;
using Topo.Services;

namespace TopoReportFunctionTest
{
    [TestClass]
    public class SiaProjectsLayoutTests
    {
        private static SIAProjectListModel Project(string id, string name, string project, int month, string patrol = "") => new SIAProjectListModel
        {
            memberId = id, memberName = name, memberPatrol = patrol, area = "Environment", projectName = project, status = "In Progress", statusUpdated = new DateTime(2026, month, 10)
        };

        [TestMethod]
        public void EachScoutsProjectsAreTogetherOldestFirstAndScoutsFollowTheOrderGiven()
        {
            var sheets = SiaProjectsLayout.Build(new[]
            {
                Project("b", "Ben", "Kayak", 5), Project("a", "Amy", "Robot", 6), Project("b", "Ben", "Poetry", 2), Project("a", "Amy", "Compost", 1)
            }, false);

            var sheet = sheets.Single();
            Assert.AreEqual("", sheet.Patrol);
            CollectionAssert.AreEqual(new[] { "Ben", "Amy" }, sheet.Scouts.Select(s => s.Name).ToList());
            CollectionAssert.AreEqual(new[] { "b", "a" }, sheet.Scouts.Select(s => s.MemberId).ToList()); // each Scout is known by id, so they can be ticked
            CollectionAssert.AreEqual(new[] { "Poetry", "Kayak" }, sheet.Scouts[0].Projects.Select(p => p.projectName).ToList());
            CollectionAssert.AreEqual(new[] { "Compost", "Robot" }, sheet.Scouts[1].Projects.Select(p => p.projectName).ToList());
        }

        [TestMethod]
        public void TheListCanBeSplitIntoASheetForEachPatrol()
        {
            var projects = new[]
            {
                Project("a", "Amy", "Robot", 1, "Red"), Project("b", "Ben", "Kayak", 2, "Blue"), Project("c", "Cat", "Poetry", 3, ""), Project("a", "Amy", "Compost", 4, "Red")
            };

            var patrols = SiaProjectsLayout.Build(projects, true);

            CollectionAssert.AreEqual(new[] { "", "Blue", "Red" }, patrols.Select(s => s.Patrol).ToList()); // no patrol first
            CollectionAssert.AreEqual(new[] { "Cat" }, patrols[0].Scouts.Select(s => s.Name).ToList());
            CollectionAssert.AreEqual(new[] { "Robot", "Compost" }, patrols[2].Scouts.Single().Projects.Select(p => p.projectName).ToList());
            Assert.AreEqual(1, SiaProjectsLayout.Build(projects, false).Count);
        }

        [TestMethod]
        public void NoProjectsStillGivesOneEmptySheet()
        {
            foreach (var breakByPatrol in new[] { false, true })
            {
                var sheets = SiaProjectsLayout.Build(new List<SIAProjectListModel>(), breakByPatrol);
                Assert.AreEqual(1, sheets.Count);
                Assert.AreEqual(0, sheets.Single().Scouts.Count);
            }
        }

        [TestMethod]
        public void ProjectsAreShownUnderTheNamesGivenInTheOrderOfThoseNamesForTheScoutsNamed()
        {
            var all = new[]
            {
                Project("1", "Full Name One", "Robot", 1), Project("2", "Full Name Two", "Kayak", 2), Project("3", "Full Name Three", "Poetry", 3), Project("1", "Full Name One", "Compost", 4)
            };

            var named = SiaProjectsLayout.ForMembers(all, new Dictionary<string, string> { ["1"] = "Zed", ["2"] = "Amy B", ["4"] = "Nobody" });

            CollectionAssert.AreEqual(new[] { "Amy B", "Zed", "Zed" }, named.Select(p => p.memberName).ToList()); // those named only, in the order of their names
            Assert.IsTrue(all.All(p => p.memberName.StartsWith("Full Name"))); // the projects as read are not renamed
            Assert.AreEqual(0, SiaProjectsLayout.ForMembers(all, new Dictionary<string, string>()).Count);
            Assert.AreEqual(0, SiaProjectsLayout.ForMembers(new List<SIAProjectListModel>(), new Dictionary<string, string> { ["1"] = "Zed" }).Count);
        }

        [TestMethod]
        public void TheWorkbookHasASheetForTheUnitOrOneForEachPatrolNamedAndTitledByIt()
        {
            SetEnvironment.SetSyncfusionLicense();
            var projects = new List<SIAProjectListModel>
            {
                Project("a", "Amy", "Robot", 1, "Red"), Project("b", "Ben", "Kayak", 2, "Blue"), Project("c", "Cat", "Poetry", 3, "")
            };
            var service = new ReportService();

            var unit = service.GenerateSIAWorkbook(projects, "Group", "scout", "Scout Unit", true);
            Assert.AreEqual(1, unit.Worksheets.Count);
            StringAssert.StartsWith(unit.Worksheets[0].Range[3, 2].Text, "SIA Project List as at");
            Assert.AreEqual("Amy", unit.Worksheets[0].Range[4, 1].Text);

            var patrols = service.GenerateSIAWorkbook(projects, "Group", "scout", "Scout Unit", true, true);
            CollectionAssert.AreEqual(new[] { "Unit", "Blue", "Red" }, Enumerable.Range(0, patrols.Worksheets.Count).Select(i => patrols.Worksheets[i].Name).ToList());
            StringAssert.StartsWith(patrols.Worksheets[2].Range[3, 2].Text, "Red SIA Project List as at");
            Assert.AreEqual("Amy", patrols.Worksheets[2].Range[4, 1].Text);
            Assert.AreEqual("Robot", patrols.Worksheets[2].Range[5, 3].Text);
            Assert.AreEqual("Cat", patrols.Worksheets[0].Range[4, 1].Text);

            // nothing to list is still a workbook, with only its headings
            var empty = service.GenerateSIAWorkbook(new List<SIAProjectListModel>(), "Group", "scout", "Scout Unit", true, true);
            Assert.AreEqual(1, empty.Worksheets.Count);
            Assert.AreEqual("Scout Unit", empty.Worksheets[0].Range[2, 2].Text);
        }
    }
}
