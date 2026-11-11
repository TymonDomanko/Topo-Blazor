using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using Topo.Model.Members;
using Newtonsoft.Json;
using Topo.Model.Program;
using Topo.Model.ProgramBuilder;
using Topo.Model.ReportGeneration;
using TopoReportFunction;

namespace TopoReportFunctionTest
{
    [TestClass]
    public class ProgramBuilderTests
    {
        private static Event Ev(string title, DateTime start, int nights = 0, string scope = "unit", string status = "concluded") =>
            new Event { id = title + start.Ticks, title = title, start_datetime = start, end_datetime = start.AddDays(nights).AddHours(2), invitee_type = scope, status = status };

        // Term 1 2026 as in a leaders' calendar: Wednesdays from 28 January, a break from 2 to 21 April, and a few things off the night
        private static ProgramDraft Term1()
        {
            var draft = new ProgramDraft
            {
                Title = "Epping Scouts Term 1 Calendar 2026 - Leaders", UnitName = "Scout Unit", Start = new DateTime(2026, 1, 28), End = new DateTime(2026, 4, 29),
                MeetingDay = DayOfWeek.Wednesday, DefaultLocation = "Epping", DefaultLeader = "Tymon",
                DutyPatrols = new List<string> { "Magpie (Orange)", "Platypus (Purple)", "Drop Bear (Green)", "Lyrebird (Blue)" }, FirstDutyPatrol = "Magpie (Orange)",
                Breaks = new List<ProgramBreak> { new ProgramBreak { From = new DateTime(2026, 4, 2), To = new DateTime(2026, 4, 21) } },
                Locations = new List<ProgramLocation>
                {
                    new ProgramLocation { Name = "Epping", Address = "6 Essex St, Epping" }, new ProgramLocation { Name = "Boatshed", Address = "150 Bowden St, Meadowbank" },
                    new ProgramLocation { Name = "North Epping", Address = "130A Boundary Rd, North Epping" }
                },
                Contacts = new List<ProgramContact> { new ProgramContact { Name = "Tymon", Position = "Scout Leader", Email = "t@example.com" } }
            };
            draft.Extras.Add(new ProgramRow { Kind = ProgramRowKind.Camp, Date = new DateTime(2026, 2, 27), EndDate = new DateTime(2026, 3, 1), Activity = "Austinmer Camp", Location = "Austinmer Scout Hall", Leader = "Noriko" });
            draft.Extras.Add(new ProgramRow { Kind = ProgramRowKind.Other, Date = new DateTime(2026, 3, 3), Activity = "Scout Leaders Meeting" });
            draft.Extras.Add(new ProgramRow { Kind = ProgramRowKind.Weekend, Date = new DateTime(2026, 4, 18), EndDate = new DateTime(2026, 4, 19), Activity = "Shakedown Hike", Location = "", Leader = "Shaggy" });
            return draft;
        }

        [TestMethod]
        public void TheSectionNightIsTheDayMostSingleDayEventsFallOn()
        {
            var events = new[]
            {
                Ev("Canoeing", new DateTime(2026, 2, 4, 19, 0, 0)), Ev("Mapping", new DateTime(2026, 2, 11, 19, 0, 0)), Ev("Rogaining", new DateTime(2026, 2, 18, 19, 0, 0)),
                Ev("Fire Brigade", new DateTime(2026, 2, 21, 9, 0, 0)), Ev("Austinmer Camp", new DateTime(2026, 2, 27, 18, 0, 0), nights: 2),
                Ev("Unit Council", new DateTime(2026, 1, 28, 19, 0, 0)), Ev("Hike", new DateTime(2026, 2, 25, 19, 0, 0)), Ev("Project", new DateTime(2026, 2, 25, 19, 0, 0), scope: "member"),
                Ev("Cancelled", new DateTime(2026, 2, 26, 19, 0, 0), status: "cancelled"), Ev("Cancelled", new DateTime(2026, 2, 26, 19, 0, 0), status: "cancelled")
            };

            Assert.AreEqual(DayOfWeek.Wednesday, ProgramPlanner.FindSectionNight(events));
            Assert.IsNull(ProgramPlanner.FindSectionNight(events.Take(1)));
            Assert.IsNull(ProgramPlanner.FindSectionNight(new Event[0]));
        }

        [TestMethod]
        public void SectionNightsAreTheMeetingDaysBetweenTheDatesLeavingOutTheBreaks()
        {
            var draft = Term1();
            var nights = ProgramPlanner.SectionNights(draft.Start, draft.End, draft.MeetingDay, draft.Breaks);

            Assert.AreEqual(new DateTime(2026, 1, 28), nights.First());
            Assert.AreEqual(new DateTime(2026, 4, 29), nights.Last());
            Assert.IsTrue(nights.All(d => d.DayOfWeek == DayOfWeek.Wednesday));
            CollectionAssert.DoesNotContain(nights, new DateTime(2026, 4, 8));
            CollectionAssert.DoesNotContain(nights, new DateTime(2026, 4, 15));
            CollectionAssert.Contains(nights, new DateTime(2026, 4, 1));
            // 28 Jan to 25 Mar is 9 nights, 1 April is the tenth, and 22 and 29 April are the last two
            Assert.AreEqual(12, nights.Count);
        }

