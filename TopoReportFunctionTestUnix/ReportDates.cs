using Newtonsoft.Json;
using Topo.Model;

namespace TopoReportFunctionTest
{
    [TestClass]
    public class ReportDates
    {
        // 22:25 UTC on 26 Aug 2026: 8:25am on 27 Aug in Sydney, 6:25am in Perth, still the 26th in UTC
        private static readonly DateTime Moment = new DateTime(2026, 8, 26, 22, 25, 0, DateTimeKind.Utc);

        [TestMethod]
        public void ALocalOrUtcMomentBecomesTheUsersWallClockTimeWithNoTimezone()
        {
            var expected = Moment.ToLocalTime();
            foreach (var moment in new[] { Moment.ToLocalTime(), Moment })
            {
                var result = ReportDate.ForReport(moment);
                Assert.AreEqual(DateTimeKind.Unspecified, result.Kind);
                Assert.AreEqual(expected.Year, result.Year);
                Assert.AreEqual(expected.Month, result.Month);
                Assert.AreEqual(expected.Day, result.Day);
                Assert.AreEqual(expected.Hour, result.Hour);
                Assert.AreEqual(expected.Minute, result.Minute);
            }
        }

        [TestMethod]
        public void ADateThatHasNoKindIsKeptAsItIsAndNothingStaysNothing()
        {
            var plain = new DateTime(2026, 8, 27);
            Assert.AreEqual(plain, ReportDate.ForReport(plain));
            Assert.AreEqual(DateTimeKind.Unspecified, ReportDate.ForReport(plain).Kind);
            Assert.IsNull(ReportDate.ForReport((DateTime?)null));
            Assert.AreEqual(plain, ReportDate.ForReport((DateTime?)plain));
        }

        [TestMethod]
        public void TheDateIsSentWithoutAnOffsetSoTheFunctionInUtcReadsTheSameDay()
        {
            var sent = JsonConvert.SerializeObject(ReportDate.ForReport(Moment.ToLocalTime()));
            StringAssert.DoesNotMatch(sent, new System.Text.RegularExpressions.Regex(@"[+-]\d\d:\d\d""$|Z""$"));
            // whatever timezone the function runs in, it reads the same day and time back
            Assert.AreEqual(ReportDate.ForReport(Moment.ToLocalTime()), JsonConvert.DeserializeObject<DateTime>(sent));
        }
    }
}
