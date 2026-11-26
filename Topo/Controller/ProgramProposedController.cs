using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Newtonsoft.Json;
using Topo.Model.Cache;
using Topo.Model.Program;
using Topo.Model.ReportGeneration;
using Topo.Services;

namespace Topo.Controller
{
    public class ProgramProposedController : ComponentBase, IDisposable
    {
        [Inject]
        public StorageService _storageService { get; set; }

        [Inject]
        public IProgramService _programService { get; set; }

        [Inject]
        public IReportService _reportService { get; set; }

        [Inject]
        IJSRuntime JS { get; set; }

        [Inject]
        public NavigationManager NavigationManager { get; set; }

        public string GroupName = "";
        // The date the run counts start from: events before it are not counted
        public ProgramIdeasRangeOption SinceRange = ProgramIdeasRangeOption.None;
        public DateTime SinceDate = DateTime.Now.AddYears(-1);
        // The date the proposals on show were counted from, which is what the report says
        private DateTime? _countedSince;
        // Loading and reading again through the data cache
        public PageLoad _page = new PageLoad();
        public bool Loading => _page.Loading;
        public bool FetchFailed;
        // No unit has been chosen (a leader of several units picks one on the Program page), so there is nothing to read
        public bool NoUnit;
        public List<ProposedActivityModel> Proposals = new List<ProposedActivityModel>();

        public ProposedActivitySortColumn SortColumn = ProposedActivitySortColumn.Submitted;
        public bool SortDescending = true;
        // Show only this challenge area ("" for all of them)
        public string AreaFilter = "";
        // The proposals whose description and reasons are open
        public HashSet<string> Open = new HashSet<string>();

        protected override async Task OnInitializedAsync()
        {
            if (!_storageService.IsAuthenticated)
            {
                NavigationManager.NavigateTo("index");
                return;
            }

            _page.Changed = () => InvokeAsync(StateHasChanged);
            GroupName = _storageService.GroupNameDisplay;
            await Load();
        }

        // What the data cache holds shows at once, and is read again behind the page when it is more than a day old
        internal async Task Load()
        {
            NoUnit = string.IsNullOrEmpty(_storageService.UnitId);
            if (NoUnit)
            {
                _page.Cancel();
                return;
            }

            FetchFailed = false;
            var since = Since;
            await _page.LoadAsync(Read(since), proposals => Show(proposals, since));
            FetchFailed |= _page.Failed;
        }

        internal async Task RefreshClick()
        {
            var since = Since;
            await _page.RefreshAsync(Read(since), proposals => Show(proposals, since));
            FetchFailed |= _page.Failed;
        }

        private Func<Action<string>, CancellationToken, Task<List<ProposedActivityModel>?>> Read(DateTime? since) =>
            (progress, token) => _programService.GetProposedActivities(since);

        private void Show(List<ProposedActivityModel>? proposals, DateTime? since)
        {
            _countedSince = since;
            FetchFailed = proposals == null;
            Proposals = proposals ?? new List<ProposedActivityModel>();
            // the area being shown may not have a proposal any more
            if (AreaFilter != "" && !Proposals.Any(p => p.ChallengeArea == AreaFilter))
            {
                AreaFilter = "";
            }
        }

        public void Dispose() => _page.Dispose();

        // Where the run counts start, or null for all of the unit's events
        internal DateTime? Since => SinceRange switch
        {
            ProgramIdeasRangeOption.Year => DateTime.Now.AddYears(-1),
            ProgramIdeasRangeOption.SixMonths => DateTime.Now.AddMonths(-6),
            ProgramIdeasRangeOption.UserSet => SinceDate,
            _ => null
        };

        // Counts the runs again from the chosen date
        internal async Task SinceChanged() => await Load();

        // The proposals to show: the chosen area, in the chosen order
        internal List<ProposedActivityModel> Shown
        {
            get
            {
                var wanted = Proposals.Where(p => AreaFilter == "" || p.ChallengeArea == AreaFilter);
                return ProposedActivities.Sort(wanted, SortColumn, SortDescending);
            }
        }

        // The areas that have a proposal, for the filter
        internal List<string> Areas => Proposals.Select(p => p.ChallengeArea).Distinct().OrderBy(a => a).ToList();

        internal void SortByColumn(ProposedActivitySortColumn column)
        {
            if (SortColumn == column)
                SortDescending = !SortDescending;
            else
            {
                SortColumn = column;
                SortDescending = false;
            }
        }

        internal string SortIndicator(ProposedActivitySortColumn column)
        {
            if (SortColumn != column)
                return string.Empty;
            return SortDescending ? " ▼" : " ▲";
        }

        internal void Toggle(ProposedActivityModel proposal)
        {
            if (!Open.Remove(proposal.OpenKey))
                Open.Add(proposal.OpenKey);
        }

        internal async Task ReportPdfClick() => await DownloadReport(OutputType.PDF, "pdf", "application/pdf");

        internal async Task ReportXlsxClick() => await DownloadReport(OutputType.Excel, "xlsx", "application/vnd.ms-excel");

        // The report has what is on the page: the chosen area, in the chosen order
        private async Task DownloadReport(OutputType outputType, string extension, string contentType)
        {
            var data = new ProposedActivitiesReportData { Since = _countedSince, Proposals = Shown };
            var report = await _reportService.GetProposedActivitiesReport(_storageService.GroupName ?? "Group Name", _storageService.Section, _storageService.UnitName ?? "Unit Name", outputType, JsonConvert.SerializeObject(data));
            var fileName = $"Proposed_Activities_{(_storageService.UnitName ?? "Unit").Replace(' ', '_')}.{extension}";
            await JS.InvokeVoidAsync("BlazorDownloadFile", fileName, contentType, report);
        }
    }
}