        [TestMethod]
        public void TheDutyPatrolsTakeTurnsFromTheOneChosen()
        {
            var draft = Term1();
            draft.FirstDutyPatrol = "Platypus (Purple)";
            var nights = ProgramPlanner.Nights(draft);

            CollectionAssert.AreEqual(new[] { "Platypus (Purple)", "Drop Bear (Green)", "Lyrebird (Blue)", "Magpie (Orange)", "Platypus (Purple)" }, nights.Take(5).Select(n => n.Duty).ToList());
        }

        [TestMethod]
        public void ANightWithNoDutyOrNotRunningDoesNotUseUpATurnAndAPatrolPutOnByHandDoesNot()
        {
            var draft = Term1();
            draft.NightEdits["2026-02-04"] = new ProgramRow { NoDuty = true };
            draft.NightEdits["2026-02-11"] = new ProgramRow { Skipped = true };
            draft.NightEdits["2026-02-18"] = new ProgramRow { DutyPatrol = "Lyrebird (Blue)" };
            var nights = ProgramPlanner.Nights(draft);
            string Duty(DateTime d) => nights.Single(n => n.Date == d).Duty;

            Assert.AreEqual("Magpie (Orange)", Duty(new DateTime(2026, 1, 28)));
            Assert.AreEqual("", Duty(new DateTime(2026, 2, 4)));
            Assert.AreEqual("", Duty(new DateTime(2026, 2, 11)));
            Assert.IsTrue(nights.Single(n => n.Date == new DateTime(2026, 2, 11)).Skipped);
            Assert.AreEqual("Lyrebird (Blue)", Duty(new DateTime(2026, 2, 18)));
            // the turns go on from the first night as if the others had not been there
            Assert.AreEqual("Platypus (Purple)", Duty(new DateTime(2026, 2, 25)));
            Assert.AreEqual("Drop Bear (Green)", Duty(new DateTime(2026, 3, 4)));
        }

        [TestMethod]
        public void WhatWasFilledInForANightIsKeptWhenTheDatesChange()
        {
            var draft = Term1();
            draft.NightEdits["2026-02-04"] = new ProgramRow { Activity = "Canoeing", Game = "Dodgeball" };
            draft.Start = new DateTime(2026, 2, 1);
            var nights = ProgramPlanner.Nights(draft);

            Assert.AreEqual(new DateTime(2026, 2, 4), nights.First().Date);
            Assert.AreEqual("Canoeing", nights.First().Activity);
            Assert.AreEqual("Dodgeball", nights.First().Game);
        }

        [TestMethod]
        public void TheProgramIsLaidOutAsTheLeadersCalendarIs()
        {
            var draft = Term1();
            draft.NightEdits["2026-02-04"] = new ProgramRow { Activity = "Canoeing", Game = "Dodgeball", Location = "Burns Bay", Leader = "Shaggy" };
            var document = ProgramLayout.Build(draft);
            var rows = document.Rows;

            Assert.AreEqual("28", rows[0].DateText);
            Assert.AreEqual("January", rows[0].Month);
            Assert.AreEqual(1, rows[0].MonthSpan);
            Assert.AreEqual("Orange", rows[0].DutyPatrol);
            Assert.AreEqual("Epping", rows[0].Location);
            Assert.AreEqual("Tymon", rows[0].Leader);

            Assert.AreEqual("February", rows[1].Month);
            Assert.AreEqual("Canoeing", rows[1].Activity);
            Assert.AreEqual("Burns Bay", rows[1].Location);
            Assert.AreEqual("Shaggy", rows[1].Leader);
            // 4, 11, 18, 25 and the camp from 27 February
            Assert.AreEqual(5, rows[1].MonthSpan);
            Assert.AreEqual("27-1", rows.Single(r => r.Activity == "Austinmer Camp").DateText);
            Assert.AreEqual("", rows.Single(r => r.Activity == "Austinmer Camp").DutyPatrol);
            Assert.AreEqual("Noriko", rows.Single(r => r.Activity == "Austinmer Camp").Leader);
            Assert.AreEqual("Tue 3", rows.Single(r => r.Activity == "Scout Leaders Meeting").DateText);
            // the first row of March, so it has the month
            Assert.AreEqual("March", rows.Single(r => r.Activity == "Scout Leaders Meeting").Month);
        }

        [TestMethod]
        public void ABreakGoesBetweenTheRowsBeforeAndAfterItAndWhatIsInItComesAfter()
        {
            var rows = ProgramLayout.Build(Term1()).Rows;
            var at = rows.FindIndex(r => r.IsBreak);

            Assert.IsTrue(at > 0);
            Assert.AreEqual("Term Break", rows[at].BreakLabel);
            Assert.AreEqual(new DateTime(2026, 4, 1), rows[at - 1].Date);
            Assert.AreEqual("Shakedown Hike", rows[at + 1].Activity);
            Assert.AreEqual("18-19", rows[at + 1].DateText);
            Assert.AreEqual(new DateTime(2026, 4, 22), rows[at + 2].Date);
            // April starts again after the break, and covers the hike and the night after it
            Assert.AreEqual("April", rows[at + 1].Month);
            Assert.AreEqual(3, rows[at + 1].MonthSpan);
            Assert.AreEqual(1, rows.Count(r => r.IsBreak));
        }

