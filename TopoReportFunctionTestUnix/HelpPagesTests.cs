using Topo.Model.Help;

namespace TopoReportFunctionTest
{
    [TestClass]
    public class HelpPagesTests
    {
        [TestMethod]
        public void APageLinksToItsOwnWikiPage()
        {
            Assert.AreEqual("https://github.com/NomisNostab/Topo-Blazor/wiki/Members", HelpPages.UrlFor("members"));
            Assert.AreEqual("https://github.com/NomisNostab/Topo-Blazor/wiki/Home-Page", HelpPages.UrlFor(""));
        }

        [TestMethod]
        public void AddressesAreReadLeniently()
        {
            Assert.AreEqual("Members", HelpPages.PageFor("/members"));
            Assert.AreEqual("Members", HelpPages.PageFor("Members/"));
            Assert.AreEqual("Members", HelpPages.PageFor("members?unit=1"));
            Assert.AreEqual("Wallchart", HelpPages.PageFor("wallchart#top"));
            Assert.AreEqual("Home-Page", HelpPages.PageFor(null));
        }

        [TestMethod]
        public void SubPagesEachHaveTheirOwnWikiPage()
        {
            var subPages = new[] { "approvals/approvals", "approvals/pending", "approvals/massaward", "approvals/backup", "scoutinghistory/member", "scoutinghistory/unit", "programideas", "programideas/proposed", "progress/progress" };
            var wikiPages = subPages.Select(p => HelpPages.PageFor(p)).ToList();
            Assert.IsFalse(wikiPages.Any(p => p == null), "every sub page has one");
            Assert.AreEqual(subPages.Length, wikiPages.Distinct().Count(), "and no two share one");
        }

        [TestMethod]
        public void ThePatrolPlannerIsFoundAtEitherAddress()
        {
            Assert.AreEqual("Patrol-Planner", HelpPages.PageFor("patrolplanner"));
            Assert.AreEqual("Patrol-Planner", HelpPages.PageFor("members/patrolplanner"));
            Assert.AreEqual("Program-Ideas", HelpPages.PageFor("program/ideas"));
            Assert.AreEqual("Proposed-Activities", HelpPages.PageFor("program/proposed"));
        }

        [TestMethod]
        public void APageWithAnIdInItsAddressHasItsOwnWikiPage()
        {
            Assert.AreEqual("Personal-Progress-Details", HelpPages.PageFor("progress/details/4ce3a7c4-b98f-3062-8779-6ebd619ec4e3"));
            Assert.AreEqual("Personal-Progress-Details", HelpPages.PageFor("/Progress/Details/abc?x=1"));
            Assert.IsNull(HelpPages.PageFor("progress/details/"), "no id, no such page");
        }

        [TestMethod]
        public void PagesWithNoHelpHaveNoLink()
        {
            Assert.IsNull(HelpPages.UrlFor("logout"));
            Assert.IsNull(HelpPages.UrlFor("counter"));
            Assert.IsNull(HelpPages.UrlFor("no-such-page"));
        }

        [TestMethod]
        public void EveryWikiPageNameIsAValidWikiAddress()
        {
            Assert.IsTrue(HelpPages.AllWikiPages.Count >= 25);
            foreach (var page in HelpPages.AllWikiPages)
                Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(page, "^[A-Za-z0-9]+(-[A-Za-z0-9]+)*$"), $"'{page}' must be words joined by hyphens, with no slash or space");
        }

        [TestMethod]
        public void TheExistingWikiPagesKeepTheirNames()
        {
            // These are the pages the wiki already has, so their links must not change
            foreach (var page in new[] { "Home-Page", "Login", "Members", "Program", "OAS", "SIA", "Milestone", "Logbook", "Wallchart", "Additional-Awards", "Approvals", "Backup-Restore", "Personal-Progress", "Debug-Logs" })
                CollectionAssert.Contains(HelpPages.AllWikiPages.ToList(), page);
        }
    }
}
