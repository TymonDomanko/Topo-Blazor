using Topo.Model.Members;

namespace Topo.Model.Attendance
{
    public class AttendanceColumn
    {
        // The heading over the columns of one measure, such as "Scout nights (10)"
        public string Group { get; set; } = string.Empty;
        // "No." for how many events, "%" for how many of the possible ones
        public string Heading { get; set; } = string.Empty;
        public bool IsPercent { get; set; }
    }

    public class AttendanceCell
    {
        public string Text { get; set; } = string.Empty;
        public double? Value { get; set; }
        // The background, red for the lowest in the column through yellow to green for the highest; blank for none
        public string Colour { get; set; } = string.Empty;
    }

    public class AttendanceRow
    {
        public string Name { get; set; } = string.Empty;
        public string Patrol { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public List<AttendanceCell> Cells { get; set; } = new List<AttendanceCell>();
    }

    public class AttendanceTable
    {
        // Blank when the report is not split by patrol
        public string Patrol { get; set; } = string.Empty;
        public List<AttendanceRow> Rows { get; set; } = new List<AttendanceRow>();
        public List<AttendanceCell> Average { get; set; } = new List<AttendanceCell>();
    }

    // The attendance report as the page shows it and the pdf and spreadsheet print it
    public class AttendanceReport
    {
        public string UnitName { get; set; } = string.Empty;
        public string Section { get; set; } = string.Empty;
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public DateTime AsAt { get; set; }
        // The day the Scout nights fall on, blank when there are none
        public string MeetingDay { get; set; } = string.Empty;
        public int EventsCounted { get; set; }
        public int EventsLeftOut { get; set; }
        public bool BreakByPatrol { get; set; }
        public List<AttendanceColumn> Columns { get; set; } = new List<AttendanceColumn>();
        public List<AttendanceTable> Tables { get; set; } = new List<AttendanceTable>();
    }

    public static class AttendanceLayout
    {
        private static readonly (AttendanceKind Kind, string Label)[] Kinds =
        {
            (AttendanceKind.ScoutNight, "Scout nights"), (AttendanceKind.Other, "Other activities"), (AttendanceKind.Camp, "Camps"),
            (AttendanceKind.Hike, "Hikes"), (AttendanceKind.UnitCouncil, "Unit councils")
        };

        private static readonly (string Area, string Label)[] Areas =
        {
            ("outdoors", "Outdoors"), ("community", "Community"), ("personal_growth", "Personal Growth"), ("creative", "Creative")
        };

        // A column is a function of the member and what it counts, so the same rule makes the cells for every row
        private class Measure
        {
            public AttendanceColumn Column = new AttendanceColumn();
            public Func<AttendanceEvent, bool> Counts = _ => true;
            // Only those it applies to, such as the unit council for members of it
            public Func<MemberListModel, bool> AppliesTo = _ => true;
            public bool IsPercent;
        }

        public static AttendanceReport Build(AttendanceData data, bool breakByPatrol, bool useFullNames)
        {
            var report = new AttendanceReport
            {
                UnitName = data.UnitName, Section = data.Section, From = data.From, To = data.To, AsAt = data.AsAt,
                MeetingDay = data.MeetingDay == null || !data.Events.Any(e => e.Kind == AttendanceKind.ScoutNight) ? "" : data.MeetingDay.Value.ToString(),
                EventsCounted = data.Events.Count, EventsLeftOut = data.EventsLeftOut, BreakByPatrol = breakByPatrol
            };
            if (data.Events.Count == 0 || data.Youth.Count == 0)
                return report;

            var measures = new List<Measure>();
            foreach (var (kind, label) in Kinds)
            {
                var count = data.Events.Count(e => e.Kind == kind);
                if (count > 0)
                    AddPair(measures, $"{label} ({count})", e => e.Kind == kind, kind == AttendanceKind.UnitCouncil ? (m => m.unit_council) : (m => true));
            }
            AddPair(measures, $"All events ({data.Events.Count})", e => true, m => true);
            foreach (var (area, label) in Areas)
            {
                var count = data.Events.Count(e => string.Equals(e.ChallengeArea, area, StringComparison.OrdinalIgnoreCase));
                if (count > 0)
                    measures.Add(new Measure
                    {
                        Column = new AttendanceColumn { Group = "Challenge areas", Heading = $"{label} ({count})", IsPercent = true },
                        Counts = e => string.Equals(e.ChallengeArea, area, StringComparison.OrdinalIgnoreCase), IsPercent = true
                    });
            }
            report.Columns = measures.Select(m => m.Column).ToList();

            var names = SignInSheetLayout.GetDisplayNames(data.Youth, useFullNames);
            var rows = Sorted(data.Youth).Select(member => new AttendanceRow
            {
                Name = names.TryGetValue(member.id, out var name) ? name : $"{member.first_name} {member.last_name}".Trim(),
                Patrol = member.patrol_name ?? "",
                Role = member.patrol_duty ?? "",
                Cells = measures.Select(m => Cell(data, member, m)).ToList()
            }).ToList();

            ApplyColours(rows, measures.Count);

            report.Tables = breakByPatrol
                ? rows.GroupBy(r => r.Patrol).Select(g => new AttendanceTable { Patrol = g.Key == "" ? "Unit" : g.Key, Rows = g.ToList() }).ToList()
                : new List<AttendanceTable> { new AttendanceTable { Rows = rows } };
            foreach (var table in report.Tables)
                table.Average = Enumerable.Range(0, measures.Count).Select(i => Average(table.Rows.Select(r => r.Cells[i]), measures[i].IsPercent)).ToList();
            return report;
        }