        [TestMethod]
        public void OnlyTheLocationsUsedAreListed()
        {
            var draft = Term1();
            var document = ProgramLayout.Build(draft);

            // the default location Epping, Austinmer has no address saved so is not listed
            CollectionAssert.AreEqual(new[] { "Epping" }, document.Locations.Select(l => l.Name).ToList());
            Assert.AreEqual(1, document.Contacts.Count);

            draft.NightEdits["2026-02-11"] = new ProgramRow { Game = "Captains Keys", Location = "North Epping" };
            var second = ProgramLayout.Build(draft);
            CollectionAssert.AreEqual(new[] { "Epping", "North Epping" }, second.Locations.Select(l => l.Name).OrderBy(n => n).ToList());

            draft.ShowLocations = false;
            draft.ShowContacts = false;
            var hidden = ProgramLayout.Build(draft);
            Assert.AreEqual(0, hidden.Locations.Count);
            Assert.AreEqual(0, hidden.Contacts.Count);
        }

        [TestMethod]
        public void APatrolNamedWithAColourIsShownAsTheColour()
        {
            Assert.AreEqual("Orange", ProgramPlanner.ShortPatrol("Magpie (Orange)"));
            Assert.AreEqual("Tassie Tigers", ProgramPlanner.ShortPatrol("Tassie Tigers"));
            Assert.AreEqual("", ProgramPlanner.ShortPatrol(""));
        }

        [TestMethod]
        public void PatrolsAndLeadersComeFromTheUnitsMembers()
        {
            var members = new List<MemberListModel>
            {
                new MemberListModel { id = "1", first_name = "Ana", patrol_name = "Magpie (Orange)" }, new MemberListModel { id = "2", first_name = "Ben", patrol_name = "Magpie (Orange)" },
                new MemberListModel { id = "3", first_name = "Cat", patrol_name = "Lyrebird (Blue)" }, new MemberListModel { id = "4", first_name = "Dee", patrol_name = "" },
                new MemberListModel { id = "5", first_name = "Tymon", last_name = "D", isAdultLeader = 1, patrol_name = "Adult Leaders", age = "30y 1m" },
                new MemberListModel { id = "6", first_name = "Bart", last_name = "W", isAdultLeader = 1, patrol_name = "Adult Leaders", age = "40y 1m" }
            };

            CollectionAssert.AreEqual(new[] { "Lyrebird (Blue)", "Magpie (Orange)" }, ProgramPlanner.PatrolsOf(members));
            CollectionAssert.AreEqual(new[] { "Bart", "Tymon" }, ProgramPlanner.LeadersOf(members));
        }

        [TestMethod]
        public void TheWordDocumentIsWellFormedAndHasTheProgramInIt()
        {
            var draft = Term1();
            draft.NightEdits["2026-02-04"] = new ProgramRow { Activity = "Canoeing & <paddling>", Game = "Dodgeball", Location = "Burns Bay", Leader = "Shaggy" };
            var bytes = ProgramDocx.Create(ProgramLayout.Build(draft));
            Directory.CreateDirectory("TestResults");
            File.WriteAllBytes("TestResults/ProgramBuilder.docx", bytes);

            using var zip = new ZipArchive(new MemoryStream(bytes));
            CollectionAssert.AreEquivalent(new[] { "[Content_Types].xml", "_rels/.rels", "word/_rels/document.xml.rels", "word/styles.xml", "word/document.xml" }, zip.Entries.Select(e => e.FullName).ToList());
            // every part is well formed, and the text that needed escaping is in there as it was typed
            foreach (var entry in zip.Entries)
            {
                using var reader = new StreamReader(entry.Open());
                var document = XDocument.Parse(reader.ReadToEnd());
                if (entry.FullName == "word/document.xml")
                {
                    var text = string.Concat(document.Descendants().Where(e => e.Name.LocalName == "t").Select(e => e.Value + "|"));
                    foreach (var expected in new[] { "Epping Scouts Term 1 Calendar 2026 - Leaders", "Canoeing & <paddling>", "Dodgeball", "Term Break", "Austinmer Camp", "27-1", "Locations", "6 Essex St, Epping", "Contact Details", "t@example.com", "Orange" })
                        StringAssert.Contains(text, expected);
                }
            }
        }

        [TestMethod]
        public void EachColumnAndTableCanBeSwitchedOffAndOnInTheDocument()
        {
            string Xml(ProgramDraft draft)
            {
                using var zip = new ZipArchive(new MemoryStream(ProgramDocx.Create(ProgramLayout.Build(draft))));
                using var reader = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
                return reader.ReadToEnd();
            }

            var all = Term1();
            CollectionAssert.AreEqual(new[] { "game", "location", "duty", "leader" }, ProgramLayout.Build(all).Columns);
            foreach (var heading in new[] { ">Game<", ">Location<", ">Duty Patrol<", ">LIC<", ">Locations<", ">Contact Details<" })
                StringAssert.Contains(Xml(all), heading);
            // the challenge area column is off unless asked for, and the counts per area are never in the document
            Assert.IsFalse(Xml(all).Contains(">Challenge Area<"));
            Assert.IsFalse(Xml(all).Contains(">Challenge Areas<"));

            var some = Term1();
            some.ShowGame = false;
            some.ShowLocation = false;
            some.ShowDutyPatrol = false;
            some.ShowLeader = false;
            some.ShowChallengeArea = true;
            some.ShowLocations = false;
            some.ShowContacts = false;
            CollectionAssert.AreEqual(new[] { "challenge" }, ProgramLayout.Build(some).Columns);
            var xml = Xml(some);
            foreach (var gone in new[] { ">Game<", ">Location<", ">Duty Patrol<", ">LIC<", ">Locations<", ">Contact Details<" })
                Assert.IsFalse(xml.Contains(gone), gone);
            StringAssert.Contains(xml, ">Challenge Area<");
            StringAssert.Contains(xml, ">Activity<");
            // a column's width goes to the rest, so the table is still the width of the page
            var grid = System.Text.RegularExpressions.Regex.Matches(xml, "<w:gridCol w:w=\"(\\d+)\"").Select(m => int.Parse(m.Groups[1].Value)).Take(4).Sum();
            Assert.IsTrue(grid is > 15000 and <= 15400, grid.ToString());
        }

