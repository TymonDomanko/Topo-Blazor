using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Newtonsoft.Json;
using Topo.Model.Members;
using Topo.Model.OAS;
using Topo.Model.ReportGeneration;
using Topo.Model.Cache;
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
        // Loading and reading again through the data cache
        public PageLoad _page = new PageLoad();
        public bool Loading => _page.Loading || _readingBranches;
        private bool _readingBranches;
        // The unit has no youth, so there is nothing to read
        public bool NoYouth;
        public string Progress => _readingBranches ? "the branches of each skill" : _page.Progress;
        // Laid out again whenever an option changes, from what was read for the unit
        public OasSummaryReport? Report;
        private List<OasSummaryMember>? _members;
        private List<Result>? _records;
        // The branches of each skill, as Terrain lists them; read the first time they are wanted
        private List<OasBranch>? _branches;

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
            _members = null;
            _records = null;
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
            await SplitChanged();
        }

        // Reads everything on the page again, leaving what is shown in place until the new table is in
        internal async Task RefreshClick()
        {
            model.ErrorMessage = "";
            await _page.RefreshAsync(Read(model.UnitId), Show);
            ShowFailure();
        }

        private Func<Action<string>, CancellationToken, Task<(List<MemberListModel> Youth, List<Result> Records)>> Read(string unitId) =>
            async (progress, cancellationToken) =>
            {
                progress("the unit's Scouts");
                // Youth only: the Unit Leader, Patrol Leaders and Assistant Patrol Leaders are youth, and stay in the order the unit is listed
                var members = (await _membersService.GetMembersAsync(unitId)).Where(m => m.isAdultLeader == 0).ToList();
                cancellationToken.ThrowIfCancellationRequested();
                if (members.Count == 0)
                {
                    return (members, new List<Result>());
                }
                progress("their Outdoor Adventure Skills");
                var records = await _oasService.GetUnitOasRecords(unitId);
                return (members, records.results.ToList());
            };

        private void Show((List<MemberListModel> Youth, List<Result> Records) read)
        {
            NoYouth = read.Youth.Count == 0;
            _members = read.Youth.Select(m => new OasSummaryMember { MemberId = m.id, FirstName = m.first_name, LastName = m.last_name, Patrol = m.patrol_name }).ToList();
            _records = read.Records;
            Recalculate();
        }

        private void ShowFailure()
        {
            if (_page.Failed)
            {
                model.ErrorMessage = "Couldn't read the Outdoor Adventure Skills from Terrain.";
            }
        }

        // The table follows the options as they are ticked, without reading Terrain again
        internal void OptionChanged() => Recalculate();

        // The branches are listed in Terrain's stage tree, which is read once (the OAS Progress page shares it) the first time they are wanted
        internal async Task SplitChanged()
        {
            if (model.SplitBranches && _branches == null)
            {
                _readingBranches = true;
                StateHasChanged();
                try
                {
                    _branches = (await _oasService.GetOASStagesList())
                        .Select(stage => (stream: OasSummaryLayout.Streams.FirstOrDefault(s => string.Equals(s.Heading, stage.Stream, StringComparison.OrdinalIgnoreCase)), stage))
                        .Where(x => x.stream != null)
                        .GroupBy(x => (x.stream!.Key, branch: x.stage.Branch))
                        .Select(g => new OasBranch { Stream = g.Key.Key, Key = g.Key.branch, Title = g.First().stage.StageTitle })
                        .ToList();
                }
                catch (Exception)
                {
                    // the branches the Scouts have records in are still shown, named from Terrain's own names for them
                    _branches = new List<OasBranch>();
                }
                finally
                {
                    _readingBranches = false;
                }
            }
            Recalculate();
        }

        private void Recalculate()
        {
            // a table split into branches waits for the branches to be listed
            if (_members == null || _records == null || (model.SplitBranches && _branches == null))
            {
                return;
            }
            var columns = model.SplitBranches ? OasSummaryLayout.BranchColumns(_branches ?? new List<OasBranch>(), _records) : OasSummaryLayout.SkillColumns();
            Report = OasSummaryBuilder.Build(_members, _records, _storageService.Section, model.BreakByPatrol, model.UseFullNames, columns, model.SplitBranches);
            Report.UnitName = _storageService.UnitName ?? "";
            Report.Section = _storageService.Section;
            Report.GeneratedAt = DateTime.Now;
        }

        internal async Task PdfClick() => await Download(OutputType.PDF);

        internal async Task XlsxClick() => await Download(OutputType.Excel);

        // The report is made from what is on the page, so it is the same as the preview
        private async Task Download(OutputType outputType)
        {
            if (Report == null || _page.Updating)
            {
                return;
            }
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

        public void Dispose() => _page.Dispose();
    }
}
