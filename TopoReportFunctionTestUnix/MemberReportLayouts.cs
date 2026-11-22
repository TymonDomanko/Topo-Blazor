using Topo.Model.Members;
using Topo.Services;

namespace TopoReportFunctionTest
{
    [TestClass]
    public class MemberReportLayoutsTests
    {
        private static MemberListModel Person(string first, string last = "Smith", string patrol = "Red", string duty = "", int adult = 0, bool council = false, string age = "12y 0m") => new MemberListModel
        {
            id = first + last + patrol, first_name = first, last_name = last, patrol_name = patrol, patrol_duty = duty, isAdultLeader = adult, unit_council = council, age = age, member_number = "N" + first
        };

        [TestMethod]
        public void TheOldestAgeInEachSection()
        {
            Assert.AreEqual(8, MemberReportLayouts.MaxAge("joey"));
            Assert.AreEqual(11, MemberReportLayouts.MaxAge("cub"));
            Assert.AreEqual(15, MemberReportLayouts.MaxAge("scout"));
            Assert.AreEqual(18, MemberReportLayouts.MaxAge("venturer"));
            Assert.AreEqual(26, MemberReportLayouts.MaxAge("rover"));
            Assert.AreEqual(26, MemberReportLayouts.MaxAge("")); // a section that isn't known is judged as a rover
        }

        [TestMethod]
        public void TheLastYearsInTheSectionAreShadedByAge()
        {
            var rows = MemberReportLayouts.MemberList(new[]
            {
                Person("Young", age: "11y 3m"), Person("Third", age: "12y 0m"), Person("SecondLast", age: "13y 6m"), Person("Before", age: "14y 1m"), Person("Last", age: "15y 11m")
            }, "scout");

            CollectionAssert.AreEqual(new[] { AgeBand.None, AgeBand.None, AgeBand.SecondLast, AgeBand.Last, AgeBand.Last }, rows.Select(r => r.AgeBand).ToList());
            // the same ages are further from the end in an older section
            Assert.IsTrue(MemberReportLayouts.MemberList(new[] { Person("Amy", age: "15y 0m") }, "venturer").All(r => r.AgeBand == AgeBand.None));
        }

        [TestMethod]
        public void TheMemberListHasEachMembersDetailsWithThePatrolTrimmedAndNoAgeFailing()
        {
            var row = MemberReportLayouts.MemberList(new[] { Person("Amy", "Brown", " Blue ", "PL", age: null!) }, "scout").Single();

            Assert.AreEqual("Amy", row.FirstName);
            Assert.AreEqual("Brown", row.LastName);
            Assert.AreEqual("NAmy", row.MemberNumber);
            Assert.AreEqual("PL", row.Duty);
            Assert.AreEqual("Blue", row.Patrol);
            Assert.AreEqual("", row.Age); // an age that is missing is shown as nothing, not as a failure
            Assert.AreEqual(AgeBand.None, row.AgeBand);
            Assert.AreEqual(0, MemberReportLayouts.MemberList(new List<MemberListModel>(), "scout").Count);
        }

        [TestMethod]
        public void TheListsAreMadeFromTheYouthUnlessLeadersAreAskedFor()
        {
            var everyone = new[]
            {
                Person("Zoe", patrol: "Blue", duty: "UL"), // the unit leader is a youth member
                Person("Amy", patrol: "Red", duty: "PL"),
                Person("Sam", "Lead", patrol: "Adult Leaders - Scouts", duty: "SL", adult: 1)
            };

            CollectionAssert.AreEqual(new[] { "Zoe", "Amy" }, MemberReportLayouts.PatrolListMembers(everyone, false, true).Select(m => m.first_name).ToList());
            CollectionAssert.AreEqual(new[] { "Sam", "Zoe", "Amy" }, MemberReportLayouts.PatrolListMembers(everyone, true, true).Select(m => m.first_name).ToList()); // by patrol name
            CollectionAssert.AreEqual(new[] { "Amy", "Zoe" }, MemberReportLayouts.MemberListMembers(everyone, MemberListSort.Alphabetical, true).Select(m => m.first_name).ToList()); // youth only
        }