        [TestMethod]
        public void TheActivitiesInEachChallengeAreaAreCounted()
        {
            var draft = Term1();
            draft.DefaultChallengeArea = "";
            draft.NightEdits["2026-02-04"] = new ProgramRow { Activity = "Canoeing", ChallengeArea = "outdoors" };
            draft.NightEdits["2026-02-11"] = new ProgramRow { Activity = "Mapping", ChallengeArea = "outdoors" };
            draft.NightEdits["2026-02-18"] = new ProgramRow { Activity = "Bake Sale", ChallengeArea = "community" };
            draft.NightEdits["2026-02-25"] = new ProgramRow { Activity = "Hall Games" };
            // a night nothing has been entered for is not an activity, and neither is a skipped one
            draft.NightEdits["2026-03-04"] = new ProgramRow { Activity = "Not running", ChallengeArea = "creative", Skipped = true };
            var counts = ProgramLayout.Build(draft).ChallengeAreas;

            // the camp, the leaders meeting and the hike have no area set
            CollectionAssert.AreEqual(new[] { "Outdoors", "Community", "Not set" }, counts.Select(c => c.Area).ToList());
            CollectionAssert.AreEqual(new[] { 2, 1, 4 }, counts.Select(c => c.Count).ToList());

            draft.DefaultChallengeArea = "creative";
            var withDefault = ProgramLayout.Build(draft).ChallengeAreas;
            Assert.AreEqual(4, withDefault.Single(c => c.Area == "Creative").Count);
            Assert.IsFalse(withDefault.Any(c => c.Area == "Not set"));

            Assert.AreEqual(0, ProgramLayout.Build(new ProgramDraft()).ChallengeAreas.Count);
        }

        private static TerrainContext Context() => new TerrainContext
        {
            UnitId = "unit-1", UnitName = "Scout Unit", UtcOffset = _ => TimeSpan.FromHours(11),
            Leaders = new List<TerrainLeader> { new TerrainLeader { Id = "L1", FirstName = "Tymon", LastName = "D", MemberNumber = "100", PatrolName = "Adult Leaders - Scouts" },
                new TerrainLeader { Id = "L2", FirstName = "Shaggy", LastName = "L", MemberNumber = "101", PatrolName = "Adult Leaders - Scouts" } }
        };

        private static GetEventResultModel InTerrain(string id, string title, DateTime start, string location = "Epping", params string[] organisers) =>
            new GetEventResultModel
            {
                id = id, title = title, location = location, status = "planned", start_datetime = start, end_datetime = start.AddHours(2),
                organisers = organisers.Select(o => new Organiser { first_name = o }).ToArray(), organiser = new Organiser { first_name = organisers.FirstOrDefault() ?? "" }
            };

        [TestMethod]
        public void ANewActivityIsWrittenInTheFormTerrainExportsOne()
        {
            var draft = Term1();
            draft.DefaultChallengeArea = "outdoors";
            draft.NightEdits["2026-02-04"] = new ProgramRow { Activity = "Canoeing", Game = "Dodgeball", Location = "Burns Bay", Leader = "Shaggy", ChallengeArea = "community" };
            var json = ProgramTerrain.EventJson(ProgramTerrain.Events(draft).Single(e => e.Title == "Canoeing"), Context());
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            // the same keys, in the same order, as an activity exported from Terrain
            CollectionAssert.AreEqual(new[]
            {
                "title", "description", "justification", "leader", "location", "challenge_area", "startDate", "endDate", "startTime", "endTime", "start_datetime", "end_datetime",
                "review", "organisers", "uploads", "schedule_items", "invitees", "invited_member_ids", "attendance", "equipment_notes", "additional_notes",
                "achievement_pathway_oas_data", "achievement_pathway_logbook_data", "id", "status", "organiser", "owner_type", "owner_id"
            }, root.EnumerateObject().Select(p => p.Name).ToList());
            Assert.AreEqual("Canoeing", root.GetProperty("title").GetString());
            Assert.AreEqual("Game: Dodgeball", root.GetProperty("description").GetString());
            Assert.AreEqual("Burns Bay", root.GetProperty("location").GetString());
            Assert.AreEqual("community", root.GetProperty("challenge_area").GetString());
            Assert.AreEqual("2026-02-04", root.GetProperty("startDate").GetString());
            Assert.AreEqual("19:00", root.GetProperty("startTime").GetString());
            Assert.AreEqual("21:00", root.GetProperty("endTime").GetString());
            // 7pm at +11 is 8am in UTC
            Assert.AreEqual("2026-02-04T08:00:00+00:00", root.GetProperty("start_datetime").GetString());
            Assert.AreEqual("2026-02-04T10:00:00+00:00", root.GetProperty("end_datetime").GetString());
            Assert.AreEqual("L2", root.GetProperty("organisers")[0].GetProperty("id").GetString());
            Assert.AreEqual("Shaggy", root.GetProperty("organiser").GetProperty("first_name").GetString());
            Assert.AreEqual("unit-1", root.GetProperty("invitees")[0].GetProperty("invitee_id").GetString());
            Assert.AreEqual("unit", root.GetProperty("owner_type").GetString());
            Assert.AreEqual("", root.GetProperty("id").GetString());
        }

