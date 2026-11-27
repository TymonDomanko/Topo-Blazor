using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Newtonsoft.Json;
using Topo.Model.Program;
using Topo.Model.ReportGeneration;
using Topo.Model.Cache;
using Topo.Services;

namespace Topo.Controller
{
    public class ProgramIdeasController : ComponentBase, IDisposable
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

        public ElementReference _select2Reference;

        public ProgramIdeasPageViewModel model = new ProgramIdeasPageViewModel();

        private bool _select2Bound;
        // Loading and reading again through the data cache
        public PageLoad _page = new PageLoad();

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            // select2 is set up once the calendars are in the list, so a calendar already chosen (the only one there is)
            // shows as selected
            if (!_select2Bound && model.Calendars.Any())
            {
                _select2Bound = true;
                await JS.InvokeVoidAsync("BindSelect2");
            }
        }

        protected override async Task OnInitializedAsync()
        {
            if (!_storageService.IsAuthenticated)
            {
                NavigationManager.NavigateTo("index");
                return;
            }

            _page.Changed = () => InvokeAsync(StateHasChanged);
            model.GroupName = _storageService.GroupNameDisplay;
            model.Calendars = await _programService.GetCalendars();
            model.SelectTheOnlyCalendar();
        }

        public async Task<bool> GetSelections()
        {
            model.SelectedCalendars = (await JS.InvokeAsync<List<string>>("getSelectedValues", _select2Reference)).ToArray();
            model.CalendarErrorMessage = "";

            if (model.SelectedCalendars == null || model.SelectedCalendars.Length == 0)
            {
                model.CalendarErrorMessage = "Please select at least one calendar";
                return false;
            }
            return true;
        }

        // Terrain's events API appears to reject (or silently fail on) a DateTime.MinValue
        // "from" date, so "no limit" uses a fixed floor date well before Terrain existed instead.
        private static readonly DateTime EarliestPossibleDate = new DateTime(2015, 1, 1);

        private static DateTime ResolveFromDate(ProgramIdeasRangeOption option, DateTime userSetDate)
        {
            return option switch
            {
                ProgramIdeasRangeOption.Year => DateTime.Now.AddYears(-1),
                ProgramIdeasRangeOption.SixMonths => DateTime.Now.AddMonths(-6),
                ProgramIdeasRangeOption.UserSet => userSetDate,
                _ => EarliestPossibleDate
            };
        }

        internal async Task ShowProgramIdeasClick()
        {
            if (!await GetSelections())
                return;

            var fromDate = ResolveFromDate(model.HistoryRange, model.HistoryFromDate);
            DateTime? highlightFromDate = model.HighlightRange == ProgramIdeasRangeOption.None
                ? null
                : ResolveFromDate(model.HighlightRange, model.HighlightFromDate);

            // the ideas from the kept list show at once; the list is then read from Terrain
            var calendars = model.SelectedCalendars;
            _read = (progress, cancellationToken) => _programService.GetProgramIdeas(fromDate, highlightFromDate, calendars);
            await _page.LoadAsync(_read, Show);
            ShowFailure();
        }

        // What the ideas on show were read with
        private Func<Action<string>, CancellationToken, Task<List<ProgramIdeaItemModel>?>>? _read;

        internal async Task RefreshClick()
        {
            if (_read != null)
            {
                await _page.RefreshAsync(_read, Show);
                ShowFailure();
            }
        }

        private void Show(List<ProgramIdeaItemModel>? ideas)
        {
            model.FetchFailed = ideas == null;
            model.Ideas = ApplySort(ideas ?? new List<ProgramIdeaItemModel>(), model.SortColumn, model.SortDescending);
        }

        private void ShowFailure()
        {
            if (_page.Failed)
            {
                model.FetchFailed = true;
                model.Ideas ??= new List<ProgramIdeaItemModel>();
            }
        }

        public void Dispose() => _page.Dispose();

        // One shared sort state applies across every Challenge Area group/sheet, not per-group.
        internal void SortByColumn(ProgramIdeaSortColumn column)
        {
            if (model.Ideas == null)
                return;

            if (model.SortColumn == column)
                model.SortDescending = !model.SortDescending;
            else
            {
                model.SortColumn = column;
                model.SortDescending = false;
            }

            model.Ideas = ApplySort(model.Ideas, model.SortColumn, model.SortDescending);
        }

        internal string SortIndicator(ProgramIdeaSortColumn column)
        {
            if (model.SortColumn != column)
                return string.Empty;
            return model.SortDescending ? " ▼" : " ▲";
        }

        private static List<ProgramIdeaItemModel> ApplySort(List<ProgramIdeaItemModel> ideas, ProgramIdeaSortColumn column, bool descending)
        {
            // ChallengeArea always sorts first so each area's rows stay grouped together on the page
            // and in the exported report, regardless of which column the user actually sorted by.
            var ordered = ideas.OrderBy(i => i.ChallengeArea);
            IOrderedEnumerable<ProgramIdeaItemModel> sorted = column switch
            {
                ProgramIdeaSortColumn.LastRunDate => descending ? ordered.ThenByDescending(i => i.LastRunDate) : ordered.ThenBy(i => i.LastRunDate),
                ProgramIdeaSortColumn.TimesRun => descending ? ordered.ThenByDescending(i => i.TimesRun) : ordered.ThenBy(i => i.TimesRun),
                _ => descending ? ordered.ThenByDescending(i => i.ActivityName) : ordered.ThenBy(i => i.ActivityName)
            };
            return sorted.ToList();
        }

        internal async Task ProgramIdeasReportXlsxClick()
        {
            byte[] report = await ProgramIdeasReport(OutputType.Excel);
            var fileName = $"Program_Ideas_{_storageService.UnitName.Replace(' ', '_')}.xlsx";

            // Send the data to JS to actually download the file
            await JS.InvokeVoidAsync("BlazorDownloadFile", fileName, "application/vnd.ms-excel", report);
        }

        internal async Task ProgramIdeasReportPdfClick()
        {
            byte[] report = await ProgramIdeasReport(OutputType.PDF);
            var fileName = $"Program_Ideas_{_storageService.UnitName.Replace(' ', '_')}.pdf";

            // Send the data to JS to actually download the file
            await JS.InvokeVoidAsync("BlazorDownloadFile", fileName, "application/pdf", report);
        }

        private async Task<byte[]> ProgramIdeasReport(OutputType outputType)
        {
            var groupName = _storageService.GroupName ?? "Group Name";
            var unitName = _storageService.UnitName ?? "Unit Name";
            var section = _storageService.Section;

            var serialisedIdeas = JsonConvert.SerializeObject(model.Ideas);
            return await _reportService.GetProgramIdeasReport(groupName, section, unitName, outputType, serialisedIdeas);
        }
    }
}
