using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Newtonsoft.Json;
using Topo.Model.Common;
using Topo.Model.ReportGeneration;
using Topo.Model.SectionReport;
using Topo.Services;

namespace Topo.Controller
{
    public class SectionReportController : ComponentBase, IDisposable
    {
        [Inject]
        public StorageService _storageService { get; set; }

        [Inject]
        public IProgramService _programService { get; set; }

        [Inject]
        public ISectionReportService _sectionReportService { get; set; }

        [Inject]
        public IReportService _reportService { get; set; }

        [Inject]
        public Blazored.LocalStorage.ILocalStorageService _localStorageService { get; set; }

        [Inject]
        IJSRuntime JS { get; set; }

        [Inject]
        public NavigationManager NavigationManager { get; set; }

        public SectionReportPageViewModel model = new SectionReportPageViewModel();
        public SectionReportResult? Result;
        public bool Busy;
        public string Progress = "";
        // The report is read again a moment after the unit, calendars or period change, rather than from a button
        private readonly AutoRun _auto = new AutoRun();
        private const int OptionDelayMilliseconds = 800;

        public void Dispose() => _auto.Dispose();

        // Only offered to someone who can see more than one unit
        public bool CanDoGroup => model.Units.Count > 1;
        public bool IsGroup => model.UnitId == SectionReportPageViewModel.WholeGroup;

        private static string RosterKey(string unitId) => $"SectionReportRoster_{unitId}";

        protected override async Task OnInitializedAsync()
        {
            if (!_storageService.IsAuthenticated)
            {
                NavigationManager.NavigateTo("index");
                return;
            }

            model.GroupName = _storageService.GroupNameDisplay;
            model.Units = _storageService.Units ?? new Dictionary<string, string>();
            model.Calendars = await _programService.GetCalendars();
            SetPeriod(ThisQuarterStart(), ThisQuarterStart().AddMonths(3).AddDays(-1));
            if (!string.IsNullOrEmpty(_storageService.UnitId))
            {
                SelectUnit(_storageService.UnitId);
                await _auto.RunAsync(Generate);
            }
        }

        internal async Task UnitChange(ChangeEventArgs e)
        {
            SelectUnit(e.Value?.ToString() ?? "");
            Result = null;
            await _auto.RunAsync(Generate);
        }

        private void SelectUnit(string unitId)
        {
            model.UnitId = unitId;
            if (unitId == SectionReportPageViewModel.WholeGroup || unitId == "")
                return;

            _storageService.UnitId = unitId;
            // use the calendar named after the unit when there is one
            var match = CalendarMatcher.FindForUnit(model.Calendars, _storageService.UnitName);
            if (match != null)
                model.SelectedCalendars = new HashSet<string> { match };
        }

        internal async Task ToggleCalendar(string calendarId, bool selected)
        {
            if (selected)
                model.SelectedCalendars.Add(calendarId);
            else
                model.SelectedCalendars.Remove(calendarId);
            await _auto.RunAsync(Generate, OptionDelayMilliseconds);
        }

        internal async Task DatesChanged() => await _auto.RunAsync(Generate, OptionDelayMilliseconds);

        public List<List<KeyValuePair<string, string>>> CalendarColumns() => CalendarMatcher.Columns(model.Calendars);

        private static DateTime ThisQuarterStart() => new DateTime(DateTime.Today.Year, (DateTime.Today.Month - 1) / 3 * 3 + 1, 1);

        private void SetPeriod(DateTime from, DateTime to)
        {
            model.From = from;
            model.To = to;
        }

        internal async Task LastMonth()
        {
            var firstOfThisMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            SetPeriod(firstOfThisMonth.AddMonths(-1), firstOfThisMonth.AddDays(-1));
            await _auto.RunAsync(Generate, OptionDelayMilliseconds);
        }

        internal async Task ThisQuarter()
        {
            SetPeriod(ThisQuarterStart(), ThisQuarterStart().AddMonths(3).AddDays(-1));
            await _auto.RunAsync(Generate, OptionDelayMilliseconds);
        }