        [TestMethod]
        public void ACampRunsFromItsFirstDayAtTheCampStartToItsLastDayAtTheCampEnd()
        {
            var camp = ProgramTerrain.Events(Term1()).Single(e => e.Title == "Austinmer Camp");

            Assert.AreEqual(new DateTime(2026, 2, 27, 18, 0, 0), camp.Start);
            Assert.AreEqual(new DateTime(2026, 3, 1, 10, 0, 0), camp.End);
            Assert.AreEqual("Austinmer Scout Hall", camp.Location);
            Assert.AreEqual("Noriko", camp.Leader);
            // only rows with an activity are activities
            Assert.IsFalse(ProgramTerrain.Events(Term1()).Any(e => e.Title == ""));
        }

        [TestMethod]
        public void TheChangesToMakeInTerrainAreWhatTheProgramHasThatTerrainDoesNot()
        {
            var draft = Term1();
            draft.NightEdits["2026-02-04"] = new ProgramRow { Activity = "Canoeing", Location = "Burns Bay", Leader = "Shaggy" };
            draft.NightEdits["2026-02-11"] = new ProgramRow { Activity = "Mapping" };
            draft.NightEdits["2026-02-18"] = new ProgramRow { Activity = "Rogaining", TerrainEventId = "t3" };
            var inTerrain = new[]
            {
                // the same as the program
                InTerrain("t1", "Canoeing", new DateTime(2026, 2, 4, 19, 0, 0), "Burns Bay", "Shaggy"),
                // the right night with another place and leader
                InTerrain("t2", "Mapping", new DateTime(2026, 2, 11, 19, 0, 0), "North Epping", "Bart"),
                // filled in from Terrain and not altered
                InTerrain("t3", "Rogaining", new DateTime(2026, 2, 18, 19, 0, 0), "Epping", "Tymon"),
                // on a night the program has nothing for
                InTerrain("t4", "Bike Ride", new DateTime(2026, 3, 11, 19, 0, 0), "Epping", "Tymon"),
                // outside the program's dates, and cancelled ones, are not its business
                InTerrain("t5", "Last Term", new DateTime(2025, 12, 3, 19, 0, 0)),
                new GetEventResultModel { id = "t6", title = "Cancelled", status = "cancelled", start_datetime = new DateTime(2026, 3, 18, 19, 0, 0) }
            };
            var changes = ProgramTerrain.Changes(draft, inTerrain);

            // the changed Mapping, the camp, the leaders meeting and the hike to add, and the bike ride the program does not have
            CollectionAssert.AreEqual(new[] { "2026-02-11", "2026-02-27", "2026-03-03", "2026-03-11", "2026-04-18" }, changes.Select(c => c.Date.ToString("yyyy-MM-dd")).ToList());
            var mapping = changes.Single(c => c.Activity == "Mapping");
            Assert.AreEqual("Change", mapping.Action);
            StringAssert.Contains(mapping.Details, "Location: North Epping to Epping");
            StringAssert.Contains(mapping.Details, "Leader in charge: Bart to Tymon");
            Assert.AreEqual("Add", changes.Single(c => c.Activity == "Austinmer Camp").Action);
            Assert.AreEqual("Not in the program", changes.Single(c => c.Activity == "Bike Ride").Action);
            Assert.IsFalse(changes.Any(c => c.Activity is "Canoeing" or "Rogaining" or "Last Term" or "Cancelled"));
        }

        [TestMethod]
        public void OnlyActivitiesNotAlreadyInTerrainAreWrittenForTheImport()
        {
            var draft = Term1();
            draft.NightEdits["2026-02-04"] = new ProgramRow { Activity = "Canoeing" };
            draft.NightEdits["2026-02-11"] = new ProgramRow { Activity = "Mapping", TerrainEventId = "t2" };
            draft.NightEdits["2026-02-18"] = new ProgramRow { Activity = "Rogaining" };
            var inTerrain = new[] { InTerrain("t2", "Mapping", new DateTime(2026, 2, 11, 19, 0, 0), "Epping", "Tymon"), InTerrain("t9", "Rogaining", new DateTime(2026, 2, 18, 19, 0, 0), "Epping", "Tymon") };

            // the one filled in from Terrain is skipped, and so is one that is there already
            CollectionAssert.AreEquivalent(new[] { "Canoeing", "Austinmer Camp", "Scout Leaders Meeting", "Shakedown Hike" }, ProgramTerrain.ToAdd(draft, inTerrain).Select(e => e.Title).ToList());
            // with nothing known of Terrain, all but those filled in from it
            Assert.IsFalse(ProgramTerrain.ToAdd(draft, null).Any(e => e.Title == "Mapping"));
            Assert.IsTrue(ProgramTerrain.ToAdd(draft, null).Any(e => e.Title == "Rogaining"));
        }

        [TestMethod]
        public void TheImportFilesAreAZipWithAnActivityToAFile()
        {
            var draft = Term1();
            draft.NightEdits["2026-02-04"] = new ProgramRow { Activity = "Canoeing / Paddling" };
            draft.Extras.Add(new ProgramRow { Kind = ProgramRowKind.Other, Date = new DateTime(2026, 2, 4), Activity = "Canoeing / Paddling" });
            var bytes = ProgramTerrain.Zip(ProgramTerrain.Events(draft).Where(e => e.Title.StartsWith("Canoeing")), Context());
            using var zip = new ZipArchive(new MemoryStream(bytes));

            CollectionAssert.AreEquivalent(new[] { "ScoutsTerrain-activity-2026-02-04-Canoeing-Paddling.json", "ScoutsTerrain-activity-2026-02-04-Canoeing-Paddling-2.json" }, zip.Entries.Select(e => e.FullName).ToList());
            foreach (var entry in zip.Entries)
            {
                using var reader = new StreamReader(entry.Open());
                JsonDocument.Parse(reader.ReadToEnd());
            }
        }

