namespace Topo.Model.OAS
{
    public enum OasWorksheetCellKind
    {
        // Nothing done yet
        Blank,
        // Ticked off and waiting to be sent for review or approved: shown in orange, with the date it was ticked (or Y)
        InProgress,
        // Awarded: shown in green
        Awarded
    }

    public class OasWorksheetCell
    {
        public OasWorksheetCellKind Kind { get; set; }
        public string Text { get; set; } = string.Empty;
    }

    // Plan, Do or Review (or Verify) over a run of requirements
    public class OasWorksheetGroup
    {
        public string Heading { get; set; } = string.Empty;
        // A hex colour, or blank for none
        public string Colour { get; set; } = string.Empty;
        // How many requirements it covers
        public int Span { get; set; }
    }

    // One worksheet: the requirements of one stage down the side and the Scouts across, as Terrain's own page has it
    public class OasWorksheetSheet
    {
        // The stage's title, with the patrol after it when broken up by patrol; the name of the sheet in the downloaded report too
        public string Title { get; set; } = string.Empty;
        public List<OasWorksheetGroup> Groups { get; set; } = new List<OasWorksheetGroup>();
        public List<string> Requirements { get; set; } = new List<string>();
        public List<string> Scouts { get; set; } = new List<string>();
        // [requirement][scout]
        public List<List<OasWorksheetCell>> Cells { get; set; } = new List<List<OasWorksheetCell>>();
    }

    // How a worksheet is laid out, from the answers read for it, with the rules the downloaded worksheet uses so the screen matches it
    public static class OasWorksheetLayout
    {
        public static string ColourOf(string heading) => heading switch
        {
            "Plan" => "#add8e6",
            "Do" => "#87ceeb",
            "Review" => "#6495ed",
            "Verify" => "#4169e1",
            _ => ""
        };

        public static List<OasWorksheetSheet> Build(IEnumerable<OASWorksheetAnswers> answers, bool breakByPatrol)
        {
            var sheets = new List<OasWorksheetSheet>();
            foreach (var template in answers.GroupBy(a => a.TemplateTitle + (breakByPatrol ? " - " + a.MemberPatrol : "")).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                var scouts = template.GroupBy(a => a.MemberName + "|" + a.MemberId).OrderBy(g => g.Key, StringComparer.Ordinal).ToList();
                var requirements = scouts.First()
                    .OrderBy(a => a.InputTitleSortIndex).ThenBy(a => a.InputSortIndex)
                    .Select(a => (a.InputId, a.InputTitle, a.InputLabel))
                    .ToList();

                var sheet = new OasWorksheetSheet
                {
                    Title = template.Key,
                    Requirements = requirements.Select(r => r.InputLabel).ToList(),
                    Scouts = scouts.Select(s => s.First().MemberName).ToList()
                };
                foreach (var group in requirements.Select(r => r.InputTitle).Aggregate(new List<(string heading, int span)>(), (runs, title) =>
                {
                    if (runs.Count > 0 && runs[^1].heading == title)
                    {
                        runs[^1] = (title, runs[^1].span + 1);
                    }
                    else
                    {
                        runs.Add((title, 1));
                    }
                    return runs;
                }))
                    sheet.Groups.Add(new OasWorksheetGroup { Heading = group.heading, Colour = ColourOf(group.heading), Span = group.span });

                // each Scout's answers in the order of the requirements, then turned so a row is a requirement
                var perScout = scouts.Select(s => s.OrderBy(a => a.InputTitleSortIndex).ThenBy(a => a.InputSortIndex).Select(CellOf).ToList()).ToList();
                for (var r = 0; r < requirements.Count; r++)
                    sheet.Cells.Add(perScout.Select(cells => r < cells.Count ? cells[r] : new OasWorksheetCell()).ToList());
                sheets.Add(sheet);
            }
            return sheets;
        }

        // An awarded requirement is green (with text only for the logbook one); one ticked but not awarded is orange with its date, or Y
        private static OasWorksheetCell CellOf(OASWorksheetAnswers answer)
        {
            if (!answer.Answered)
            {
                return new OasWorksheetCell();
            }
            if (answer.Awarded)
            {
                return new OasWorksheetCell { Kind = OasWorksheetCellKind.Awarded, Text = answer.InputId == "logbook_up_to_date" ? answer.MemberAnswer : "" };
            }
            return new OasWorksheetCell { Kind = OasWorksheetCellKind.InProgress, Text = string.IsNullOrEmpty(answer.MemberAnswer) ? "Y" : answer.MemberAnswer };
        }
    }
}
