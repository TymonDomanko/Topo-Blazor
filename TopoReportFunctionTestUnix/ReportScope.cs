using Topo.Model.Members;

namespace TopoReportFunctionTest
{
    [TestClass]
    public class ReportScopeTests
    {
        private static MemberListModel Person(string first, string last, bool ticked = false) =>
            new MemberListModel { id = first + last, first_name = first, last_name = last, selected = ticked };

        [TestMethod]
        public void TheReportIsForTheTickedScoutsOrEveryoneWhenNoneAreTicked()
        {
            var unit = new[] { Person("Amy", "Chen"), Person("Ben", "Lee"), Person("Cat", "Moss") };
            CollectionAssert.AreEqual(new[] { "Amy", "Ben", "Cat" }, ReportScope.Chosen(unit).Select(m => m.first_name).ToList());

            unit[1].selected = true;
            CollectionAssert.AreEqual(new[] { "Ben" }, ReportScope.Chosen(unit).Select(m => m.first_name).ToList());

            unit[2].selected = true;
            CollectionAssert.AreEqual(new[] { "Ben", "Cat" }, ReportScope.Chosen(unit).Select(m => m.first_name).ToList());
        }

        [TestMethod]
        public void ANameDoesNotChangeAsOtherScoutsAreTicked()
        {
            var unit = new[] { Person("Sam", "Brown"), Person("Sam", "Black"), Person("Amy", "Chen") };

            var everyone = ReportScope.Names(unit, false);
            Assert.AreEqual("Sam Br", everyone["SamBrown"]);
            Assert.AreEqual("Amy", everyone["AmyChen"]);

            // Brown alone is still told apart from the Black who is not in the report
            unit[0].selected = true;
            var justBrown = ReportScope.Names(unit, false);
            CollectionAssert.AreEqual(new[] { "SamBrown" }, justBrown.Keys.ToList());
            Assert.AreEqual("Sam Br", justBrown["SamBrown"]);

            unit[0].selected = false;
            unit[2].selected = true;
            Assert.AreEqual("Amy", ReportScope.Names(unit, false).Single().Value);
            Assert.AreEqual("Amy Chen", ReportScope.Names(unit, true).Single().Value);
        }

        [TestMethod]
        public void NobodyToReportOnIsNobody()
        {
            Assert.AreEqual(0, ReportScope.Chosen(new List<MemberListModel>()).Count);
            Assert.AreEqual(0, ReportScope.Names(new List<MemberListModel>(), false).Count);
        }
    }
}