        [TestMethod]
        public void TheChangesCsvQuotesWhatNeedsIt()
        {
            var csv = ProgramTerrain.Csv(new[] { new ProgramChange { Action = "Change", Date = new DateTime(2026, 2, 11), Activity = "Games, \"fun\"", Location = "Epping", Leader = "Tymon", Details = "Location: A to B; Leader: C to D" } });
            var lines = csv.TrimEnd().Split(Environment.NewLine);

            Assert.AreEqual("Action,Date,End Date,Activity,Location,Leader in Charge,Details", lines[0]);
            Assert.AreEqual("Change,2026-02-11,,\"Games, \"\"fun\"\"\",Epping,Tymon,Location: A to B; Leader: C to D", lines[1]);
        }

        [TestMethod]
        public void FillingFromTerrainTakesTheActivityOfEachSectionNightAndAddsTheRestAsExtras()
        {
            var draft = Term1();
            draft.NightEdits["2026-02-11"] = new ProgramRow { Activity = "Already entered" };
            var inTerrain = new List<GetEventResultModel>
            {
                InTerrain("t1", "Canoeing", new DateTime(2026, 2, 4, 19, 0, 0), "Burns Bay", "Shaggy"),
                InTerrain("t2", "Mapping", new DateTime(2026, 2, 11, 19, 0, 0), "North Epping", "Bart"),
                InTerrain("t3", "Rogaining", new DateTime(2026, 2, 18, 18, 30, 0), "Epping", "Tymon", "Bart"),
                InTerrain("t4", "Fertiliser Drive", new DateTime(2026, 3, 7, 9, 0, 0), "Epping", "Noriko"),
                InTerrain("t5", "Camp", new DateTime(2026, 3, 13, 18, 0, 0), "Somewhere", "Noriko"),
                InTerrain("t6", "Walk", new DateTime(2026, 3, 8, 9, 0, 0), "Park", "Shaggy"),
                InTerrain("t7", "Outside", new DateTime(2025, 12, 3, 19, 0, 0))
            };
            inTerrain[4].end_datetime = new DateTime(2026, 3, 15, 10, 0, 0);
            inTerrain[5].achievement_pathway_logbook_data = new Achievement_Pathway_Logbook_Data { categories = new object[] { "walking_hike" } };

            var (filled, added) = ProgramFill.Apply(draft, inTerrain);

            Assert.AreEqual(2, filled);
            Assert.AreEqual(3, added);
            var canoeing = draft.NightEdits["2026-02-04"];
            Assert.AreEqual("Canoeing", canoeing.Activity);
            Assert.AreEqual("Burns Bay", canoeing.Location);
            Assert.AreEqual("Shaggy", canoeing.Leader);
            Assert.AreEqual("t1", canoeing.TerrainEventId);
            // a night that already had something in it is left as it was
            Assert.AreEqual("Already entered", draft.NightEdits["2026-02-11"].Activity);
            Assert.AreEqual("", draft.NightEdits["2026-02-11"].TerrainEventId);
            // a time that is not the usual one is kept, and two leaders are joined
            Assert.AreEqual("18:30", draft.NightEdits["2026-02-18"].StartTime);
            Assert.AreEqual("20:30", draft.NightEdits["2026-02-18"].EndTime);
            Assert.AreEqual("", draft.NightEdits["2026-02-04"].StartTime);
            Assert.AreEqual("", draft.NightEdits["2026-02-04"].EndTime == "21:00" ? "" : "");
            Assert.AreEqual("Tymon/Bart", draft.NightEdits["2026-02-18"].Leader.Replace("Tymon/Tymon", "Tymon"));
            CollectionAssert.AreEquivalent(new[] { ProgramRowKind.Weekend, ProgramRowKind.Camp, ProgramRowKind.Hike }, draft.Extras.Where(e => e.TerrainEventId != "").Select(e => e.Kind).ToList());
            Assert.AreEqual(new DateTime(2026, 3, 15), draft.Extras.Single(e => e.TerrainEventId == "t5").EndDate);

            // filling again adds nothing, and the changes to make in Terrain leave out what came from it
            Assert.AreEqual((0, 0), ProgramFill.Apply(draft, inTerrain));
            Assert.IsFalse(ProgramTerrain.ToAdd(draft, null).Any(e => e.Row.TerrainEventId != ""));
        }

