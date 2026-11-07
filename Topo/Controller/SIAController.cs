using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Topo.Model.SIA;
using Topo.Services;
using Topo.Model.ReportGeneration;
using Newtonsoft.Json;
using Topo.Model.Members;

namespace Topo.Controller
{
    public class SIAController : ComponentBase
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
        public ISIAService _SIAService { get; set; }

        public SIAPageViewModel model = new SIAPageViewModel();
        public bool Loading;
        public string Progress = "";
        public string ErrorMessage = "";
        // Laid out again whenever an option changes, from what was read for the unit
        public List<SiaProjectsSheet>? Sheets;
        // The ids of the Scouts ticked beside their names; with none ticked the report is for everyone
        public HashSet<string> Ticked => model.Members.Where(m => m.selected).Select(m => m.id).ToHashSet();
        private List<SIAProjectListModel>? _data;

        protected override async Task OnInitializedAsync()
        {
            if (!_storageService.IsAuthenticated)
                NavigationManager.NavigateTo("index");

            model.GroupName = _storageService.GroupNameDisplay;
            model.Units = _storageService.Units;
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
            Sheets = null;
            _data = null;
            ErrorMessage = "";
            if (string.IsNullOrEmpty(unitId))
            {
                model.UnitId = unitId;
                _storageService.UnitId = model.UnitId;
                model.UnitName = _storageService.UnitName;
                model.Members = new List<MemberListModel>();
                return;
            }
            model.UnitId = unitId;
            _storageService.UnitId = model.UnitId;
            var allMembers = await _membersService.GetMembersAsync(model.UnitId);
            model.Members = allMembers.Where(m => m.isAdultLeader == 0).OrderBy(m => m.first_name).ThenBy(m => m.last_name).ToList();
            model.UnitName = _storageService.UnitName;
            // the members are kept for the unit, so a tick left from another visit is cleared
            foreach (var member in model.Members)
                member.selected = false;
            await Load();
        }

        // Reads every Scout's projects once; the options then change what is shown without reading Terrain again
        private async Task Load()
        {
            if (model.Members.Count == 0)
                return;

            Loading = true;
            try
            {
                _data = await _SIAService.GenerateSIAReportData(model.Members, _storageService.Section, model.UnitId, step => { Progress = step; InvokeAsync(StateHasChanged); });
                Recalculate();
            }
            catch (Exception)
            {
                ErrorMessage = "Couldn't read the projects from Terrain.";
            }
            finally
            {
                Loading = false;
                Progress = "";
            }
        }

        internal void OptionChanged() => Recalculate();

        // Ticking a Scout beside their name puts them in the report; it is for everyone while none are ticked
        internal void Toggle((string Id, bool Ticked) change)
        {
            var member = model.Members.FirstOrDefault(m => m.id == change.Id);
            if (member != null)
                member.selected = change.Ticked;
        }

        internal void ClearTicks()
        {
            foreach (var member in model.Members)
                member.selected = false;
        }

        // The Scouts ticked, or everyone when none are, named as the options ask
        private List<SIAProjectListModel>? ReportData() =>
            _data == null ? null : SiaProjectsLayout.ForMembers(_data, ReportScope.Names(model.Members, model.UseFullNames));

        // The page shows everyone, so a Scout can be ticked; the report is made from those ticked
        private void Recalculate() =>
            Sheets = _data == null ? null : SiaProjectsLayout.Build(SiaProjectsLayout.ForMembers(_data, ReportScope.AllNames(model.Members, model.UseFullNames)), model.BreakByPatrol);

        internal async Task SIAReportPdfClick() => await Download(OutputType.PDF, "pdf", "application/pdf");

        internal async Task SIAReportXlsxClick() => await Download(OutputType.Excel, "xlsx", "application/vnd.ms-excel");

        // The report is made from what is on the page, so it is the same as the preview
        private async Task Download(OutputType outputType, string extension, string contentType)
        {
            var reportData = ReportData();
            if (reportData == null || reportData.Count == 0)
                return;

            try
            {
                var groupName = _storageService.GroupName ?? "";
                var unitName = _storageService.UnitName ?? "";
                var report = await _reportService.GetSIAReport(groupName, _storageService.Section, unitName, outputType, JsonConvert.SerializeObject(reportData), model.BreakByPatrol);
                var fileName = $"SIA_Projects_{model.UnitName.Replace(' ', '_')}.{extension}";

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
