using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Newtonsoft.Json;
using Syncfusion.Blazor.Calendars;
using Syncfusion.Blazor.Grids;
using System.Net.NetworkInformation;
using System.Reflection;
using Topo.Model.AdditionalAwards;
using Topo.Model.Approvals;
using Topo.Model.Common;
using Topo.Model.ReportGeneration;
using Topo.Model.Cache;
using Topo.Services;


namespace Topo.Controller
{
    public class ApprovalsController : ComponentBase, IDisposable
    {
        [Inject]
        public StorageService _storageService { get; set; }

        [Inject]
        IJSRuntime JS { get; set; }

        [Inject]
        public IReportService _reportService { get; set; }

        [Inject]
        public IApprovalsService _approvalsService { get; set; }

        [Inject]
        public IAdditionalAwardCheckService _additionalAwards { get; set; }

        [Inject]
        public NavigationManager NavigationManager { get; set; }

        public ApprovalsPageViewModel model = new ApprovalsPageViewModel();

        public IEditorSettings DateEditParams = new DateEditCellParams
        {
            Params = new DatePickerModel() { ShowClearButton = true }
        };

        public SfGrid<ApprovalsListModel> GridInstance { get; set; }

        // Loading and reading again through the data cache
        public PageLoad _page = new PageLoad();
        public bool Loading => _page.Loading;
        public string Progress => _page.Progress;
        public string ErrorMessage = "";
        // Everything Terrain has for the unit; the dates and tick boxes then only narrow it down, without reading Terrain again
        private List<ApprovalsListModel> _all = new List<ApprovalsListModel>();

        public void Dispose() => _page.Dispose();

        protected override async Task OnInitializedAsync()
        {
            if (!_storageService.IsAuthenticated)
                NavigationManager.NavigateTo("index");

            _page.Changed = () => InvokeAsync(StateHasChanged);
            model.GroupName = _storageService.GroupNameDisplay;
            model.Units = _storageService.Units;
            model.ApprovalSearchFromDate = DateTime.Today;
            model.ApprovalSearchToDate = DateTime.Now;
            if (!string.IsNullOrEmpty(_storageService.UnitId))
            {
                model.UnitId = _storageService.UnitId;
                model.UnitName = _storageService.UnitName;
                await Load();
            }
        }

        // The Camper and Walkabout Awards to hand over for the unit (earned by the logbook, not in Terrain yet), as rows like the others. If they
        // cannot be worked out the list is still shown without them.
        private async Task<List<ApprovalsListModel>> AdditionalAwardRowsAsync(string unitId)
        {
            try
            {
                // Those to hand over, then those Terrain already holds (listed as presented)
                return AwardCheckRows.ToApprovalRows(await _additionalAwards.PendingAsync(unitId))
                    .Concat(AwardCheckRows.ToRecordedRows(await _additionalAwards.RecordedAsync(unitId))).ToList();
            }
            catch (Exception)
            {
                return new List<ApprovalsListModel>();
            }
        }

        internal async Task UnitChange(ChangeEventArgs e)
        {
            var unitId = e.Value?.ToString() ?? "";
            model.UnitId = unitId;
            _storageService.UnitId = model.UnitId;
            model.UnitName = _storageService.UnitName;
            _all = new List<ApprovalsListModel>();
            model.Approvals = new List<ApprovalsListModel>();
            await Load();
        }

        // Reads the unit's approvals. What the data cache holds shows at once, and is read again behind the page when it is more than a day old.
        private async Task Load()
        {
            ErrorMessage = "";
            if (string.IsNullOrEmpty(model.UnitId))
            {
                _page.Cancel();
                return;
            }
            await _page.LoadAsync(ReadRows(model.UnitId), Show);
            ShowFailure();
        }

        // Reads everything on the page again, leaving what is shown in place until the new list is in
        internal async Task RefreshClick()
        {
            ErrorMessage = "";
            await _page.RefreshAsync(ReadRows(model.UnitId), Show);
            ShowFailure();
        }

        private Func<Action<string>, CancellationToken, Task<List<ApprovalsListModel>>> ReadRows(string unitId) =>
            async (progress, cancellationToken) =>
            {
                var approvals = await _approvalsService.GetApprovalListItems(unitId, progress);
                cancellationToken.ThrowIfCancellationRequested();
                return (approvals ?? new List<ApprovalsListModel>()).Concat(await AdditionalAwardRowsAsync(unitId)).ToList();
            };

        private void Show(List<ApprovalsListModel> rows)
        {
            _all = rows;
            // Worked out over everything, before the filters, so a stage is not called the highest just because a higher one is out of the date range
            ApprovalsToAward.ApplyOas(_all);
            ApplyFilters();
        }