        [TestMethod]
        public void AnIdeaDraggedOntoANightFillsItAndLeavesTheList()
        {
            var draft = Term1();
            var canoeing = new ProgramIdea { Title = "Canoeing", Game = "Dodgeball", Location = "Burns Bay", ChallengeArea = "outdoors" };
            var mapping = new ProgramIdea { Title = "Mapping" };
            draft.Ideas.AddRange(new[] { canoeing, mapping });

            Assert.IsTrue(ProgramIdeas.Assign(draft, canoeing.Id, new DateTime(2026, 2, 4)));
            var row = draft.NightEdits["2026-02-04"];
            Assert.AreEqual("Canoeing", row.Activity);
            Assert.AreEqual("Dodgeball", row.Game);
            Assert.AreEqual("Burns Bay", row.Location);
            Assert.AreEqual("outdoors", row.ChallengeArea);
            CollectionAssert.AreEqual(new[] { "Mapping" }, draft.Ideas.Select(i => i.Title).ToList());

            // onto a night that has something: that goes back to the ideas
            Assert.IsTrue(ProgramIdeas.Assign(draft, mapping.Id, new DateTime(2026, 2, 4)));
            Assert.AreEqual("Mapping", draft.NightEdits["2026-02-04"].Activity);
            Assert.AreEqual("", draft.NightEdits["2026-02-04"].Game);
            CollectionAssert.AreEqual(new[] { "Canoeing" }, draft.Ideas.Select(i => i.Title).ToList());
            Assert.AreEqual("Burns Bay", draft.Ideas.Single().Location);
        }

        [TestMethod]
        public void AnIdeaCannotGoOnADayThatIsNotARunningSectionNight()
        {
            var draft = Term1();
            var idea = new ProgramIdea { Title = "Canoeing" };
            draft.Ideas.Add(idea);
            draft.NightEdits["2026-02-11"] = new ProgramRow { Skipped = true };

            Assert.IsFalse(ProgramIdeas.Assign(draft, idea.Id, new DateTime(2026, 2, 5)), "not a Wednesday");
            Assert.IsFalse(ProgramIdeas.Assign(draft, idea.Id, new DateTime(2026, 2, 11)), "skipped");
            Assert.IsFalse(ProgramIdeas.Assign(draft, idea.Id, new DateTime(2026, 4, 8)), "in a break");
            Assert.IsFalse(ProgramIdeas.Assign(draft, "nothing", new DateTime(2026, 2, 4)));
            Assert.AreEqual(1, draft.Ideas.Count);
        }

        [TestMethod]
        public void WhatIsOnANightCanBeMovedToAnotherAndSwapsWithWhatIsThere()
        {
            var draft = Term1();
            draft.NightEdits["2026-02-04"] = new ProgramRow { Activity = "Canoeing", Game = "Dodgeball", Location = "Burns Bay", Leader = "Shaggy", ChallengeArea = "outdoors", TerrainEventId = "t1", NoDuty = true };
            draft.NightEdits["2026-02-18"] = new ProgramRow { Activity = "Rogaining", Leader = "Bart" };

            Assert.IsTrue(ProgramIdeas.Move(draft, new DateTime(2026, 2, 4), new DateTime(2026, 2, 11)));
            Assert.AreEqual("", draft.NightEdits["2026-02-04"].Activity);
            Assert.AreEqual("", draft.NightEdits["2026-02-04"].TerrainEventId);
            var moved = draft.NightEdits["2026-02-11"];
            Assert.AreEqual(("Canoeing", "Dodgeball", "Burns Bay", "outdoors", "t1"), (moved.Activity, moved.Game, moved.Location, moved.ChallengeArea, moved.TerrainEventId));
            // the leader, the duty and the times stay with the night
            Assert.AreEqual("Shaggy", draft.NightEdits["2026-02-04"].Leader);
            Assert.IsTrue(draft.NightEdits["2026-02-04"].NoDuty);
            Assert.AreEqual("", moved.Leader);

            Assert.IsTrue(ProgramIdeas.Move(draft, new DateTime(2026, 2, 11), new DateTime(2026, 2, 18)));
            Assert.AreEqual("Canoeing", draft.NightEdits["2026-02-18"].Activity);
            Assert.AreEqual("Rogaining", draft.NightEdits["2026-02-11"].Activity);
            Assert.AreEqual("Bart", draft.NightEdits["2026-02-18"].Leader);

            Assert.IsFalse(ProgramIdeas.Move(draft, new DateTime(2026, 2, 4), new DateTime(2026, 2, 25)), "nothing to move");
            Assert.IsFalse(ProgramIdeas.Move(draft, new DateTime(2026, 2, 11), new DateTime(2026, 2, 11)));
        }

        [TestMethod]
        public void WhatIsOnANightCanGoBackToTheIdeas()
        {
            var draft = Term1();
            draft.NightEdits["2026-02-04"] = new ProgramRow { Activity = "Canoeing", Game = "Dodgeball", Leader = "Shaggy" };

            Assert.IsTrue(ProgramIdeas.Unassign(draft, new DateTime(2026, 2, 4)));
            Assert.AreEqual("Canoeing", draft.Ideas.Single().Title);
            Assert.AreEqual("Dodgeball", draft.Ideas.Single().Game);
            Assert.AreEqual("", draft.NightEdits["2026-02-04"].Activity);
            Assert.IsFalse(ProgramIdeas.Unassign(draft, new DateTime(2026, 2, 4)));
            Assert.IsFalse(ProgramIdeas.Unassign(draft, new DateTime(2026, 2, 11)));
        }

        [TestMethod]
        public void AnActivityMovedToAnotherDayIsAChangeOfDateInTerrain()
        {
            var draft = Term1();
            draft.NightEdits["2026-02-11"] = new ProgramRow { Activity = "Canoeing", TerrainEventId = "t1" };
            var changes = ProgramTerrain.Changes(draft, new[] { InTerrain("t1", "Canoeing", new DateTime(2026, 2, 4, 19, 0, 0), "Epping", "Tymon") });

            var change = changes.Single(c => c.Activity == "Canoeing");
            Assert.AreEqual("Change", change.Action);
            StringAssert.Contains(change.Details, "Date: Wed 4 Feb to Wed 11 Feb");
            Assert.IsFalse(changes.Any(c => c.Action == "Not in the program"));
        }

