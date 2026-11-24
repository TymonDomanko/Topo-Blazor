using Topo.Model.Program;

namespace Topo.Model.ProgramBuilder
{
    // Fills the program in from the activities already in Terrain: a section night with nothing entered takes the activity of that day, and
    // anything else becomes a camp, hike, weekend or other activity. Each row keeps the Terrain event it came from, so it is not put in
    // Terrain again.
    public static class ProgramFill
    {
        // How many rows were filled in and how many were added
        public static (int Filled, int Added) Apply(ProgramDraft draft, IEnumerable<GetEventResultModel> inTerrain)
        {
            LinkToEvents(draft, inTerrain);

            // what was filled in from an activity before its parts of the Scout Method were kept takes them from it now
            var byId = inTerrain.Where(e => e.id != "").GroupBy(e => e.id).ToDictionary(g => g.Key, g => g.First());
            foreach (var row in draft.NightEdits.Values.Concat(draft.Extras).Where(r => r.TerrainEventId != "" && r.ScoutMethod.Count == 0))
                if (byId.TryGetValue(row.TerrainEventId, out var source) && source.review?.scout_method_elements?.Length > 0)
                {
                    row.ScoutMethod = source.review.scout_method_elements.ToList();
                }

            var known = draft.NightEdits.Values.Concat(draft.Extras).Select(r => r.TerrainEventId).Where(id => id != "").ToHashSet();
            var nights = ProgramPlanner.Nights(draft).Where(n => !n.Skipped).Select(n => n.Date.Date).ToHashSet();
            int filled = 0, added = 0;
            var filledNow = new HashSet<string>();

            foreach (var evt in InProgram(draft, inTerrain))
            {
                if (evt.id == "" || known.Contains(evt.id))
                {
                    continue;
                }
                var start = Local(evt.start_datetime);
                var end = Local(evt.end_datetime);
                var singleDay = end.Date <= start.Date;
                var key = ProgramPlanner.NightKey(start);

                // a single day activity on a section night fills that night when nothing has been entered for it; when something has, it is left
                // for the changes to make in Terrain to show; and a second one on the same night is an extra
                if (singleDay && nights.Contains(start.Date))
                {
                    var hasContent = draft.NightEdits.TryGetValue(key, out var edited) && (edited.Activity != "" || edited.TerrainEventId != "");
                    if (hasContent && !filledNow.Contains(key))
                    {
                        continue;
                    }
                    if (!hasContent)
                    {
                        var row = edited ?? new ProgramRow();
                        Copy(row, evt, start, end, draft.NightStart, draft.NightEnd);
                        draft.NightEdits[key] = row;
                        filledNow.Add(key);
                        known.Add(evt.id);
                        filled++;
                        continue;
                    }
                }

                var extra = new ProgramRow { Kind = KindOf(evt, start, end), Date = start.Date, EndDate = singleDay ? null : end.Date };
                Copy(extra, evt, start, end, extra.Kind == ProgramRowKind.Camp ? draft.CampStart : draft.OtherStart, extra.Kind == ProgramRowKind.Camp ? draft.CampEnd : draft.OtherEnd);
                draft.Extras.Add(extra);
                known.Add(evt.id);
                added++;
            }
            PinDutyPatrols(draft, byId);
            return (filled, added);
        }

        // A night or camp entered in the program that is in Terrain already (an activity of the same name that day), such as one added to Terrain from
        // here before the id was kept, is tied to it, so that what is read from it and what is changed in it is that one
        private static void LinkToEvents(ProgramDraft draft, IEnumerable<GetEventResultModel> inTerrain)
        {
            var claimed = draft.NightEdits.Values.Concat(draft.Extras).Select(r => r.TerrainEventId).Where(id => id != "").ToHashSet();
            var events = InProgram(draft, inTerrain).Where(e => e.id != "" && !claimed.Contains(e.id)).ToList();
            foreach (var night in draft.NightEdits.Where(n => n.Value.TerrainEventId == "" && n.Value.Activity.Trim() != "").ToList())
            {
                var match = events.FirstOrDefault(e => ProgramPlanner.NightKey(Local(e.start_datetime)) == night.Key && string.Equals(e.title.Trim(), night.Value.Activity.Trim(), StringComparison.OrdinalIgnoreCase));
                if (match == null)
                {
                    continue;
                }
                night.Value.TerrainEventId = match.id;
                events.Remove(match);
            }
            foreach (var extra in draft.Extras.Where(r => r.TerrainEventId == "" && ProgramPlanner.IsDated(r) && r.Activity.Trim() != ""))
            {
                var match = events.FirstOrDefault(e => Local(e.start_datetime).Date == extra.Date.Date && string.Equals(e.title.Trim(), extra.Activity.Trim(), StringComparison.OrdinalIgnoreCase));
                if (match == null)
                {
                    continue;
                }
                extra.TerrainEventId = match.id;
                events.Remove(match);
            }
        }

