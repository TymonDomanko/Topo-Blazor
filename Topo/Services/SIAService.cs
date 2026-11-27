using Topo.Model;
using System.Globalization;
using Topo.Model.Approvals;
using Topo.Model.Cache;
using Topo.Model.SIA;

namespace Topo.Services
{
    public interface ISIAService
    {
        // The Scouts' Special Interest Area projects for the report. Reads come from the data cache while in date, unless a CacheScope asks otherwise.
        public Task<List<SIAProjectListModel>> GenerateSIAReportData(List<KeyValuePair<string, string>> selectedMembers, string section, string selectedUnitId,
            Action<string>? progress = null, CancellationToken cancellationToken = default);
        // When the oldest of what is kept of these Scouts' projects was read; null when nothing is
        public Task<DateTime?> OldestReadAsync(IReadOnlyList<string> memberIds);
    }

    public class SIAService : ISIAService
    {
        private readonly ITerrainAPIService _terrainAPIService;

        public SIAService(ITerrainAPIService terrainAPIService)
        {
            _terrainAPIService = terrainAPIService;
        }

        public Task<DateTime?> OldestReadAsync(IReadOnlyList<string> memberIds) => _terrainAPIService.HeldSiaSinceAsync(memberIds);

        public async Task<List<SIAProjectListModel>> GenerateSIAReportData(List<KeyValuePair<string, string>> selectedMembers, string section, string selectedUnitId,
            Action<string>? progress = null, CancellationToken cancellationToken = default)
        {
            using var ownScope = CacheScope.Current == null ? CacheScope.Begin(CacheMode.Fresh) : null;
            var approvals = new List<ApprovalsListModel>(); // await _approvalsService.GetApprovalListItems(selectedUnitId);
            TextInfo myTI = new CultureInfo("en-UA", false).TextInfo;
            var unitSiaProjects = new List<SIAProjectListModel>();
            var read = 0;
            foreach (var member in selectedMembers)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Invoke($"{++read} of {selectedMembers.Count} Scouts");
                try
                {
                    var siaResultModel = await _terrainAPIService.GetSIAResultsForMember(member.Key);
                    var memberSiaProjects = siaResultModel.results.Where(r => r.section == section)
                        .Select(r => new SIAProjectListModel
                        {
                            memberName = member.Value,
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
                    if (memberSiaProjects != null && memberSiaProjects.Count > 0)
                        unitSiaProjects = unitSiaProjects.Concat(memberSiaProjects).ToList();
                }
                catch (Exception ex)
                {
                    unitSiaProjects.Add(new SIAProjectListModel
                    {
                        memberName = $"{member.Value}",
                        area = "Error",
                        projectName = "Error",
                        status = "Error",
                        statusUpdated = DateTime.Now
                    });
                }
            }

            return unitSiaProjects;
        }

    }
}