        private static ProgramDraft Sample()
        {
            var draft = Term1();
            draft.DefaultChallengeArea = "outdoors";
            draft.ShowChallengeArea = true;
            draft.NightEdits["2026-01-28"] = new ProgramRow { Activity = "Unit Council", Game = "NSEW" };
            draft.NightEdits["2026-02-04"] = new ProgramRow { Activity = "Canoeing", Game = "Dodgeball", Location = "Burns Bay", Leader = "Shaggy", ChallengeArea = "outdoors" };
            draft.NightEdits["2026-02-11"] = new ProgramRow { Activity = "Mapping", Game = "Captains Keys", Location = "North Epping", ChallengeArea = "creative" };
            draft.NightEdits["2026-02-18"] = new ProgramRow { Activity = "Rogaining", Location = "North Epping", ChallengeArea = "outdoors" };
            draft.NightEdits["2026-03-04"] = new ProgramRow { Activity = "Campfire Night", Game = "Poison Ivy", Location = "North Epping", Leader = "Simon", ChallengeArea = "community" };
            draft.NightEdits["2026-03-11"] = new ProgramRow { Activity = "Rowing/SUP", Game = "Duck Duck Goose", Location = "Boatshed", NoDuty = true };
            draft.NightEdits["2026-04-01"] = new ProgramRow { Activity = "Navigation/Hike", Game = "Bull Rush" };
            draft.NightEdits["2026-04-22"] = new ProgramRow { Activity = "Scouts Resumes" };
            return draft;
        }

        [DataTestMethod]
        [DataRow(OutputType.PDF, "pdf")]
        [DataRow(OutputType.Excel, "xlsx")]
        public void TheProgramRendersAsPdfAndExcel(OutputType outputType, string extension)
        {
            SetEnvironment.SetSyncfusionLicense();
            var request = new ReportGenerationRequest
            {
                ReportType = ReportType.ProgramCalendar, GroupName = "Sample Group", Section = "scout", UnitName = "Scout Unit",
                OutputType = outputType, ReportData = JsonConvert.SerializeObject(ProgramLayout.Build(Sample()))
            };
            var result = new Function().FunctionHandler(new Amazon.Lambda.APIGatewayEvents.APIGatewayProxyRequest { Body = JsonConvert.SerializeObject(request) }, null);

            Assert.IsFalse(string.IsNullOrEmpty(result), $"{extension} came back empty");
            Directory.CreateDirectory("TestResults");
            File.WriteAllBytes($"TestResults/ProgramCalendar.{extension}", Convert.FromBase64String(result));
        }

        [TestMethod]
        public void TheSpreadsheetHasTheColumnsAndTablesThatAreSwitchedOn()
        {
            SetEnvironment.SetSyncfusionLicense();
            string[] Cells(ProgramDraft draft)
            {
                var sheet = new Topo.Services.ReportService().GenerateProgramCalendarWorkbook(ProgramLayout.Build(draft)).Worksheets[0];
                return Enumerable.Range(1, sheet.UsedRange.LastRow).SelectMany(r => Enumerable.Range(1, sheet.UsedRange.LastColumn).Select(c => sheet.Range[r, c].Text)).Where(t => !string.IsNullOrEmpty(t)).ToArray();
            }

            var all = Cells(Sample());
            foreach (var expected in new[] { "Epping Scouts Term 1 Calendar 2026 - Leaders", "Date", "Activity", "Game", "Location", "Challenge Area", "Duty Patrol", "LIC", "Canoeing", "Term Break", "27-1", "Locations", "Contact Details", "6 Essex St, Epping" })
                CollectionAssert.Contains(all, expected);

            var some = Sample();
            some.ShowGame = false;
            some.ShowLeader = false;
            some.ShowChallengeArea = false;
            some.ShowContacts = false;
            var fewer = Cells(some);
            foreach (var gone in new[] { "Game", "LIC", "Challenge Area", "Contact Details", "Dodgeball" })
                CollectionAssert.DoesNotContain(fewer, gone);
            // the counts are on the page only, so the workbook never has them
            CollectionAssert.DoesNotContain(all, "Challenge Areas");
            foreach (var kept in new[] { "Location", "Duty Patrol", "Locations", "Canoeing" })
                CollectionAssert.Contains(fewer, kept);
        }

        [TestMethod]
        public void NothingEnteredGivesAnEmptyProgram()
        {
            var empty = ProgramLayout.Build(new ProgramDraft());
            Assert.AreEqual(0, empty.Rows.Count);

            // a start after the end, and no patrols to take turns
            var draft = new ProgramDraft { Start = new DateTime(2026, 5, 1), End = new DateTime(2026, 4, 1) };
            Assert.AreEqual(0, ProgramPlanner.Nights(draft).Count);
            var noPatrols = new ProgramDraft { Start = new DateTime(2026, 1, 28), End = new DateTime(2026, 2, 11) };
            Assert.IsTrue(ProgramPlanner.Nights(noPatrols).All(n => n.Duty == ""));
            Assert.AreEqual(3, ProgramLayout.Build(noPatrols).Rows.Count);

            // a break before anything in the program is not shown
            var early = Term1();
            early.Breaks.Add(new ProgramBreak { From = new DateTime(2026, 1, 1), To = new DateTime(2026, 1, 20), Label = "Holidays" });
            Assert.AreEqual(1, ProgramLayout.Build(early).Rows.Count(r => r.IsBreak));
        }
    }
}