        // The duty patrol written in an activity's description is the night's, so a program picks up where Terrain says it is. A night is only put on
        // duty by hand where the cycle would not have it there, as a night put on by hand does not use up a turn in the cycle, and a night the leader
        // has already put on duty, or on none, is left as they chose.
        private static void PinDutyPatrols(ProgramDraft draft, Dictionary<string, GetEventResultModel> byId)
        {
            var wanted = draft.NightEdits.Where(n => n.Value.TerrainEventId != "" && n.Value.DutyPatrol == "" && !n.Value.NoDuty)
                .Where(n => byId.ContainsKey(n.Value.TerrainEventId))
                .Select(n => (n.Key, Duty: ProgramDescription.Duty(byId[n.Value.TerrainEventId].description)))
                .Where(x => x.Duty != "").OrderBy(x => x.Key).ToList();
            foreach (var (key, duty) in wanted)
            {
                var night = ProgramPlanner.Nights(draft).FirstOrDefault(n => ProgramPlanner.NightKey(n.Date) == key);
                if (night == null || night.Skipped || string.Equals(night.Duty, duty, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                draft.NightEdits[key].DutyPatrol = draft.DutyPatrols.FirstOrDefault(p => string.Equals(p, duty, StringComparison.OrdinalIgnoreCase)) ?? duty;
            }
        }

        // Makes a contact of each of the unit's leaders (the adults in its membership) that has not been before, with their name as Terrain has it;
        // Terrain's member list has no position or email, so those are left to be entered. How many were added.
        public static int AddLeaderContacts(ProgramDraft draft, IEnumerable<TerrainLeader> leaders)
        {
            var added = 0;
            foreach (var leader in leaders)
            {
                if (leader.Id == "" || draft.TakenLeaders.Contains(leader.Id))
                {
                    continue;
                }
                draft.TakenLeaders.Add(leader.Id);
                var name = $"{leader.FirstName} {leader.LastName}".Trim();
                if (name == "" || draft.Contacts.Any(c => string.Equals(c.Name.Trim(), name, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }
                draft.Contacts.Add(new ProgramContact { Name = name, MemberId = leader.Id });
                added++;
            }
            return added;
        }

        // The events that are not cancelled and start between the program's dates, in the order they happen
        private static IEnumerable<GetEventResultModel> InProgram(ProgramDraft draft, IEnumerable<GetEventResultModel> inTerrain) => inTerrain
            .Where(e => !e.status.Contains("cancel", StringComparison.OrdinalIgnoreCase))
            .Where(e => Local(e.start_datetime).Date >= draft.Start.Date && Local(e.start_datetime).Date <= draft.End.Date)
            .OrderBy(e => e.start_datetime);

        // Puts the places the program goes to in its list of locations, those not in it already, with no address to begin with: the location of
        // each running section night and each camp and the like that has a date. A place named for an idea or an activity that is not in the program
        // yet is not added until it is. How many were added.
        public static int AddProgramLocations(ProgramDraft draft)
        {
            var added = 0;
            var rows = ProgramPlanner.Nights(draft).Where(n => !n.Skipped).Concat(draft.Extras.Where(ProgramPlanner.IsDated)).OrderBy(r => r.Date);
            foreach (var name in rows.Select(r => (r.Location ?? "").Trim()).Where(n => n != ""))
            {
                if (draft.Locations.Any(l => string.Equals(l.Name.Trim(), name, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }
                draft.Locations.Add(new ProgramLocation { Name = name });
                added++;
            }
            return added;
        }

        private static void Copy(ProgramRow row, GetEventResultModel evt, DateTime start, DateTime end, string usualStart, string usualEnd)
        {
            row.Activity = evt.title.Trim();
            row.Location = evt.location.Trim();
            row.ChallengeArea = evt.challenge_area;
            row.TerrainEventId = evt.id;
            row.Game = ProgramDescription.Game(evt.description);
            var organisers = evt.organisers.Length > 0 ? evt.organisers : new[] { evt.organiser };
            row.Leader = string.Join("/", organisers.Select(o => o.first_name.Trim()).Where(n => n != ""));
            // only times that are not the usual ones are kept
            row.StartTime = start.ToString("HH:mm") == usualStart ? "" : start.ToString("HH:mm");
            row.EndTime = end.ToString("HH:mm") == usualEnd ? "" : end.ToString("HH:mm");
            row.ScoutMethod = (evt.review?.scout_method_elements ?? new string[0]).ToList();
        }

        private static ProgramRowKind KindOf(GetEventResultModel evt, DateTime start, DateTime end)
        {
            if (end.Date > start.Date)
            {
                return ProgramRowKind.Camp;
            }
            if ((evt.achievement_pathway_logbook_data?.categories ?? new object[0]).Any(c => string.Equals(c?.ToString(), "walking_hike", StringComparison.OrdinalIgnoreCase)))
            {
                return ProgramRowKind.Hike;
            }
            return start.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? ProgramRowKind.Weekend : ProgramRowKind.Other;
        }

        private static DateTime Local(DateTime value) => value.Kind == DateTimeKind.Utc ? value.ToLocalTime() : value;
    }
}