        private void ShowFailure()
        {
            if (_page.Failed)
            {
                ErrorMessage = "Couldn't read the approvals from Terrain.";
            }
        }

        // The dates and tick boxes narrow down what was read, so the list follows them as they change
        internal void ApplyFilters()
        {
            model.DateErrorMessage = "";
            if (model.ApprovalSearchToDate < model.ApprovalSearchFromDate)
            {
                model.DateErrorMessage = "The search to date must be after the search from date.";
                model.Approvals = new List<ApprovalsListModel>();
                return;
            }

            model.Approvals = ApprovalsFilter.Apply(_all, model.ApprovalSearchFromDate, model.ApprovalSearchToDate, model.ToBePresented, model.IsPresented, model.ShowRejected);
        }

        public async void ActionBeginHandler(ActionEventArgs<ApprovalsListModel> Args)
        {
            if (Args.RequestType == Syncfusion.Blazor.Grids.Action.Save)
            {
                var data = (ApprovalsListModel)Args.Data;
                await _approvalsService.UpdateApproval(model.UnitId, data);
            }
        }

        internal async Task ApprovalsReportPdfClick()
        {
            byte[] report = await ApprovalsReport(OutputType.PDF);
            var fileName = $"Approvals_{model.UnitName.Replace(' ', '_')}.pdf";

            // Send the data to JS to actually download the file
            await JS.InvokeVoidAsync("BlazorDownloadFile", fileName, "application/pdf", report);
        }

        internal async Task ApprovalsReportXlsxClick()
        {
            byte[] report = await ApprovalsReport(OutputType.Excel);
            var fileName = $"Approvals_{model.UnitName.Replace(' ', '_')}.xlsx";

            // Send the data to JS to actually download the file
            await JS.InvokeVoidAsync("BlazorDownloadFile", fileName, "application/vnd.ms-excel", report);
        }

        internal async Task<byte[]> ApprovalsReport(OutputType outputType = OutputType.PDF)
        {
            var filterSetings = GridInstance.FilterSettings;
            string[] nameFilterColumnValues = new string[0];
            string nameFilterOperator = "";
            string[] statusFilterColumnValues = new string[0];
            string statusFilterOperator = "";
            string groupCols = "";
            if (filterSetings != null && filterSetings.Columns != null)
            {
                var nameFilterSettings = filterSetings.Columns.Where(x => x.Field == "member_display_name");
                if (nameFilterSettings != null)
                {
                    nameFilterColumnValues = nameFilterSettings.Select(x => x.Value.ToString() ?? "").ToArray();
                    nameFilterOperator = nameFilterSettings.FirstOrDefault()?.Operator.ToString() ?? "";
                }
                var statusFilterSettings = filterSetings.Columns.Where(x => x.Field == "submission_status");
                if (statusFilterSettings != null)
                {
                    statusFilterColumnValues = statusFilterSettings.Select(x => x.Value.ToString() ?? "").ToArray();
                    statusFilterOperator = statusFilterSettings.FirstOrDefault()?.Operator.ToString() ?? "";
                }
            }
            var groupSettings = GridInstance.GroupSettings;
            if (groupSettings != null && groupSettings.Columns != null)
            {
                groupCols = string.Join(",", groupSettings.Columns);
            }

            var selectedApprovals = new List<ApprovalsListModel>();
            if (nameFilterOperator.ToLower() == "equal")
                selectedApprovals = model.Approvals.Where(t2 => nameFilterColumnValues.Count(m => m == t2.member_display_name) != 0).ToList();
            else
                selectedApprovals = model.Approvals.Where(t2 => nameFilterColumnValues.Count(m => m == t2.member_display_name) == 0).ToList();
            if (statusFilterOperator.ToLower() == "equal")
                selectedApprovals = selectedApprovals.Where(t2 => statusFilterColumnValues.Count(m => m == t2.submission_status) != 0).ToList();
            else
                selectedApprovals = selectedApprovals.Where(t2 => statusFilterColumnValues.Count(m => m == t2.submission_status) == 0).ToList();

            var groupByMember = (groupCols ?? "achievement_name") == "member_display_name";
            var groupName = _storageService.GroupName ?? "";
            var unitName = _storageService.UnitName ?? "";
            var section = _storageService.Section;

            var serialisedReportData = JsonConvert.SerializeObject(selectedApprovals);

            var report = await _reportService.GetApprovalsReport(groupName, section, unitName, outputType, serialisedReportData, model.ApprovalSearchFromDate, model.ApprovalSearchToDate, groupByMember);
            return report;
        }
    }
}