        [TestMethod]
        public void EachPatrolListsItsLeadersFirstAndPatrolsFollowTheOrderGiven()
        {
            var members = new[]
            {
                Person("Ben", patrol: "Blue"), Person("Cat", patrol: "Blue", duty: "APL"), Person("Dan", patrol: "Blue", duty: "PL"),
                Person("Eve", patrol: "Red"), Person("Fay", patrol: ""), Person("Gus", patrol: "Red", duty: "PL")
            };

            var patrols = MemberReportLayouts.Patrols(members);

            CollectionAssert.AreEqual(new[] { "Blue", "Red", "" }, patrols.Select(p => p.Name).ToList());
            Assert.AreEqual("Unassigned", patrols[2].DisplayName); // a patrol-less group is named rather than left blank
            Assert.AreEqual("Blue", patrols[0].DisplayName);
            CollectionAssert.AreEqual(new[] { "Dan", "Cat", "Ben" }, patrols[0].Members.Select(m => m.FirstName).ToList()); // PL, APL, then the rest
            CollectionAssert.AreEqual(new[] { "Gus", "Eve" }, patrols[1].Members.Select(m => m.FirstName).ToList());
            Assert.IsTrue(patrols[0].Members[0].HasDuty);
            Assert.IsFalse(patrols[0].Members[2].HasDuty);
        }

        [TestMethod]
        public void TheUnitLeaderIsYouthAndSoIsInAPatrolNotWithTheAdults()
        {
            var members = new[] { Person("Zoe", patrol: "Red", duty: "UL"), Person("Sam", "Lead", patrol: "Adult Leaders - Scouts", duty: "SL", adult: 1) };

            var layout = MemberReportLayouts.PatrolList(members, true);

            CollectionAssert.AreEqual(new[] { "Zoe" }, layout.Patrols.SelectMany(p => p.Members).Select(m => m.FirstName).ToList());
            CollectionAssert.AreEqual(new[] { "Sam" }, layout.AdultLeaders!.Select(m => m.FirstName).ToList());
        }

        [TestMethod]
        public void TheUnitCouncilIsYouthFirstThenByNameAndAdultLeadersOnlyWhenAsked()
        {
            var members = new[]
            {
                Person("Zed", council: true), Person("Amy", council: true), Person("Not", council: false),
                Person("Adult", "B", adult: 1, council: true), Person("Another", "A", adult: 1), Person("Zoe", "A", adult: 1)
            };

            var without = MemberReportLayouts.PatrolList(members, false);
            CollectionAssert.AreEqual(new[] { "Amy", "Zed", "Adult" }, without.UnitCouncil.Select(m => m.FirstName).ToList());
            Assert.IsNull(without.AdultLeaders);

            var with = MemberReportLayouts.PatrolList(members, true);
            CollectionAssert.AreEqual(new[] { "Adult", "Another", "Zoe" }, with.AdultLeaders!.Select(m => m.FirstName).ToList());
        }

        [TestMethod]
        public void PatrolSheetsNameScoutsByFirstNameWithEnoughSurnameToTellThemApartUnlessFullNamesAreAsked()
        {
            var members = new[]
            {
                Person("Sam", "Brown", "Red"), Person("Sam", "Black", "Red"), Person("Amy", "Chen", "Blue"),
                Person("Pat", "Lee", "Red"), Person("Pat", "Lee", "Blue"), // the same name twice
                Person("Lead", "Adult", "Adult Leaders", adult: 1)
            };

            var shortened = MemberReportLayouts.PatrolSheetMembers(members, false);
            string Name(MemberListModel m) => $"{m.first_name} {m.last_name}".Trim();

            // youth only, by patrol; a first name that is not shared stands alone, shared ones get letters of the surname
            CollectionAssert.AreEqual(new[] { "Amy", "Sam Br", "Sam Bl", "Pat Lee (1)", "Pat Lee (2)" }.OrderBy(n => n).ToArray(), shortened.Select(Name).OrderBy(n => n).ToArray());
            Assert.AreEqual("", shortened.Single(m => m.first_name == "Amy").last_name); // so the surname column is left empty
            Assert.IsTrue(shortened.All(m => m.isAdultLeader == 0));

            var full = MemberReportLayouts.PatrolSheetMembers(members, true);
            CollectionAssert.AreEqual(new[] { "Amy Chen", "Pat Lee (1)", "Pat Lee (2)", "Sam Black", "Sam Brown" }, full.Select(Name).OrderBy(n => n).ToArray());
            Assert.AreEqual("Brown", members[0].last_name); // the members themselves are not changed
        }

