using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Newtonsoft.Json;
using Topo.Model.Login;
using Topo.Model.Members;
using Topo.Model.ReportGeneration;
using Topo.Services;

namespace Topo.Controller
{

    public class MembersController : ComponentBase
    {
        [Inject]
        public IMembersService _membersService { get; set; }

        [Inject]
        public StorageService _storageService { get; set; }

        [Inject]
        IJSRuntime JS { get; set; }

        [Inject]
        public IReportService _reportService { get; set; }

        [Inject]
        public NavigationManager NavigationManager { get; set; }

        public MembersPageViewModel model = new MembersPageViewModel();
        // Every member of the unit, the adult leaders too, for the patrol list that can include them
        private List<MemberListModel> _allMembers = new List<MemberListModel>();

        public string Section => _storageService.Section;

        protected override async Task OnInitializedAsync()
        {
            if (!_storageService.IsAuthenticated)
                NavigationManager.NavigateTo("index");

            model.Units = _storageService.Units;
            model.GroupName = _storageService.GroupNameDisplay;
            if (!string.IsNullOrEmpty(_storageService.UnitId))
            {
                await UnitChange(_storageService.UnitId);
            }
        }

        internal async Task UnitChange(ChangeEventArgs e)
        {
            var unitId = e.Value?.ToString() ?? "";
            await UnitChange(unitId);
        }

        internal async Task UnitChange(string unitId)
        {
            if (string.IsNullOrEmpty(unitId))
            {
                model.UnitId = unitId;
                _storageService.UnitId = model.UnitId;
                model.UnitName = _storageService.UnitName;
                model.Members = new List<MemberListModel>();
                _allMembers = new List<MemberListModel>();
                return;
            }
            model.UnitId = unitId;
            _storageService.UnitId = model.UnitId;
            _allMembers = await _membersService.GetMembersAsync(model.UnitId);
            model.Members = MemberReportLayouts.Sorted(_allMembers.Where(m => m.isAdultLeader == 0), MemberListSort.Alphabetical);
            model.UnitName = _storageService.UnitName;
        }

        internal void ViewChange(MemberReportView view) => model.View = view;

        // What each report is made from. The preview and the downloaded report are made from the same members, so they match.
        private List<MemberListModel> PatrolListMembers => MemberReportLayouts.PatrolListMembers(_allMembers, model.IncludeLeaders, model.UseFullNames);
        private List<MemberListModel> MemberListMembers => MemberReportLayouts.MemberListMembers(_allMembers, model.Sort, model.UseFullNames);
        private List<MemberListModel> PatrolSheetMembers => MemberReportLayouts.PatrolSheetMembers(_allMembers, model.UseFullNames);

        public PatrolListLayout PatrolList => MemberReportLayouts.PatrolList(PatrolListMembers, model.IncludeLeaders);
        public List<MemberListRow> MemberList => MemberReportLayouts.MemberList(MemberListMembers, Section);
        public List<PatrolGroup> PatrolSheets => MemberReportLayouts.Patrols(PatrolSheetMembers);

        // The pdf or spreadsheet of whichever report is showing
        internal async Task ReportPdfClick() => await Download(OutputType.PDF, "pdf", "application/pdf");

        internal async Task ReportXlsxClick() => await Download(OutputType.Excel, "xlsx", "application/vnd.ms-excel");

        private async Task Download(OutputType outputType, string extension, string contentType)
        {
            var groupName = _storageService.GroupName ?? "Group Name";
            var unitName = _storageService.UnitName ?? "Unit Name";
            var section = _storageService.Section;

            byte[] report;
            string name;
            switch (model.View)
            {
                case MemberReportView.PatrolList:
                    name = "Patrol_List";
                    report = await _reportService.GetPatrolListReport(groupName, section, unitName, outputType, JsonConvert.SerializeObject(PatrolListMembers), model.IncludeLeaders);
                    break;
                case MemberReportView.PatrolSheets:
                    name = "Patrol_Sheets";
                    report = await _reportService.GetPatrolSheetsReport(groupName, section, unitName, outputType, JsonConvert.SerializeObject(PatrolSheetMembers));
                    break;
                default:
                    name = "Members";
                    report = await _reportService.GetMemberListReport(groupName, section, unitName, outputType, JsonConvert.SerializeObject(MemberListMembers));
                    break;
            }
            var fileName = $"{name}_{model.UnitName.Replace(' ', '_')}.{extension}";

            // Send the data to JS to actually download the file
            await JS.InvokeVoidAsync("BlazorDownloadFile", fileName, contentType, report);
        }

        internal async Task MemberRefreshClick()
        {
            _membersService.ClearMemberCache(_storageService.UnitId);
            await UnitChange(_storageService.UnitId);
        }
    }
}
