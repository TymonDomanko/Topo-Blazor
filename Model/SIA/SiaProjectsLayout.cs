namespace Topo.Model.SIA
{
    // One Scout's projects, oldest status change first
    public class SiaScoutProjects
    {
        public string MemberId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public List<SIAProjectListModel> Projects { get; set; } = new List<SIAProjectListModel>();
    }

    // A sheet of the report: the whole unit, or one patrol when the report is broken up by patrol
    public class SiaProjectsSheet
    {
        // Blank for the whole unit
        public string Patrol { get; set; } = string.Empty;
        public List<SiaScoutProjects> Scouts { get; set; } = new List<SiaScoutProjects>();
    }

    public static class SiaProjectsLayout
    {
        // The report for the Scouts named, each under the name given for their member id and in the order of those names
        public static List<SIAProjectListModel> ForMembers(IEnumerable<SIAProjectListModel> projects, IReadOnlyDictionary<string, string> namesById) =>
            projects
                .Where(p => namesById.ContainsKey(p.memberId))
                .Select(p => p.WithName(namesById[p.memberId]))
                .OrderBy(p => p.memberName)
                .ToList();

        // How the SIA project list is laid out, shared by the page's preview and the pdf and spreadsheet: a sheet for the unit, or for
        // each patrol, with each Scout's projects together. Scouts come in the order the projects are given.
        public static List<SiaProjectsSheet> Build(IEnumerable<SIAProjectListModel> projects, bool breakByPatrol)
        {
            var sheets = projects
                .GroupBy(p => breakByPatrol ? p.memberPatrol ?? "" : "")
                .OrderBy(g => g.Key)
                .Select(patrol => new SiaProjectsSheet
                {
                    Patrol = patrol.Key,
                    Scouts = patrol
                        .GroupBy(p => p.memberName)
                        .Select(scout => new SiaScoutProjects { MemberId = scout.First().memberId, Name = scout.Key, Projects = scout.OrderBy(p => p.statusUpdated).ToList() })
                        .ToList()
                })
                .ToList();
            // nothing to list still gives one sheet, with just its headings
            return sheets.Count > 0 ? sheets : new List<SiaProjectsSheet> { new SiaProjectsSheet() };
        }
    }
}
