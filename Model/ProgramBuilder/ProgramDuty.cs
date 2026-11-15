using Topo.Model.Program;

namespace Topo.Model.ProgramBuilder
{
    // Where a new program's duty cycle starts, so it picks up from where the one before left off
    public class DutyCycle
    {
        public List<string> Order { get; set; } = new List<string>();
        public string First { get; set; } = string.Empty;
    }

    public static class ProgramDuty
    {
        // The order the patrols take their turns on duty and which of them is next, from the duty patrols written in the activities Terrain has
        // before the date: the last run of different patrols, oldest first, then the patrols not seen in it in the order they were given, so
        // the next is the first of those, or the oldest in the run when every patrol is in it. A patrol that is not one of the given ones is
        // ignored; null when Terrain has none to go by.
        public static DutyCycle? CycleFrom(IEnumerable<GetEventResultModel> inTerrain, DateTime before, List<string> patrols)
        {
            var seen = inTerrain
                .Where(e => !e.status.Contains("cancel", StringComparison.OrdinalIgnoreCase) && Local(e.start_datetime).Date < before.Date)
                .Select(e => (Start: e.start_datetime, Duty: Known(ProgramDescription.Duty(e.description), patrols)))
                .Where(x => x.Duty != "").OrderByDescending(x => x.Start).Select(x => x.Duty).ToList();
            if (seen.Count == 0)
                return null;

            // newest first, until a patrol comes round again: that is one go of the cycle
            var run = new List<string>();
            foreach (var duty in seen)
            {
                if (run.Contains(duty))
                    break;
                run.Add(duty);
            }
            run.Reverse();
            var rest = patrols.Where(p => !run.Contains(p)).ToList();
            return new DutyCycle { Order = run.Concat(rest).ToList(), First = rest.Count > 0 ? rest[0] : run[0] };
        }

        // The patrol on duty at the last night of a program that is one of its duty patrols; blank when there is none
        public static string LastOnDuty(ProgramDraft draft)
        {
            var last = ProgramPlanner.Nights(draft).Where(n => !n.Skipped && IsOneOf(n.Duty, draft.DutyPatrols)).Select(n => n.Duty).LastOrDefault();
            return last ?? "";
        }

        // The patrol after the one on duty at the last night of a program, for the program that follows it
        public static string NextAfterProgram(ProgramDraft draft) => After(LastOnDuty(draft), draft.DutyPatrols);

        // The patrol that takes its turn after the given one, round the cycle; blank when that is not one of the patrols
        public static string After(string patrol, List<string> patrols)
        {
            var at = patrols.FindIndex(p => string.Equals(p, patrol, StringComparison.OrdinalIgnoreCase));
            return at < 0 ? "" : patrols[(at + 1) % patrols.Count];
        }

        // The patrol as the list of patrols has it, blank when it is not one of them
        private static string Known(string patrol, List<string> patrols) => patrols.FirstOrDefault(p => string.Equals(p, patrol, StringComparison.OrdinalIgnoreCase)) ?? "";

        private static bool IsOneOf(string patrol, List<string> patrols) => patrol != "" && patrols.Any(p => string.Equals(p, patrol, StringComparison.OrdinalIgnoreCase));

        private static DateTime Local(DateTime value) => value.Kind == DateTimeKind.Utc ? value.ToLocalTime() : value;
    }
}
