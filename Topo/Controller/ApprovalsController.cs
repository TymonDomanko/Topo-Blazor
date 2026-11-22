using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Newtonsoft.Json;
using Syncfusion.Blazor.Calendars;
using Syncfusion.Blazor.Grids;
using System.Net.NetworkInformation;
using System.Reflection;
using Topo.Model.AdditionalAwards;
using Topo.Model.Approvals;
using Topo.Model.ReportGeneration;
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
        public IAutoAwardService _autoAwards { get; set; }

        [Inject]
        public NavigationManager NavigationManager { get; set; }

        public ApprovalsPageViewModel model = new ApprovalsPageViewModel();

        public IEditorSettings DateEditParams = new DateEditCellParams
        {
            Params = new DatePickerModel() { ShowClearButton = true }
        };

        public SfGrid<ApprovalsListModel> GridInstance { get; set; }
        protected override void OnInitialized()
        {
            if (!_storageService.IsAuthenticated)
                NavigationManager.NavigateTo("index");

            // The automatic Camper and Walkabout check keeps its records up to date; this page shows them and its status
            _autoAwards.Changed += OnAutoChanged;
            model.GroupName = _storageService.GroupNameDisplay;
            model.Units = _storageService.Units;
            model.ApprovalSearchFromDate = DateTime.Now.AddMonths(-2);
            model.ApprovalSearchToDate = DateTime.Now;
            if (!string.IsNullOrEmpty(_storageService.UnitId))
            {
                model.UnitId = _storageService.UnitId;
                model.UnitName = _storageService.UnitName;
            }
        }

        private void OnAutoChanged() => InvokeAsync(StateHasChanged);

        public void Dispose() => _autoAwards.Changed -= OnAutoChanged;

        public bool AutoEnabled => _autoAwards.Settings.Enabled;

        internal async Task AutoToggle(ChangeEventArgs e) => await _autoAwards.SetEnabledAsync(e.Value is true);

        // The Camper and Walkabout Awards the Topo records hold for the unit, as rows like the others. If the records cannot be read the
        // list is still shown without them.
        private async Task<List<ApprovalsListModel>> AdditionalAwardRowsAsync(string unitId)
        {
            try
            {
                return AwardCheckRows.ToApprovalRows(await _additionalAwards.StoredAsync(unitId));
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
            await RefreshApprovalsClick();
        }

        internal async Task RefreshApprovalsClick()
        {
            if (!string.IsNullOrEmpty(model.UnitName))
            {
                model.Approvals = await _approvalsService.GetApprovalListItems(_storageService.UnitId);
                model.Approvals = (model.Approvals ?? new List<ApprovalsListModel>()).Concat(await AdditionalAwardRowsAsync(_storageService.UnitId)).ToList();
                // Worked out over everything, before the filters, so a stage is not called the highest just because a higher one is out of the date range
                ApprovalsToAward.ApplyOas(model.Approvals);
                model.Approvals = ApprovalsFilter.Apply(model.Approvals, model.ApprovalSearchFromDate, model.ApprovalSearchToDate, model.ToBePresented, model.IsPresented, model.ShowRejected);
            }
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