        internal async Task LastQuarter()
        {
            SetPeriod(ThisQuarterStart().AddMonths(-3), ThisQuarterStart().AddDays(-1));
            await _auto.RunAsync(Generate, OptionDelayMilliseconds);
        }

        internal async Task ThisYear()
        {
            SetPeriod(new DateTime(DateTime.Today.Year, 1, 1), new DateTime(DateTime.Today.Year, 12, 31));
            await _auto.RunAsync(Generate, OptionDelayMilliseconds);
        }

        // Reads the report for what is chosen. A read that has been overtaken by a newer change stops, and leaves the page to the newer one.
        private async Task Generate(CancellationToken cancellationToken)
        {
            model.ErrorMessage = "";
            model.Message = "";
            // no unit chosen yet, so there is nothing to read and nothing to complain about
            if (string.IsNullOrEmpty(model.UnitId))
            {
                Result = null;
                Busy = false;
                return;
            }
            if (model.To < model.From)
                model.ErrorMessage = "The to date must be after the from date.";
            if (model.ErrorMessage != "")
            {
                Result = null;
                Busy = false;
                return;
            }

            var unitIds = IsGroup ? model.Units.Keys.ToList() : new List<string> { model.UnitId };
            var request = new SectionReportRequest
            {
                UnitIds = unitIds,
                IsGroup = IsGroup,
                From = model.From,
                To = model.To
            };
            foreach (var unitId in unitIds)
            {
                // a group report reads each unit's events from the calendar named after it
                var calendars = IsGroup
                    ? new[] { CalendarMatcher.FindForUnit(model.Calendars, model.Units[unitId]) }.Where(c => c != null).Select(c => c!).ToArray()
                    : model.SelectedCalendars.ToArray();
                request.CalendarIdsByUnit[unitId] = calendars;
                var saved = await ReadRoster(unitId);
                if (saved != null)
                    request.PreviousRosters[unitId] = saved;
            }

            Busy = true;
            Result = null;
            try
            {
                var result = await _sectionReportService.GetSectionReport(request,
                    step => { if (!cancellationToken.IsCancellationRequested) { Progress = step; InvokeAsync(StateHasChanged); } }, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                Result = result;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                model.ErrorMessage = "Couldn't build the report.";
            }
            finally
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    Busy = false;
                    Progress = "";
                }
            }
        }

        // A roster that can't be read back (cleared or changed storage) is the same as none having been saved
        private async Task<Topo.Model.SectionReport.RosterSnapshot?> ReadRoster(string unitId)
        {
            try
            {
                return await _localStorageService.GetItemAsync<Topo.Model.SectionReport.RosterSnapshot>(RosterKey(unitId));
            }
            catch (Exception)
            {
                return null;
            }
        }

        internal async Task DownloadClick(OutputType outputType)
        {
            if (Result == null)
                return;

            var report = Result.Report;
            var first = report.Sections.FirstOrDefault();
            var unitName = report.IsGroup ? "Group" : first?.UnitName ?? "";
            // the logo is the section being reported on (the first unit's, for a group)
            var bytes = await _reportService.GetSectionReport(_storageService.GroupName ?? "Group Name", first?.Section ?? "", unitName, outputType, JsonConvert.SerializeObject(report));
            var isPdf = outputType == OutputType.PDF;
            var fileName = $"{(report.IsGroup ? "GroupReport" : "SectionReport")}_{unitName.Replace(' ', '_')}_{report.From:yyyy-MM-dd}_{report.To:yyyy-MM-dd}" + (isPdf ? ".pdf" : ".xlsx");
            await JS.InvokeVoidAsync("BlazorDownloadFile", fileName, isPdf ? "application/pdf" : "application/vnd.ms-excel", bytes);

            if (model.SaveRoster)
            {
                foreach (var roster in Result.Rosters)
                    await _localStorageService.SetItemAsync(RosterKey(roster.Key), roster.Value);
                model.Message = "Rosters saved: the next report will name who has joined or left since this one.";
            }
        }
    }
}