        [TestMethod]
        public void PatrolSheetsCopeWithAMemberWhoHasNoSurnameOrNoFirstName()
        {
            var members = new[] { Person("Ben", "", "Blue"), Person("", "Smith", "Red") };

            var shortened = MemberReportLayouts.PatrolSheetMembers(members, false);
            var full = MemberReportLayouts.PatrolSheetMembers(members, true);

            Assert.AreEqual(2, shortened.Count);
            Assert.AreEqual("", shortened.Single(m => m.first_name == "Ben").last_name);
            Assert.AreEqual("Smith", full.Single(m => m.first_name == "").last_name);
        }

        private static List<string> Order(IEnumerable<MemberListModel> members, MemberListSort sort) =>
            MemberReportLayouts.Sorted(members, sort).Select(m => m.first_name).ToList();

        [TestMethod]
        public void TheMemberListCanBeSortedByNamePositionPatrolAgeOrMemberNumber()
        {
            MemberListModel P(string first, string patrol, string duty, string age, string number) =>
                new MemberListModel { id = first, first_name = first, last_name = "Z", patrol_name = patrol, patrol_duty = duty, age = age, member_number = number };
            var members = new[]
            {
                P("Dan", "Red", "", "12y 1m", "300"), P("Amy", "Blue", "APL", "14y 0m", "1000"), P("Cat", "Red", "PL", "11y 5m", "20"),
                P("Eve", "", "UL", "15y 2m", "9"), P("Ben", "Blue", "PL", "13y 11m", "abc"), P("Fay", "Red", "", "9y 0m", "")
            };

            CollectionAssert.AreEqual(new[] { "Amy", "Ben", "Cat", "Dan", "Eve", "Fay" }, Order(members, MemberListSort.Alphabetical));
            // the unit leader, patrol leaders, assistant patrol leaders, then everyone else, each by name
            CollectionAssert.AreEqual(new[] { "Eve", "Ben", "Cat", "Amy", "Dan", "Fay" }, Order(members, MemberListSort.Positions));
            // the unit leader, then by patrol with those in none last, the patrol leader then the assistant first in each
            CollectionAssert.AreEqual(new[] { "Eve", "Ben", "Amy", "Cat", "Dan", "Fay" }, Order(members, MemberListSort.Patrols));
            // the oldest first
            CollectionAssert.AreEqual(new[] { "Eve", "Amy", "Ben", "Dan", "Cat", "Fay" }, Order(members, MemberListSort.Age));
            // the number as a number (9 before 20, 300, 1000), then anything that is not one
            CollectionAssert.AreEqual(new[] { "Eve", "Cat", "Dan", "Amy", "Ben", "Fay" }, Order(members, MemberListSort.MemberNumber));
        }

        [TestMethod]
        public void SortingTreatsPatrolLeadersAndSixersAlikeAndTiesFallBackToTheName()
        {
            MemberListModel P(string first, string duty) => new MemberListModel { id = first, first_name = first, last_name = "Z", patrol_name = "Red", patrol_duty = duty, age = "10y 0m" };
            var members = new[] { P("Zed", ""), P("Yan", "Sixer"), P("Xia", "Seconder"), P("Wes", "") };

            CollectionAssert.AreEqual(new[] { "Yan", "Xia", "Wes", "Zed" }, Order(members, MemberListSort.Positions));
            CollectionAssert.AreEqual(new[] { "Yan", "Xia", "Wes", "Zed" }, Order(members, MemberListSort.Patrols));
            CollectionAssert.AreEqual(new[] { "Wes", "Xia", "Yan", "Zed" }, Order(members, MemberListSort.Age)); // the same age, so by name
        }

        [TestMethod]
        public void AgesAreReadInMonthsAndAnythingElseIsNotAnAge()
        {
            Assert.AreEqual(12 * 12 + 11, MemberReportLayouts.AgeInMonths("12y 11m"));
            Assert.AreEqual(8 * 12, MemberReportLayouts.AgeInMonths("8y 0m"));
            Assert.AreEqual(10 * 12, MemberReportLayouts.AgeInMonths("10y"));
            Assert.AreEqual(-1, MemberReportLayouts.AgeInMonths(""));
            Assert.AreEqual(-1, MemberReportLayouts.AgeInMonths(null));
            Assert.AreEqual(-1, MemberReportLayouts.AgeInMonths("unknown"));
        }

