using System.Text.RegularExpressions;

namespace Topo.Model.ProgramBuilder
{
    // What the program keeps in an activity's description in Terrain, which has no field for it: the game and the duty patrol, a line each
    // ("Game: Tag", "Duty patrol: Magpie (Orange)"). Anything else written in the description is left as it is.
    public static class ProgramDescription
    {
        private static readonly Regex GameLine = new Regex(@"^\s*Game:\s*(.*?)\s*$", RegexOptions.IgnoreCase);
        private static readonly Regex DutyLine = new Regex(@"^\s*Duty patrol:\s*(.*?)\s*$", RegexOptions.IgnoreCase);

        public static string Game(string description) => Find(description, GameLine);

        public static string Duty(string description) => Find(description, DutyLine);

        // The two lines for a new activity
        public static string Compose(string game, string duty) => Merge("", game, duty);

        // The description with its game and duty lines made these, and the rest of it kept; a blank game or duty takes its line out
        public static string Merge(string description, string game, string duty)
        {
            var others = Lines(description).Where(l => !GameLine.IsMatch(l) && !DutyLine.IsMatch(l)).ToList();
            var lines = new List<string>();
            if (game.Trim() != "")
            {
                lines.Add($"Game: {game.Trim()}");
            }
            if (duty.Trim() != "")
            {
                lines.Add($"Duty patrol: {duty.Trim()}");
            }
            lines.AddRange(others);
            return string.Join("\n", lines);
        }

        private static string Find(string description, Regex line)
        {
            foreach (var l in Lines(description))
            {
                var m = line.Match(l);
                if (m.Success)
                {
                    return m.Groups[1].Value;
                }
            }
            return "";
        }

        private static IEnumerable<string> Lines(string description) =>
            (description ?? "").Replace("\r\n", "\n").Split('\n').Select(l => l.TrimEnd()).Where(l => l.Trim() != "");
    }
}
