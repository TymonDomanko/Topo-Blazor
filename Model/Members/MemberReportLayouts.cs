namespace Topo.Model.Members
{
    // A person on a patrol list or patrol sheet
    public class PatrolLine
    {
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Duty { get; set; } = string.Empty;
        // Patrol leaders and the like are shaded
        public bool HasDuty => !string.IsNullOrEmpty(Duty);
    }

    public class PatrolGroup
    {
        // Blank for the youth who are in no patrol
        public string Name { get; set; } = string.Empty;
        // What the patrol is called on a list or sheet
        public string DisplayName => string.IsNullOrEmpty(Name) ? "Unassigned" : Name;
        public List<PatrolLine> Members { get; set; } = new List<PatrolLine>();
    }

    public class PatrolListLayout
    {
        public List<PatrolGroup> Patrols { get; set; } = new List<PatrolGroup>();
        public List<PatrolLine> UnitCouncil { get; set; } = new List<PatrolLine>();
        // Null unless the adult leaders were asked for
        public List<PatrolLine>? AdultLeaders { get; set; }
        // False when no one has anything for the surname column, so it can be left out
        public bool ShowLastName => MemberReportLayouts.ShowLastName(Patrols)
            || UnitCouncil.Any(m => !string.IsNullOrWhiteSpace(m.LastName))
            || (AdultLeaders?.Any(m => !string.IsNullOrWhiteSpace(m.LastName)) ?? false);
    }

    // How the member list is ordered
    public enum MemberListSort
    {
        Alphabetical,
        Positions,
        Patrols,
        Age,
        MemberNumber
    }

    public enum AgeBand
    {
        None,
        // The second last year in the section
        SecondLast,
        // The last year, or the one before the last
        Last
    }

    public class MemberListRow
    {
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Age { get; set; } = string.Empty;
        public string MemberNumber { get; set; } = string.Empty;
        public string Duty { get; set; } = string.Empty;
        public string Patrol { get; set; } = string.Empty;
        public AgeBand AgeBand { get; set; }
    }

    // How the patrol list, member list and patrol sheets are laid out, shared by the page's previews and the pdfs and spreadsheets
    public static class MemberReportLayouts
    {
        // The oldest a youth member can be in each section
        public static int MaxAge(string section) => section switch
        {
            "joey" => 8,
            "cub" => 11,
            "scout" => 15,
            "venturer" => 18,
            _ => 26
        };

        // Whether the surname column has anything in it: with short names it is empty unless two people share a first name
        public static bool ShowLastName(IEnumerable<MemberListRow> rows) => rows.Any(r => !string.IsNullOrWhiteSpace(r.LastName));

        public static bool ShowLastName(IEnumerable<PatrolGroup> patrols) => patrols.Any(p => p.Members.Any(m => !string.IsNullOrWhiteSpace(m.LastName)));

        // The people named as the sign in sheet names them, for a list or sheet that shows a first name and a surname column. By default
        // that is the first name with as many letters of the surname as it takes to tell people of the same first name apart, so the
        // surname column holds only those letters (or nothing); in full it holds the whole surname. The order given is kept.
        public static List<MemberListModel> WithNames(IEnumerable<MemberListModel> people, bool useFullNames)
        {
            var list = people.ToList();
            var names = SignInSheetLayout.GetDisplayNames(list, useFullNames);
            return list.Select(m =>
            {
                var first = (m.first_name ?? "").Trim();
                // the part of the name after the first name is what goes in the surname column
                var display = names.TryGetValue(m.id, out var name) ? name : first;
                var named = m.Clone();
                named.first_name = first;
                named.last_name = display.StartsWith(first, StringComparison.OrdinalIgnoreCase) ? display.Substring(first.Length).Trim() : "";
                return named;
            }).ToList();
        }

        // The members a patrol list is made from: the youth by patrol, and the adult leaders too when they are asked for
        public static List<MemberListModel> PatrolListMembers(IEnumerable<MemberListModel> members, bool includeLeaders, bool useFullNames) =>
            WithNames(members.Where(m => includeLeaders || m.isAdultLeader == 0).OrderBy(m => m.patrol_name), useFullNames);

        // The members a member list is made from: the youth, in the order asked for
        public static List<MemberListModel> MemberListMembers(IEnumerable<MemberListModel> members, MemberListSort sort, bool useFullNames) =>
            WithNames(Sorted(members.Where(m => m.isAdultLeader == 0), sort), useFullNames);

        // The members the patrol sheets are made from: the youth by patrol
        public static List<MemberListModel> PatrolSheetMembers(IEnumerable<MemberListModel> members, bool useFullNames) =>
            WithNames(members.Where(m => m.isAdultLeader == 0).OrderBy(m => m.patrol_name), useFullNames);

        // Every way of ordering falls back to the name, so people who tie come out the same way each time
        public static List<MemberListModel> Sorted(IEnumerable<MemberListModel> members, MemberListSort sort)
        {
            var byName = members.OrderBy(m => m.first_name).ThenBy(m => m.last_name);
            switch (sort)
            {
                case MemberListSort.Positions:
                    // the unit leader, then patrol leaders (a Sixer counts as one), assistant patrol leaders, then everyone else
                    return members.OrderBy(m => PositionRank(m)).ThenBy(m => m.first_name).ThenBy(m => m.last_name).ToList();
                case MemberListSort.Patrols:
                    // the unit leader first, then by patrol (people without one last); within a patrol the patrol leader, then the
                    // assistant patrol leader, then everyone else by name: the order the sign in sheet uses
                    return members
                        .OrderBy(m => m.patrol_duty == "UL" ? 0 : 1)
                        .ThenBy(m => string.IsNullOrWhiteSpace(m.patrol_name) ? 1 : 0)
                        .ThenBy(m => m.patrol_name ?? "", StringComparer.OrdinalIgnoreCase)
                        .ThenBy(m => SignInSheetLayout.DutyRank(m.patrol_duty))
                        .ThenBy(m => m.first_name)
                        .ThenBy(m => m.last_name)
                        .ToList();
                case MemberListSort.Age:
                    // the oldest first, so those nearest moving on are at the top; an age that can't be read goes last
                    return members.OrderByDescending(m => AgeInMonths(m.age)).ThenBy(m => m.first_name).ThenBy(m => m.last_name).ToList();
                case MemberListSort.MemberNumber:
                    // by number as a number where it is one, then anything else, and anyone with no number last
                    return members
                        .OrderBy(m => long.TryParse(m.member_number, out _) ? 0 : string.IsNullOrWhiteSpace(m.member_number) ? 2 : 1)
                        .ThenBy(m => long.TryParse(m.member_number, out var number) ? number : 0)
                        .ThenBy(m => m.member_number, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(m => m.first_name)
                        .ThenBy(m => m.last_name)
                        .ToList();
                default:
                    return byName.ToList();
            }
        }

        private static int PositionRank(MemberListModel m) => m.patrol_duty == "UL" ? 0 : SignInSheetLayout.DutyRank(m.patrol_duty) + 1;

        // An age as Terrain writes it, such as "12y 11m", in months; -1 when it isn't in that form
        public static int AgeInMonths(string? age)
        {
            var match = System.Text.RegularExpressions.Regex.Match(age ?? "", @"^\s*(\d+)\s*y(?:\s*(\d+)\s*m)?");
            return match.Success ? int.Parse(match.Groups[1].Value) * 12 + (match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 0) : -1;
        }

        private static PatrolLine Line(MemberListModel m) => new PatrolLine { FirstName = m.first_name, LastName = m.last_name, Duty = m.patrol_duty };

        // The youth by patrol, in the order the members are given, each patrol with its leaders first
        public static List<PatrolGroup> Patrols(IEnumerable<MemberListModel> sortedMembers) =>
            sortedMembers
                .Where(m => m.isAdultLeader == 0)
                .GroupBy(m => m.patrol_name)
                .Select(patrol => new PatrolGroup { Name = patrol.Key ?? "", Members = patrol.OrderByDescending(p => p.patrol_duty).Select(Line).ToList() })
                .ToList();

        public static PatrolListLayout PatrolList(IEnumerable<MemberListModel> sortedMembers, bool includeLeaders)
        {
            var members = sortedMembers.ToList();
            return new PatrolListLayout
            {
                Patrols = Patrols(members),
                UnitCouncil = members.Where(m => m.unit_council).OrderBy(m => m.isAdultLeader).ThenBy(m => m.first_name).ThenBy(m => m.last_name).Select(Line).ToList(),
                AdultLeaders = includeLeaders
                    ? members.Where(m => m.isAdultLeader == 1).OrderBy(m => m.first_name).ThenBy(m => m.last_name).Select(Line).ToList()
                    : null
            };
        }

        // The ages that are shaded: the last year in the section and the one before are rose, the one before that is yellow
        public static List<MemberListRow> MemberList(IEnumerable<MemberListModel> sortedMembers, string section)
        {
            var maxAge = MaxAge(section);
            return sortedMembers.Select(m =>
            {
                var age = m.age ?? "";
                var band = AgeBand.None;
                if (age.StartsWith((maxAge - 2).ToString()))
                    band = AgeBand.SecondLast;
                if (age.StartsWith(maxAge.ToString()) || age.StartsWith((maxAge - 1).ToString()))
                    band = AgeBand.Last;
                return new MemberListRow
                {
                    FirstName = m.first_name,
                    LastName = m.last_name,
                    Age = age,
                    MemberNumber = m.member_number,
                    Duty = m.patrol_duty,
                    Patrol = (m.patrol_name ?? "").Trim(),
                    AgeBand = band
                };
            }).ToList();
        }
    }
}
