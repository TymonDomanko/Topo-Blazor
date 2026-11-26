using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Newtonsoft.Json;
using Topo.Model.Milestone;
using Topo.Model.ReadyToSubmit;
using Topo.Model.ReportGeneration;
using Topo.Services;

namespace Topo.Controller
{
    // The Milestone Progress page: a page for each Scout of a milestone, with their counts, the events that counted and what they wrote on the review
    public class MilestoneProgressController : ComponentBase, IDisposable
    {
        [Inject]
        public StorageService _storageService { get; set; }

        [Inject]
        IJSRuntime JS { get; set; }

        [Inject]
        public IReportService _reportService { get; set; }

        [Inject]
        public NavigationManager NavigationManager { get; set; }

        [Inject]
        public IMembersService _membersService { get; set; }

        [Inject]
        public IMilestoneProgressService _milestoneProgressService { get; set; }

        public MilestoneProgressPageViewModel model = new MilestoneProgressPageViewModel();
        public bool Loading;
        // The unit has no youth, so there is nothing to read
        public bool NoYouth;
        public string Progress = "";
        // Worked out again whenever an option changes, from what was read for the unit
        public MilestoneProgressReport? Report;
        private List<MilestoneProgressMember>? _members;
        private MilestoneProgressData? _data;
        // The unit can be changed while one is still being read, so a read that has been overtaken is dropped
        private int _loadId;
        private CancellationTokenSource? _loading;

        protected override async Task OnInitializedAsync()
        {
            if (!_storageService.IsAuthenticated)
            {
                NavigationManager.NavigateTo("index");
                return;
            }

            model.GroupName = _storageService.GroupNameDisplay;
            model.Units = _storageService.Units;
            if (!string.IsNullOrEmpty(_storageService.UnitId))
            {
                await UnitChange(_storageService.UnitId);
            }
        }

        internal async Task UnitChange(ChangeEventArgs e) => await UnitChange(e.Value?.ToString() ?? "");

        internal async Task UnitChange(string unitId)
        {
            model.UnitId = unitId;
            model.ErrorMessage = "";
            Report = null;
            _data = null;
            _members = null;
            NoYouth = false;
            var loadId = ++_loadId;
            _loading?.Cancel();
            _loading = new CancellationTokenSource();
            var token = _loading.Token;
            if (string.IsNullOrEmpty(unitId))
            {
                Loading = false;
                Progress = "";
                return;
            }

            _storageService.UnitId = unitId;
            model.UnitName = _storageService.UnitName;
            Loading = true;
            Progress = "the unit's Scouts";
            try
            {
                // The Unit Leader, Patrol Leaders and Assistant Patrol Leaders are youth; the unit is listed in the order the reports use
                var youth = ReadyToSubmitBuilder.InPatrolOrder((await _membersService.GetMembersAsync(unitId)).Where(m => m.isAdultLeader == 0).ToList());
                if (loadId != _loadId)
                {
                    return;
                }
                if (youth.Count == 0)
                {
                    NoYouth = true;
                    return;
                }
                var data = await _milestoneProgressService.LoadAsync(unitId, _storageService.Section,
                    step => { if (loadId == _loadId) { Progress = step; InvokeAsync(StateHasChanged); } }, token);
                if (loadId != _loadId)
                {
                    return;
                }
                _members = youth.Select(m => new MilestoneProgressMember { MemberId = m.id, FirstName = m.first_name, LastName = m.last_name, Patrol = m.patrol_name }).ToList();
                _data = data;
                Recalculate();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception)
            {
                if (loadId == _loadId)
                {
                    model.ErrorMessage = "Couldn't read the milestones from Terrain.";
                }
            }
            finally
            {
                if (loadId == _loadId)
                {
                    Loading = false;
                    Progress = "";
                }
            }
        }

        private string Which => Report == null || Report.Stage == MilestoneProgressBuilder.AllMilestones ? "any milestone" : $"Milestone {Report.Stage}";

        // How many pages are shown out of how many there could be, or why there are none
        public string Summary
        {
            get
            {
                if (Report == null)
                {
                    return "";
                }
                var all = Report.Stage == MilestoneProgressBuilder.AllMilestones;
                if (Report.Pages.Count == 0)
                {
                    return Report.OnlyReady
                        ? $"No Scouts are ready for {Which} yet. Untick the box above to see them all ({Report.ScoutsWithTheMilestone})."
                        : $"No Scouts have a record for {Which}.";
                }
                return all
                    ? $"{Report.Pages.Count} of the {Report.ScoutsWithTheMilestone} milestones the Scouts have, across Milestones 1, 2 and 3."
                    : $"{Report.Pages.Count} of {Report.ScoutsWithTheMilestone} {(Report.ScoutsWithTheMilestone == 1 ? "Scout" : "Scouts")} with Milestone {Report.Stage}.";
            }
        }

        // The pages follow the options as they are changed, without reading Terrain again
        internal void OptionChanged() => Recalculate();

        internal void StageChanged(ChangeEventArgs e)
        {
            model.Stage = int.TryParse(e.Value?.ToString(), out var stage) ? stage : MilestoneProgressBuilder.AllMilestones;
            Recalculate();
        }

        private void Recalculate()
        {
            if (_members == null || _data == null)
            {
                return;
            }
            Report = MilestoneProgressBuilder.Build(new MilestoneProgressInput
            {
                UnitName = _storageService.UnitName ?? "",
                Section = _storageService.Section,
                Stage = model.Stage,
                OnlyReady = model.OnlyReady,
                UseFullNames = model.UseFullNames,
                Members = _members,
                Records = _data.Records,
                Forms = _data.Forms,
                GeneratedAt = DateTime.Now
            });
        }

        internal async Task PdfClick() => await Download(OutputType.PDF, "pdf", "application/pdf");

        internal async Task XlsxClick() => await Download(OutputType.Excel, "xlsx", "application/vnd.ms-excel");

        // The report is made from what is on the page, so it is the same as the preview
        private async Task Download(OutputType outputType, string extension, string contentType)
        {
            if (Report == null || Report.Pages.Count == 0)
            {
                return;
            }
            try
            {
                var groupName = _storageService.GroupName ?? "";
                var bytes = await _reportService.GetMilestoneProgressReport(groupName, Report.Section, Report.UnitName, outputType, JsonConvert.SerializeObject(Report));
                var fileName = $"Milestone_{(Report.Stage == MilestoneProgressBuilder.AllMilestones ? "All" : Report.Stage.ToString())}_Progress_{Report.UnitName.Replace(' ', '_')}.{extension}";
                await JS.InvokeVoidAsync("BlazorDownloadFile", fileName, contentType, bytes);
            }
            catch (Exception)
            {
                model.ErrorMessage = "Couldn't build the report.";
            }
        }

        public void Dispose()
        {
            _loading?.Cancel();
            _loading?.Dispose();
        }
    }
}
