using Topo.Model.Cache;

namespace TopoReportFunctionTest
{
    // What each request Topo can write to Terrain with drops from the data cache
    [TestClass]
    public class WriteEffectsTests
    {
        private const string Members = "https://members.terrain.scouts.com.au/";
        private const string Events = "https://events.terrain.scouts.com.au/";
        private const string Achievements = "https://achievements.terrain.scouts.com.au/";
        private const string Templates = "https://templates.terrain.scouts.com.au/";

        private static readonly Dictionary<string, string> Held = new Dictionary<string, string>
        {
            ["members"] = $"me|g|get|{Members}units/u1/members",
            ["metrics"] = $"me|g|get|{Members}units/u1/metrics",
            ["unit records"] = $"me|g|get|{Achievements}units/u1/achievements?type=milestone",
            ["approvals"] = $"me|g|get|{Achievements}units/u1/submissions?status=pending",
            ["scout records"] = $"me|g|get|{Achievements}members/m1/achievements?type=milestone",
            ["logbook"] = $"me|g|get|{Achievements}members/m1/logbook",
            ["logbook entry"] = $"me|g|get|{Achievements}members/m1/logbook/entry1",
            ["another logbook"] = $"me|g|get|{Achievements}members/m2/logbook",
            ["totals"] = "me|g|logbook-totals|m1",
            ["another's totals"] = "me|g|logbook-totals|m2",
            ["event"] = $"me|g|get|{Events}events/e1",
            ["another event"] = $"me|g|get|{Events}events/e2",
            ["proposals"] = $"me|g|get|{Events}units/u1/proposals",
            ["templates"] = $"me|g|get|{Templates}oas/camping/tree.json"
        };

        private static string[] Dropped(string method, string uri)
        {
            var affected = WriteEffects.Of(method, uri);
            return affected == null ? Array.Empty<string>() : Held.Where(h => affected(h.Value)).Select(h => h.Key).ToArray();
        }

        [TestMethod]
        public void ReadsSigningInChoosingCalendarsAndAssumingProfilesDropNothing()
        {
            Assert.AreEqual(0, Dropped("GET", $"{Achievements}units/u1/achievements?type=milestone").Length);
            Assert.AreEqual(0, Dropped("POST", "https://cognito-idp.ap-southeast-2.amazonaws.com/").Length);
            Assert.AreEqual(0, Dropped("PUT", $"{Events}members/me/calendars").Length);
            Assert.AreEqual(0, Dropped("POST", $"{Members}members/m1/assume-profiles").Length);
            Assert.AreEqual(0, Dropped("POST", $"{Members}revoke-assumed-profiles").Length);
            Assert.AreEqual(0, Dropped("PUT", "https://abc.lambda-url.ap-southeast-2.on.aws/").Length);
        }

        [TestMethod]
        public void RecordingAwardsOrAssessingASubmissionDropsRecordsApprovalsAndUnitFigures()
        {
            var expected = new[] { "metrics", "unit records", "approvals", "scout records" };

            CollectionAssert.AreEquivalent(expected, Dropped("PUT", $"{Achievements}units/u1/achievements?type=additional_award"));
            CollectionAssert.AreEquivalent(expected, Dropped("POST", $"{Achievements}submissions/s1/assessments"));
        }

        [TestMethod]
        public void WritingALogbookDropsThatScoutsLogbookAndTotalsOnly()
        {
            CollectionAssert.AreEquivalent(new[] { "logbook", "logbook entry", "totals" }, Dropped("POST", $"{Achievements}members/m1/logbook"));
            CollectionAssert.AreEquivalent(new[] { "logbook", "logbook entry", "totals" }, Dropped("DELETE", $"{Achievements}members/m1/logbook/entry1"));
        }

        [TestMethod]
        public void CreatingAnEventDropsTheProposals()
        {
            CollectionAssert.AreEquivalent(new[] { "proposals" }, Dropped("POST", $"{Events}units/u1/events"));
            CollectionAssert.AreEquivalent(new[] { "proposals" }, Dropped("POST", $"{Events}groups/g/events"));
        }

        [TestMethod]
        public void ChangingOrRemovingAnEventAlsoDropsItAndWhatItsAttendanceFeeds()
        {
            var expected = new[] { "event", "proposals", "metrics", "unit records", "approvals", "scout records" };

            CollectionAssert.AreEquivalent(expected, Dropped("PATCH", $"{Events}events/e1"));
            CollectionAssert.AreEquivalent(expected, Dropped("DELETE", $"{Events}events/e1"));
        }

        [TestMethod]
        public void ACommentOnAnEventDropsThatEventButNoRecords()
        {
            var expected = new[] { "event", "proposals" };

            CollectionAssert.AreEquivalent(expected, Dropped("POST", $"{Events}events/e1/comments"));
            CollectionAssert.AreEquivalent(expected, Dropped("DELETE", $"{Events}events/e1/comments/c9"));
        }

        [TestMethod]
        public void ChangingPatrolsOrDutiesDropsMemberListsAndUnitFigures()
        {
            var expected = new[] { "members", "metrics" };

            CollectionAssert.AreEquivalent(expected, Dropped("POST", $"{Members}groups/g/patrols"));
            CollectionAssert.AreEquivalent(expected, Dropped("PUT", $"{Members}groups/g/patrols/p1/members/m1"));
            CollectionAssert.AreEquivalent(expected, Dropped("DELETE", $"{Members}groups/g/patrols/p1/members/m1"));
            CollectionAssert.AreEquivalent(expected, Dropped("PUT", $"{Members}groups/g/units/u1/members/m1"));
        }

        [TestMethod]
        public void AWriteThatIsNotKnownDropsEverything()
        {
            Assert.AreEqual(Held.Count, Dropped("POST", "https://metrics.terrain.scouts.com.au/something").Length);
        }
    }
}