        [TestMethod]
        public void EveryMemberListIsNamedByTheShortOrFullNameOption()
        {
            var members = new[]
            {
                Person("Sam", "Brown", "Red", "PL", council: true), Person("Sam", "Black", "Red"), Person("Amy", "Chen", "Blue"),
                Person("Sam", "Adult", "Adult Leaders", "SL", adult: 1, council: true) // an adult named Sam too, so the three have to be told apart together
            };
            string Name(MemberListModel m) => $"{m.first_name} {m.last_name}".Trim();

            var shortPatrol = MemberReportLayouts.PatrolListMembers(members, true, false);
            CollectionAssert.AreEqual(new[] { "Sam Ad", "Amy", "Sam Bl", "Sam Br" }.OrderBy(n => n).ToArray(), shortPatrol.Select(Name).OrderBy(n => n).ToArray());
            var fullPatrol = MemberReportLayouts.PatrolListMembers(members, true, true);
            CollectionAssert.AreEqual(new[] { "Amy Chen", "Sam Adult", "Sam Black", "Sam Brown" }, fullPatrol.Select(Name).OrderBy(n => n).ToArray());
            Assert.IsTrue(fullPatrol.Single(m => m.last_name == "Brown").unit_council && fullPatrol.Single(m => m.last_name == "Brown").patrol_duty == "PL"); // the rest of each member is kept

            // without the leaders only the youth are told apart from each other, so two letters are not needed
            CollectionAssert.AreEqual(new[] { "Amy", "Sam Bl", "Sam Br" }.OrderBy(n => n).ToArray(),
                MemberReportLayouts.PatrolListMembers(members, false, false).Select(Name).OrderBy(n => n).ToArray());

            var list = MemberReportLayouts.MemberListMembers(members, MemberListSort.Alphabetical, false);
            CollectionAssert.AreEqual(new[] { "Amy", "Sam Bl", "Sam Br" }, list.Select(Name).ToList()); // named, and still in the order asked for
            Assert.AreEqual("Brown", members[0].last_name); // the members themselves are not changed
        }

        [TestMethod]
        public void TheSurnameColumnIsOnlyNeededWhenSomeoneHasOne()
        {
            var unique = MemberReportLayouts.WithNames(new[] { Person("Amy", "Chen", "Blue"), Person("Ben", "Lee", "Red", council: true) }, false);
            var shared = MemberReportLayouts.WithNames(new[] { Person("Sam", "Brown", "Blue"), Person("Sam", "Black", "Red") }, false);

            Assert.IsFalse(MemberReportLayouts.ShowLastName(MemberReportLayouts.MemberList(unique, "scout")));
            Assert.IsFalse(MemberReportLayouts.ShowLastName(MemberReportLayouts.Patrols(unique)));
            Assert.IsFalse(MemberReportLayouts.PatrolList(unique, false).ShowLastName);
            Assert.IsTrue(MemberReportLayouts.ShowLastName(MemberReportLayouts.MemberList(shared, "scout")));
            Assert.IsTrue(MemberReportLayouts.ShowLastName(MemberReportLayouts.Patrols(shared)));
            Assert.IsTrue(MemberReportLayouts.PatrolList(shared, false).ShowLastName);
            Assert.IsFalse(MemberReportLayouts.ShowLastName(new List<MemberListRow>())); // nothing in the list: nothing to show

            // a surname that only an adult leader needs still counts, as the leaders are in the same list
            var withAdult = MemberReportLayouts.PatrolListMembers(new[] { Person("Sam", "Brown", "Blue"), Person("Sam", "Lead", "Adult Leaders", "SL", adult: 1) }, true, false);
            Assert.IsTrue(MemberReportLayouts.PatrolList(withAdult, true).ShowLastName);
        }

