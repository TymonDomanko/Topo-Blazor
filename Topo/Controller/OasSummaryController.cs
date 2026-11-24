using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Newtonsoft.Json;
using Topo.Model.Members;
using Topo.Model.OAS;
using Topo.Model.ReportGeneration;
using Topo.Services;

namespace Topo.Controller
{
    // The OAS page: every Scout's Outdoor Adventure Skill stages in one table, with the report of it
    public class OasSummaryController : ComponentBase, IDisposable
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
        public IOASService _oasService { get; set; }

        public OasSummaryPageViewModel model = new OasSummaryPageViewModel();
        public bool Loading;
        // The unit has no youth, so there is nothing to read
        public bool NoYouth;
        public string Progress = "";
        // Laid out again whenever an option changes, from what was read for the unit
        public OasSummaryReport? Report;
        private List<OasSummaryMember>? _members;
        private List<Result>? _records;
        // The unit can be changed while one is still being read, so a read that has been overtaken is dropped
        private int _loadId;

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
                await UnitChange(_storageService.UnitId);
        }

        internal async Task UnitChange(ChangeEventArgs e) => await UnitChange(e.Value?.ToString() ?? "");

        internal async Task UnitChange(string unitId)
        {
            model.UnitId = unitId;
            model.ErrorMessage = "";
            Report = null;
            _members = null;
            _records = null;
            NoYouth = false;
            var loadId = ++_loadId;
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
                // Youth only: the Unit Leader, Patrol Leaders and Assistant Patrol Leaders are youth, and stay in the order the unit is listed
                var members = (await _membersService.GetMembersAsync(unitId)).Where(m => m.isAdultLeader == 0).ToList();
                if (loadId != _loadId)
                    return;
                if (members.Count == 0)
                {
                    NoYouth = true;
                    return;
                }
                Progress = "their Outdoor Adventure Skills";
                var records = await _oasService.GetUnitOasRecords(unitId);
                if (loadId != _loadId)
                    return;
                _members = members.Select(m => new OasSummaryMember { MemberId = m.id, FirstName = m.first_name, LastName = m.last_name, Patrol = m.patrol_name }).ToList();
                _records = records.results.ToList();
                Recalculate();
            }
            catch (Exception)
            {
                if (loadId == _loadId)
                    model.ErrorMessage = "Couldn't read the Outdoor Adventure Skills from Terrain.";
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

        // The table follows the options as they are ticked, without reading Terrain again
        internal void OptionChanged() => Recalculate();

        private void Recalculate()
        {
            if (_members == null || _records == null)
                return;
            Report = new OasSummaryReport
            {
                UnitName = _storageService.UnitName ?? "",
                Section = _storageService.Section,
                GeneratedAt = DateTime.Now,
                Sheets = OasSummaryBuilder.Build(_members, _records, _storageService.Section, model.BreakByPatrol, model.UseFullNames)
            };
        }

        internal async Task PdfClick() => await Download(OutputType.PDF);

        internal async Task XlsxClick() => await Download(OutputType.Excel);

        // The report is made from what is on the page, so it is the same as the preview
        private async Task Download(OutputType outputType)
        {
            if (Report == null)
                return;
            try
            {
                var groupName = _storageService.GroupName ?? "";
                var bytes = await _reportService.GetOasSummaryReport(groupName, Report.Section, Report.UnitName, outputType, JsonConvert.SerializeObject(Report));
                var isPdf = outputType == OutputType.PDF;
                var fileName = $"OAS_{Report.UnitName.Replace(' ', '_')}" + (isPdf ? ".pdf" : ".xlsx");
                await JS.InvokeVoidAsync("BlazorDownloadFile", fileName, isPdf ? "application/pdf" : "application/vnd.ms-excel", bytes);
            }
            catch (Exception)
            {
                model.ErrorMessage = "Couldn't build the report.";
            }
        }

        public void Dispose() => _loadId++;
    }
}
