using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Newtonsoft.Json;
using Topo.Model.Milestone;
using Topo.Model.OAS;
using Topo.Model.ReportGeneration;
using Topo.Model.Cache;
using Topo.Services;

namespace Topo.Controller
{
    public class MilestoneController : ComponentBase, IDisposable
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
        public IMilestoneService _milestoneService { get; set; }

        public MilestonePageViewModel model = new MilestonePageViewModel();
        // Loading and reading again through the data cache
        public PageLoad _page = new PageLoad();
        public bool Loading => _page.Loading;
        public string Progress => _page.Progress;
        public string ErrorMessage = "";
        // Laid out again whenever an option changes, from what was read for the unit
        public List<MilestonePatrolGroup>? Groups;
        private List<MilestoneSummaryListModel>? _summaries;

        protected override async Task OnInitializedAsync()
        {
            if (!_storageService.IsAuthenticated)
            {
                NavigationManager.NavigateTo("index");
                return;
            }

            _page.Changed = () => InvokeAsync(StateHasChanged);
            model.UnitId = _storageService.UnitId;
            model.UnitName = _storageService.UnitName;
            model.GroupName = _storageService.GroupNameDisplay;
            model.Units = _storageService.Units;
            model.UseFullNames = _storageService.UseFullNames;
            if (!string.IsNullOrEmpty(model.UnitId))
                await Load();
        }

        internal async Task UnitChange(ChangeEventArgs e)
        {
            var unitId = e.Value?.ToString() ?? "";
            model.UnitId = unitId;
            _storageService.UnitId = model.UnitId;
            model.UnitName = _storageService.UnitName;
            await Load();
        }

        // Reads the unit's milestones; the preview then follows the options. What the data cache holds shows at once, and is read again behind
        // the page when it is more than a day old.
        private async Task Load()
        {
            Groups = null;
            _summaries = null;
            ErrorMessage = "";
            if (string.IsNullOrEmpty(model.UnitId))
            {
                _page.Cancel();
                return;
            }

            _storageService.UseFullNames = model.UseFullNames;
            await _page.LoadAsync(ReadSummaries(model.UnitId), Show);
            ShowFailure();
        }

        // Reads everything on the page again, leaving what is shown in place until the new figures are in
        internal async Task RefreshClick()
        {
            ErrorMessage = "";
            await _page.RefreshAsync(ReadSummaries(model.UnitId), Show);
            ShowFailure();
        }

        private Func<Action<string>, CancellationToken, Task<List<MilestoneSummaryListModel>>> ReadSummaries(string unitId) =>
            (progress, token) => _milestoneService.GetMilestoneSummaries(unitId, progress);

        private void Show(List<MilestoneSummaryListModel> summaries)
        {
            _summaries = summaries;
            Recalculate();
        }

        private void ShowFailure()
        {
            if (_page.Failed)
            {
                ErrorMessage = "Couldn't read the milestones from Terrain.";
            }
        }

        public void Dispose() => _page.Dispose();

        internal void BreakByPatrolChanged() => Recalculate();

        internal async Task UseFullNamesChanged()
        {
            _storageService.UseFullNames = model.UseFullNames;
            if (_summaries != null)
            {
                // the names come from the members already held
                using (CacheScope.Begin(CacheMode.Held))
                {
                    await _milestoneService.RenameMembers(model.UnitId, _summaries);
                }
            }
            Recalculate();
        }

        private void Recalculate() => Groups = _summaries == null ? null : MilestoneLayout.Build(_summaries, model.BreakByPatrol);

        internal async Task MilestoneReportPdfClick() => await Download(OutputType.PDF, "pdf", "application/pdf");

        internal async Task MilestoneReportXlsxClick() => await Download(OutputType.Excel, "xlsx", "application/vnd.ms-excel");

        // The report is made from what is on the page, so it is the same as the preview
        private async Task Download(OutputType outputType, string extension, string contentType)
        {
            if (_summaries == null || _page.Updating)
            {
                return;
            }

            try
            {
                var groupName = _storageService.GroupName ?? "";
                var unitName = _storageService.UnitName ?? "";
                var report = await _reportService.GetMilestoneReport(groupName, _storageService.Section, unitName, outputType, JsonConvert.SerializeObject(_summaries), model.BreakByPatrol);
                var fileName = $"Milestone_Report_{model.UnitName.Replace(' ', '_')}.{extension}";

                // Send the data to JS to actually download the file
                await JS.InvokeVoidAsync("BlazorDownloadFile", fileName, contentType, report);
            }
            catch (Exception)
            {
                ErrorMessage = "Couldn't build the report.";
            }
        }
    }
}
