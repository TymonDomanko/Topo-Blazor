using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Newtonsoft.Json;
using Topo.Model.Milestone;
using Topo.Model.ReadyToSubmit;
using Topo.Model.ReportGeneration;
using Topo.Model.Cache;
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
        // Loading and reading again through the data cache
        public PageLoad _page = new PageLoad();
        public bool Loading => _page.Loading;
        // The unit has no youth, so there is nothing to read
        public bool NoYouth;
        public string Progress => _page.Progress;
        // Worked out again whenever an option changes, from what was read for the unit
        public MilestoneProgressReport? Report;
        private List<MilestoneProgressMember>? _members;
        private MilestoneProgressData? _data;
        protected override async Task OnInitializedAsync()
        {
            if (!_storageService.IsAuthenticated)
            {
                NavigationManager.NavigateTo("index");
                return;
            }

            _page.Changed = () => InvokeAsync(StateHasChanged);
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
            if (string.IsNullOrEmpty(unitId))
            {
                _page.Cancel();
                return;
            }

            _storageService.UnitId = unitId;
            model.UnitName = _storageService.UnitName;
            // what the data cache holds shows at once, and is read again behind the page when it is more than a day old
            await _page.LoadAsync(Read(unitId), Show);
            ShowFailure();
        }

        // Reads everything on the page again, leaving what is shown in place until the new pages are in
        internal async Task RefreshClick()
        {
            model.ErrorMessage = "";
            await _page.RefreshAsync(Read(model.UnitId), Show);
            ShowFailure();
        }

        private Func<Action<string>, CancellationToken, Task<(List<MilestoneProgressMember> Youth, MilestoneProgressData? Data)>> Read(string unitId) =>
            async (progress, cancellationToken) =>
            {
                progress("the unit's Scouts");
                // The Unit Leader, Patrol Leaders and Assistant Patrol Leaders are youth; the unit is listed in the order the reports use
                var youth = ReadyToSubmitBuilder.InPatrolOrder((await _membersService.GetMembersAsync(unitId)).Where(m => m.isAdultLeader == 0).ToList())
                    .Select(m => new MilestoneProgressMember { MemberId = m.id, FirstName = m.first_name, LastName = m.last_name, Patrol = m.patrol_name }).ToList();
                cancellationToken.ThrowIfCancellationRequested();
                if (youth.Count == 0)
                {
                    return (youth, null);
                }
                return (youth, await _milestoneProgressService.LoadAsync(unitId, _storageService.Section, progress, cancellationToken));
            };

        private void Show((List<MilestoneProgressMember> Youth, MilestoneProgressData? Data) read)
        {
            NoYouth = read.Youth.Count == 0;
            _members = read.Youth;
            _data = read.Data;
            Recalculate();
        }

        private void ShowFailure()
        {
            if (_page.Failed)
            {
                model.ErrorMessage = "Couldn't read the milestones from Terrain.";
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
            if (Report == null || Report.Pages.Count == 0 || _page.Updating)
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

        public void Dispose() => _page.Dispose();
    }
}
