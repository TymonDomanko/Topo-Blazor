using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Newtonsoft.Json;
using Topo.Model.Attendance;
using Topo.Model.Common;
using Topo.Model.ReportGeneration;
using Topo.Model.Cache;
using Topo.Services;

namespace Topo.Controller
{
    public class AttendanceController : ComponentBase, IDisposable
    {
        [Inject]
        public StorageService _storageService { get; set; }

        [Inject]
        public IProgramService _programService { get; set; }

        [Inject]
        public IAttendanceService _attendanceService { get; set; }

        [Inject]
        public IReportService _reportService { get; set; }

        [Inject]
        IJSRuntime JS { get; set; }

        [Inject]
        public NavigationManager NavigationManager { get; set; }

        public AttendancePageViewModel model = new AttendancePageViewModel();
        public AttendanceReport? Report;
        // Loading and reading again through the data cache
        public PageLoad _page = new PageLoad();
        public bool Busy => _page.Loading;
        public string Progress => _page.Progress;

        // What was read for the unit and period; the options then only lay it out again, without reading Terrain again
        private AttendanceData? _data;
        // The attendance is read again a moment after the unit, calendars or period change, rather than from a button
        private readonly AutoRun _auto = new AutoRun();
        private const int OptionDelayMilliseconds = 800;
        // Reads everything for the report again, leaving it on show until the new one is in
        internal async Task RefreshClick()
        {
            await _page.RefreshAsync(ReadAttendance(), Show);
            ShowFailure();
        }

        public void Dispose()
        {
            _auto.Dispose();
            _page.Dispose();
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
            Report = null;
            _data = null;
            await _auto.RunAsync(Generate);
        }

        private void SelectUnit(string unitId)
        {
            model.UnitId = unitId;
            _storageService.UnitId = unitId;
            // use the calendar named after the unit when there is one
            var match = CalendarMatcher.FindForUnit(model.Calendars, _storageService.UnitName);
            if (match != null)
            {
                model.SelectedCalendars = new HashSet<string> { match };
            }
        }

        internal async Task ToggleCalendar(string calendarId, bool selected)
        {
            if (selected)
            {
                model.SelectedCalendars.Add(calendarId);
            }
            else
            {
                model.SelectedCalendars.Remove(calendarId);
            }
            await _auto.RunAsync(Generate, OptionDelayMilliseconds);
        }

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

        internal async Task DatesChanged() => await _auto.RunAsync(Generate, OptionDelayMilliseconds);

        // Splitting by patrol and the names change how it is laid out, not what was read
        internal void OptionChanged() => Recalculate();

        private void Recalculate() => Report = _data == null ? null : AttendanceLayout.Build(_data, model.BreakByPatrol, model.UseFullNames);

        // Shows who came to what for the unit, calendars and period chosen: from the data cache first, then with the lists of events read from
        // Terrain. A read that has been overtaken by a newer change stops, and leaves the page to the newer one.
        private async Task Generate(CancellationToken cancellationToken)
        {
            model.ErrorMessage = "";
            Report = null;
            _data = null;
            // no unit chosen yet, so there is nothing to read and nothing to complain about
            if (string.IsNullOrEmpty(model.UnitId))
            {
                _page.Cancel();
                return;
            }
            if (model.SelectedCalendars.Count == 0)
            {
                model.ErrorMessage = "Please select at least one calendar.";
            }
            else if (model.To < model.From)
            {
                model.ErrorMessage = "The to date must be after the from date.";
            }
            if (model.ErrorMessage != "")
            {
                _page.Cancel();
                return;
            }

            using var overtaken = cancellationToken.Register(_page.Cancel);
            await _page.LoadAsync(ReadAttendance(), Show);
            ShowFailure();
        }

        private Func<Action<string>, CancellationToken, Task<AttendanceData>> ReadAttendance()
        {
            var unitId = model.UnitId;
            var from = model.From;
            var to = model.To;
            var calendars = model.SelectedCalendars.ToArray();
            return (progress, token) => _attendanceService.GetAttendance(unitId, from, to, calendars, progress, token);
        }

        private void Show(AttendanceData data)
        {
            _data = data;
            Recalculate();
        }

        private void ShowFailure()
        {
            if (_page.Failed)
            {
                model.ErrorMessage = "Couldn't read the attendance from Terrain.";
            }
        }

        internal async Task DownloadClick(OutputType outputType)
        {
            if (Report == null || _page.Updating)
            {
                return;
            }

            var report = await _reportService.GetAttendanceAnalysisReport(_storageService.GroupName ?? "Group Name", Report.Section, Report.UnitName, outputType, JsonConvert.SerializeObject(Report));
            var isPdf = outputType == OutputType.PDF;
            var fileName = $"Attendance_{Report.UnitName.Replace(' ', '_')}_{Report.From:yyyy-MM-dd}_{Report.To:yyyy-MM-dd}" + (isPdf ? ".pdf" : ".xlsx");
            await JS.InvokeVoidAsync("BlazorDownloadFile", fileName, isPdf ? "application/pdf" : "application/vnd.ms-excel", report);
        }
    }
}
