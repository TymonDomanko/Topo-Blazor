using Topo.Model.Program;

namespace Topo.Model.ProgramBuilder
{
    // Where a new program's duty cycle starts, so it picks up from where the one before left off
    public static class ProgramDuty
    {
        // The patrol after the one on duty at the last night before the date that Terrain has a duty patrol written for (in the description of the
        // activity), in the order the patrols take their turns; blank when there is none, or it is not one of them.
        public static string NextAfter(IEnumerable<GetEventResultModel> inTerrain, DateTime before, List<string> patrols)
        {
            var last = inTerrain
                .Where(e => !e.status.Contains("cancel", StringComparison.OrdinalIgnoreCase) && Local(e.start_datetime).Date < before.Date)
                .Select(e => (Start: e.start_datetime, Duty: ProgramDescription.Duty(e.description)))
                .Where(x => x.Duty != "").OrderByDescending(x => x.Start).Select(x => x.Duty).FirstOrDefault();
            return last == null ? "" : After(last, patrols);
        }

        // The patrol after the one on duty at the last night of a program, for the program that follows it
        public static string NextAfterProgram(ProgramDraft draft)
        {
            var last = ProgramPlanner.Nights(draft).Where(n => !n.Skipped && n.Duty != "").Select(n => n.Duty).LastOrDefault();
            return last == null ? "" : After(last, draft.DutyPatrols);
        }

        private static string After(string patrol, List<string> patrols)
        {
            var at = patrols.FindIndex(p => string.Equals(p, patrol, StringComparison.OrdinalIgnoreCase));
            return at < 0 ? "" : patrols[(at + 1) % patrols.Count];
        }

        private static DateTime Local(DateTime value) => value.Kind == DateTimeKind.Utc ? value.ToLocalTime() : value;
    }
}
