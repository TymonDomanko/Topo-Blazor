using Topo.Model;
using System.Globalization;
using Topo.Model.Approvals;
using Topo.Model.Members;
using Topo.Model.SIA;

namespace Topo.Services
{
    public interface ISIAService
    {
        public Task<List<SIAProjectListModel>> GenerateSIAReportData(List<MemberListModel> selectedMembers, string section, string selectedUnitId, Action<string>? progress = null);
    }

    public class SIAService : ISIAService
    {
        private readonly ITerrainAPIService _terrainAPIService;

        public SIAService(ITerrainAPIService terrainAPIService)
        {
            _terrainAPIService = terrainAPIService;
        }

        public async Task<List<SIAProjectListModel>> GenerateSIAReportData(List<MemberListModel> selectedMembers, string section, string selectedUnitId, Action<string>? progress = null)
        {
            var approvals = new List<ApprovalsListModel>(); // await _approvalsService.GetApprovalListItems(selectedUnitId);
            TextInfo myTI = new CultureInfo("en-UA", false).TextInfo;
            var unitSiaProjects = new List<SIAProjectListModel>();
            // no one chosen, nothing to read
            if (selectedMembers.Count == 0)
                return unitSiaProjects;

            // One read gives every Scout's projects, so the unit isn't read once per Scout. Every record carries its member_id,
            // so ToLookup hands back one Scout's projects (a Scout with none gets an empty list). The records include earlier
            // sections, which are filtered out below.
            ILookup<string, Topo.Model.SIA.Result> projectsByMember;
            try
            {
                progress?.Invoke("the unit's projects");
                projectsByMember = (await _terrainAPIService.GetUnitAchievementsOfType<GetSIAResultsModel>(selectedUnitId, "special_interest_area"))
                    .results.ToLookup(r => r.member_id);
            }
            catch (Exception)
            {
                // the read failed for everyone chosen, so each of them gets the error row
                return selectedMembers.Select(ErrorRow).ToList();
            }

            foreach (var selectedMember in selectedMembers)
            {
                var member = new KeyValuePair<string, string>(selectedMember.id, $"{selectedMember.first_name} {selectedMember.last_name}");
                var memberSiaProjects = projectsByMember[member.Key].Where(r => r.section == section)
                    .Select(r => new SIAProjectListModel
                    {
                        memberId = member.Key,
                        memberName = member.Value,
                        memberPatrol = selectedMember.patrol_name ?? "",
                        area = myTI.ToTitleCase(r.answers.special_interest_area_selection.Replace("sia_", "").Replace("_", " ")),
                        projectName = r.answers.project_name,
                        status = myTI.ToTitleCase(r.status.Replace("_", " ")),
                        statusUpdated = AwardDate.Resolve(r.date_awarded, r.imported?.date_awarded, r.status_updated),
                        achievement_id = r.id
                    })
                    .ToList();
                foreach (var memberProject in memberSiaProjects)
                {
                    var approval = approvals.Where(a => a.achievement_id == memberProject.achievement_id && a.submission_type.ToLower() == "review").FirstOrDefault();
                    if (approval != null)
                    {
                        if (approval.presented_date.HasValue)
                        {
                            memberProject.status = "Presented";
                            memberProject.statusUpdated = approval.presented_date.Value.ToLocalTime();
                        }
                    }
                }
                unitSiaProjects.AddRange(memberSiaProjects);
            }

            return unitSiaProjects;
        }

        private static SIAProjectListModel ErrorRow(MemberListModel member) => new SIAProjectListModel
        {
            memberId = member.id,
            memberName = $"{member.first_name} {member.last_name}",
            memberPatrol = member.patrol_name ?? "",
            area = "Error",
            projectName = "Error",
            status = "Error",
            statusUpdated = DateTime.Now
        };

    }
}
