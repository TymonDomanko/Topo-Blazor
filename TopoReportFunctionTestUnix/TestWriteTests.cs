using System.Net;
using Topo.Model.TestBuild;

namespace TopoReportFunctionTest
{
    [TestClass]
    public class TestWriteTests
    {
        // Stands in for the network, noting what reached it
        private class Network : HttpMessageHandler
        {
            public int Sent;

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Sent++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }
        }

        [TestMethod]
        public async Task AHeldBackChangeNeverReachesTheNetwork()
        {
            var settings = new TestWriteSettings();
            var heldBack = new List<HttpRequestMessage>();
            var network = new Network();
            using var client = new HttpClient(new TestWriteGuardHandler(() => Task.FromResult(settings), heldBack.Add) { InnerHandler = network });
            var address = Terrain("achievements", "submissions/s1/assessments");

            var refused = await client.PostAsync(address, new StringContent("{}"));

            Assert.AreEqual(HttpStatusCode.Forbidden, refused.StatusCode);
            Assert.AreEqual(0, network.Sent);
            Assert.AreEqual(1, heldBack.Count);

            settings.AllowWrites = true;
            var sent = await client.PostAsync(address, new StringContent("{}"));

            Assert.AreEqual(HttpStatusCode.NoContent, sent.StatusCode);
            Assert.AreEqual(1, network.Sent);
        }

        private static readonly TestWriteSettings AsShipped = new TestWriteSettings();
        private static Uri Terrain(string host, string path) => new Uri($"https://{host}.terrain.scouts.com.au/{path}");

        [TestMethod]
        [DataRow("GET", "achievements", "units/u1/achievements?type=additional_award")]
        [DataRow("PUT", "events", "members/m1/calendars")]
        [DataRow("POST", "members", "members/m1/assume-profiles")]
        [DataRow("POST", "members", "revoke-assumed-profiles")]
        public void WhatReadingNeedsIsAlwaysSent(string method, string host, string path)
        {
            Assert.IsTrue(TestWriteRules.Allows(AsShipped, method, Terrain(host, path), false));
        }

        [TestMethod]
        public void RequestsOutsideTerrainAreSent()
        {
            Assert.IsTrue(TestWriteRules.Allows(AsShipped, "POST", new Uri("https://cognito-idp.ap-southeast-2.amazonaws.com/"), false));
            Assert.IsTrue(TestWriteRules.Allows(AsShipped, "PUT", new Uri("https://abc.lambda-url.ap-southeast-2.on.aws/"), false));
        }

        [TestMethod]
        [DataRow("PUT", "achievements", "units/u1/achievements?type=additional_award")]
        [DataRow("POST", "achievements", "submissions/s1/assessments")]
        [DataRow("POST", "events", "units/u1/events")]
        [DataRow("PATCH", "events", "events/e1")]
        [DataRow("DELETE", "events", "events/e1")]
        [DataRow("POST", "events", "events/e1/comments")]
        [DataRow("POST", "events", "groups/g1/events")]
        [DataRow("POST", "members", "groups/g1/patrols")]
        [DataRow("PUT", "members", "groups/g1/patrols/p1/members/m1")]
        [DataRow("DELETE", "members", "groups/g1/patrols/p1/members/m1")]
        [DataRow("PUT", "members", "groups/g1/units/u1/members/m1")]
        public void ChangesAreHeldBackToStartWith(string method, string host, string path)
        {
            Assert.IsFalse(TestWriteRules.Allows(AsShipped, method, Terrain(host, path), false));
            Assert.IsTrue(TestWriteRules.Allows(new TestWriteSettings { AllowWrites = true }, method, Terrain(host, path), false));
        }

        [TestMethod]
        [DataRow("POST", "groups/g1/events")]
        [DataRow("POST", "events/e1/comments")]
        [DataRow("DELETE", "events/e1/comments/c1")]
        public void YearsOfServiceHasItsOwnSwitch(string method, string path)
        {
            Assert.IsTrue(TestWriteRules.Allows(AsShipped, method, Terrain("events", path), true));
            Assert.IsFalse(TestWriteRules.Allows(new TestWriteSettings { AllowYearsOfService = false }, method, Terrain("events", path), true));
        }

        [TestMethod]
        public void YearsOfServiceDoesNotLetOtherChangesThrough()
        {
            Assert.IsFalse(TestWriteRules.Allows(AsShipped, "PUT", Terrain("achievements", "units/u1/achievements"), true));
            Assert.IsFalse(TestWriteRules.Allows(AsShipped, "DELETE", Terrain("events", "events/e1"), true));
        }

        [TestMethod]
        public async Task TheYearsOfServiceMarkFollowsTheWorkAndEndsWithIt()
        {
            Assert.IsFalse(YearsOfServiceWrites.Active);
            using (YearsOfServiceWrites.Begin())
            {
                await Task.Yield();
                Assert.IsTrue(YearsOfServiceWrites.Active);
            }
            Assert.IsFalse(YearsOfServiceWrites.Active);
        }
    }
}
