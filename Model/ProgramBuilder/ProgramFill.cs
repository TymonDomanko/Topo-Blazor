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
            var known = draft.NightEdits.Values.Concat(draft.Extras).Select(r => r.TerrainEventId).Where(id => id != "").ToHashSet();
            var nights = ProgramPlanner.Nights(draft).Where(n => !n.Skipped).Select(n => n.Date.Date).ToHashSet();
            int filled = 0, added = 0;
            var filledNow = new HashSet<string>();

            foreach (var evt in inTerrain
                .Where(e => !e.status.Contains("cancel", StringComparison.OrdinalIgnoreCase))
                .Where(e => Local(e.start_datetime).Date >= draft.Start.Date && Local(e.start_datetime).Date <= draft.End.Date)
                .OrderBy(e => e.start_datetime))
            {
                if (evt.id == "" || known.Contains(evt.id))
                    continue;
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
                        continue;
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
            return (filled, added);
        }

        private static void Copy(ProgramRow row, GetEventResultModel evt, DateTime start, DateTime end, string usualStart, string usualEnd)
        {
            row.Activity = evt.title.Trim();
            row.Location = evt.location.Trim();
            row.ChallengeArea = evt.challenge_area;
            row.TerrainEventId = evt.id;
            var organisers = evt.organisers.Length > 0 ? evt.organisers : new[] { evt.organiser };
            row.Leader = string.Join("/", organisers.Select(o => o.first_name.Trim()).Where(n => n != ""));
            // only times that are not the usual ones are kept
            row.StartTime = start.ToString("HH:mm") == usualStart ? "" : start.ToString("HH:mm");
            row.EndTime = end.ToString("HH:mm") == usualEnd ? "" : end.ToString("HH:mm");
        }

        private static ProgramRowKind KindOf(GetEventResultModel evt, DateTime start, DateTime end)
        {
            if (end.Date > start.Date)
                return ProgramRowKind.Camp;
            if ((evt.achievement_pathway_logbook_data?.categories ?? new object[0]).Any(c => string.Equals(c?.ToString(), "walking_hike", StringComparison.OrdinalIgnoreCase)))
                return ProgramRowKind.Hike;
            return start.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? ProgramRowKind.Weekend : ProgramRowKind.Other;
        }

        private static DateTime Local(DateTime value) => value.Kind == DateTimeKind.Utc ? value.ToLocalTime() : value;
    }
}