        [TestMethod]
        public void TheWorkbooksLeaveTheSurnameColumnOutWhenNoOneHasOneAndKeepItWhenSomeoneDoes()
        {
            SetEnvironment.SetSyncfusionLicense();
            var service = new ReportService();
            var unique = MemberReportLayouts.WithNames(new[] { Person("Amy", "Chen", "Blue", "PL", age: "14y 0m"), Person("Ben", "Lee", "Red", council: true, age: "9y 0m") }, false);
            var shared = MemberReportLayouts.WithNames(new[] { Person("Sam", "Brown", "Blue", age: "14y 0m"), Person("Sam", "Black", "Red", age: "9y 0m") }, false);
            string Headings(Syncfusion.XlsIO.IWorksheet sheet, int row, int from, int to) => string.Join("|", Enumerable.Range(from, to - from + 1).Select(c => sheet.Range[row, c].Text));

            // member list: the headings are on the fourth row from the second column
            var withoutLast = service.GenerateMemberListWorkbook(unique, "Group", "scout", "Unit").Worksheets[0];
            Assert.AreEqual("First Name|Age|Member|Duty|Patrol", Headings(withoutLast, 4, 2, 6));
            Assert.AreEqual("Amy", withoutLast.Range[5, 2].Text);
            Assert.AreEqual("14y 0m", withoutLast.Range[5, 3].Text);
            var withLast = service.GenerateMemberListWorkbook(shared, "Group", "scout", "Unit").Worksheets[0];
            Assert.AreEqual("First Name|Last Name|Age|Member|Duty|Patrol", Headings(withLast, 4, 2, 7));
            Assert.AreEqual("Sam", withLast.Range[5, 2].Text);
            Assert.AreEqual("Br", withLast.Range[5, 3].Text); // the surname letters that tell the two Sams apart, in the order given
            Assert.AreEqual("Bl", withLast.Range[6, 3].Text);

            // patrol list: a patrol's first name and duty side by side, the unit council moved in beside them
            var patrolList = service.GeneratePatrolListWorkbook(unique, "Group", "scout", "Unit", false).Worksheets[0];
            Assert.AreEqual("Blue (1)", patrolList.Range[4, 1].Text);
            Assert.AreEqual("Amy", patrolList.Range[5, 1].Text);
            Assert.AreEqual("PL", patrolList.Range[5, 2].Text);
            Assert.AreEqual("Unit Council", patrolList.Range[4, 5].Text);
            Assert.AreEqual("Ben", patrolList.Range[5, 5].Text);

            // patrol sheets
            var sheets = service.GeneratePatrolSheetsWorkbook(unique, "scout");
            Assert.AreEqual("Amy", sheets.Worksheets[0].Range[2, 2].Text);
            Assert.AreEqual("PL", sheets.Worksheets[0].Range[2, 3].Text);
            var sharedSheets = service.GeneratePatrolSheetsWorkbook(shared, "scout");
            Assert.IsTrue(sharedSheets.Worksheets[0].Range[2, 3].Text.Length > 0); // the surname letters are in the next box, so the duty moves along
        }

        [TestMethod]
        public void UnitsWithNoYouthNoAdultsOrNobodyStillLayOut()
        {
            var nobody = new List<MemberListModel>();
            Assert.AreEqual(0, MemberReportLayouts.Patrols(nobody).Count);
            var empty = MemberReportLayouts.PatrolList(nobody, true);
            Assert.AreEqual(0, empty.Patrols.Count + empty.UnitCouncil.Count + empty.AdultLeaders!.Count);

            var adultsOnly = new[] { Person("Sam", "Lead", "Adult Leaders", "SL", adult: 1) };
            var layout = MemberReportLayouts.PatrolList(MemberReportLayouts.PatrolListMembers(adultsOnly, true, false), true);
            Assert.AreEqual(0, layout.Patrols.Count);
            Assert.AreEqual(1, layout.AdultLeaders!.Count);
            Assert.AreEqual(0, MemberReportLayouts.PatrolList(MemberReportLayouts.PatrolListMembers(adultsOnly, false, false), false).Patrols.Count);
            Assert.AreEqual(0, MemberReportLayouts.MemberListMembers(adultsOnly, MemberListSort.Age, false).Count);
            Assert.AreEqual(0, MemberReportLayouts.MemberListMembers(new List<MemberListModel>(), MemberListSort.Patrols, false).Count);

            var noAdults = new[] { Person("Amy") };
            Assert.AreEqual(0, MemberReportLayouts.PatrolList(noAdults, true).AdultLeaders!.Count);
            Assert.AreEqual(1, MemberReportLayouts.Patrols(noAdults).Count);
        }
    }
}