        // The number of events and the share of the possible ones, side by side
        private static void AddPair(List<Measure> measures, string group, Func<AttendanceEvent, bool> counts, Func<MemberListModel, bool> appliesTo)
        {
            measures.Add(new Measure { Column = new AttendanceColumn { Group = group, Heading = "No." }, Counts = counts, AppliesTo = appliesTo });
            measures.Add(new Measure { Column = new AttendanceColumn { Group = group, Heading = "%", IsPercent = true }, Counts = counts, AppliesTo = appliesTo, IsPercent = true });
        }

        private static AttendanceCell Cell(AttendanceData data, MemberListModel member, Measure measure)
        {
            if (!measure.AppliesTo(member))
                return new AttendanceCell();
            // only the events since they joined the section; none at all (joined after them all) leaves the cell blank
            var possible = data.PossibleFor(member, measure.Counts);
            if (possible.Count == 0)
                return new AttendanceCell();
            var key = AttendanceData.Key(member);
            var attended = possible.Count(e => e.Attended.Contains(key));
            if (!measure.IsPercent)
                return new AttendanceCell { Text = attended.ToString(), Value = attended };
            var share = (double)attended / possible.Count;
            return new AttendanceCell { Text = Percent(share), Value = share };
        }

        private static AttendanceCell Average(IEnumerable<AttendanceCell> cells, bool isPercent)
        {
            var values = cells.Where(c => c.Value != null).Select(c => c.Value!.Value).ToList();
            if (values.Count == 0)
                return new AttendanceCell();
            var mean = values.Average();
            return new AttendanceCell { Text = isPercent ? Percent(mean) : mean.ToString("0.0"), Value = mean };
        }

        private static string Percent(double share) => $"{Math.Round(share * 100)}%";

        // The unit leader first, then by patrol, and within a patrol the patrol leader, the assistant, then everyone else by name
        private static IEnumerable<MemberListModel> Sorted(IEnumerable<MemberListModel> youth) =>
            youth.OrderBy(m => m.patrol_order == 0 ? 0 : 1).ThenBy(m => m.patrol_name ?? "", StringComparer.OrdinalIgnoreCase)
                .ThenBy(m => SignInSheetLayout.DutyRank(m.patrol_duty))
                .ThenBy(m => m.first_name, StringComparer.OrdinalIgnoreCase).ThenBy(m => m.last_name, StringComparer.OrdinalIgnoreCase);

        // Colours every column from red at its lowest value through yellow in the middle to green at its highest, as a spreadsheet's colour scale does
        private static void ApplyColours(List<AttendanceRow> rows, int columns)
        {
            for (var i = 0; i < columns; i++)
            {
                var cells = rows.Select(r => r.Cells[i]).Where(c => c.Value != null).ToList();
                if (cells.Count == 0)
                    continue;
                var sorted = cells.Select(c => c.Value!.Value).OrderBy(v => v).ToList();
                var low = sorted[0];
                var high = sorted[^1];
                var middle = Median(sorted);
                if (high <= low)
                    continue;
                foreach (var cell in cells)
                    cell.Colour = Colour(cell.Value!.Value, low, middle, high);
            }
        }

        private static double Median(List<double> sorted) =>
            sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;

        public static string Colour(double value, double low, double middle, double high)
        {
            var (from, to, fromValue, toValue) = value <= middle
                ? ((0xF8, 0x69, 0x6B), (0xFF, 0xEB, 0x84), low, middle)
                : ((0xFF, 0xEB, 0x84), (0x63, 0xBE, 0x7B), middle, high);
            // when most of the column sits at its lowest value, the middle is the lowest and those are red
            var t = toValue <= fromValue ? 0.0 : Math.Clamp((value - fromValue) / (toValue - fromValue), 0, 1);
            int Mix(int a, int b) => (int)Math.Round(a + (b - a) * t);
            return $"#{Mix(from.Item1, to.Item1):X2}{Mix(from.Item2, to.Item2):X2}{Mix(from.Item3, to.Item3):X2}";
        }
    }
}
