using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Microsoft.VisualBasic;
using Newtonsoft.Json;
using Topo.Model.Common;
using Topo.Model.Program;
using Topo.Model.ReportGeneration;
using Topo.Services;

namespace Topo.Controller
{
    public class ProgramController : ComponentBase, IDisposable
    {
        [Inject]
        public StorageService _storageService { get; set; }

        [Inject]
        public IProgramService _programService { get; set; }

        [Inject]
        IJSRuntime JS { get; set; }

        [Inject]
        public IReportService _reportService { get; set; }

        [Inject]
        public NavigationManager NavigationManager { get; set; }

        [Inject]
        public IMembersService _membersService { get; set; }

        public ElementReference _select2Reference;


        public ProgramPageViewModel model = new ProgramPageViewModel();
        private string groupCalendarId = string.Empty;
        private string projectPatrolCalendarId = string.Empty;

        private JsonSerializerSettings _settings = new JsonSerializerSettings
        {
            DateParseHandling = DateParseHandling.None
        };

        public bool Loading;
        public string Progress = "";
        private bool _select2Bound;
        private DotNetObjectReference<ProgramController>? _selfReference;
        // The calendar is read again a moment after the calendars or dates change, rather than from a button
        private readonly AutoRun _auto = new AutoRun();
        private const int DateDelayMilliseconds = 800;

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                await JS.InvokeVoidAsync("BindSelect2");
                _selfReference = DotNetObjectReference.Create(this);
                await JS.InvokeVoidAsync("bindSelect2Change", _select2Reference, _selfReference);
                if (model.SelectedCalendars != null && model.SelectedCalendars.Length > 0)
                {
                    await _auto.RunAsync(LoadEvents);
                    // nothing else redraws the page once a read that began after the render has finished
                    StateHasChanged();
                }
            }
        }

        public void Dispose()
        {
            _auto.Dispose();
            _selfReference?.Dispose();
        }

        // Called from the calendar picker when the person changes the selection
        [JSInvokable]
        public async Task OnCalendarsChanged()
        {
            if (!await GetSelections(_select2Reference))
            {
                _auto.Stop();
                model.Events = null;
                Loading = false;
                Progress = "";
                StateHasChanged();
                return;
            }
            await _auto.RunAsync(LoadEvents);
            StateHasChanged();
        }

        internal async Task DatesChanged()
        {
            if (model.SelectedCalendars == null || model.SelectedCalendars.Length == 0)
                return;
            await _auto.RunAsync(LoadEvents, DateDelayMilliseconds);
        }

        protected override async Task OnInitializedAsync()
        {
            if (!_storageService.IsAuthenticated)
                NavigationManager.NavigateTo("index");

            model.GroupName = _storageService.GroupNameDisplay;
            model.Units = _storageService.Units;
            var calendars = await _programService.GetCalendars();
            model.Calendars = calendars;
            var quarter = (DateTime.Now.Month + 2) / 3;
            var quarterStartMonth = (quarter - 1) * 3 + 1;
            model.CalendarSearchFromDate = new DateTime(DateTime.Now.Year, quarterStartMonth, 1);
            model.CalendarSearchToDate = model.CalendarSearchFromDate.AddMonths(4).AddDays(-1);
            model.DateErrorMessage = "";
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
            model.UnitId = unitId;
            _storageService.UnitId = model.UnitId;
            model.UnitName = _storageService.UnitName;
        }

        public async Task<bool> GetSelections(ElementReference elementReference)
        {
            model.SelectedCalendars = (await JS.InvokeAsync<List<string>>("getSelectedValues", _select2Reference)).ToArray<string>();
            model.CalendarErrorMessage = "";

            if (model.SelectedCalendars == null || model.SelectedCalendars.Length == 0)
            {
                model.CalendarErrorMessage = "Please select at least one calendar";
                return false;
            }
            return true;
        }

        // Reads the events of the chosen calendars. A read that has been overtaken by a newer change stops, and leaves the page to the newer one.
        private async Task LoadEvents(CancellationToken cancellationToken)
        {
            model.DateErrorMessage = "";
            if (model.CalendarSearchToDate < model.CalendarSearchFromDate)
            {
                model.DateErrorMessage = "The search to date must be after the search from date.";
                model.Events = null;
                return;
            }

            Loading = true;
            await _programService.SetCalendar(model.SelectedCalendars);
            try
            {
                var events = await _programService.GetEventsForDates(model.CalendarSearchFromDate, model.CalendarSearchToDate, cancellationToken,
                    step => { if (!cancellationToken.IsCancellationRequested) { Progress = step; InvokeAsync(StateHasChanged); } });
                cancellationToken.ThrowIfCancellationRequested();
                model.Events = events.OrderBy(e => e.StartDateTime).ToList();
            }
            finally
            {
                await _programService.ResetCalendar();
                if (!cancellationToken.IsCancellationRequested)
                {
                    Loading = false;
                    Progress = "";
                }
            }
        }

        internal async Task EventAttendanceClick(string eventId)
        {
            var groupName = _storageService.GroupName ?? "Group Name";
            var unitName = _storageService.UnitName ?? "Unit Name";
            var section = _storageService.Section;

            var eventListModel = await _programService.GetAttendanceForEvent(eventId);
            var serialisedEventListModel = JsonConvert.SerializeObject(eventListModel);
            var report = await _reportService.GetEventAttendanceReport(groupName, section, unitName, OutputType.Excel, serialisedEventListModel);

            var fileName = $"Attendance_{unitName.Replace(' ', '_')}_{eventListModel.EventDisplay.Replace(' ', '_')}.xlsx";

            // Send the data to JS to actually download the file
            await JS.InvokeVoidAsync("BlazorDownloadFile", fileName, "application/vnd.ms-excel", report);
        }

        internal async Task AttendanceReportPdfClick()
        {
            byte[] report = await AttendanceReport(OutputType.PDF);
            var fileName = $"Attendance_Report_{_storageService.UnitName.Replace(' ', '_')}.pdf";

            // Send the data to JS to actually download the file
            await JS.InvokeVoidAsync("BlazorDownloadFile", fileName, "application/pdf", report);
        }

        internal async Task AttendanceReportXlsxClick()
        {
            byte[] report = await AttendanceReport(OutputType.Excel);
            var fileName = $"Attendance_Report_{_storageService.UnitName.Replace(' ', '_')}.xlsx";

            // Send the data to JS to actually download the file
            await JS.InvokeVoidAsync("BlazorDownloadFile", fileName, "application/vnd.ms-excel", report);
        }

        private async Task<byte[]> AttendanceReport(OutputType outputType = OutputType.PDF)
        {
            var groupName = _storageService.GroupName ?? "Group Name";
            var unitName = _storageService.UnitName ?? "Unit Name";
            var section = _storageService.Section;

            var attendanceReportData = await _programService.GenerateAttendanceReportData(model.CalendarSearchFromDate, model.CalendarSearchToDate, model.SelectedCalendars);
            var serialisedAttendanceReportData = JsonConvert.SerializeObject(attendanceReportData);
            var report = await _reportService.GetAttendanceReport(groupName, section, unitName, outputType, serialisedAttendanceReportData, model.CalendarSearchFromDate, model.CalendarSearchToDate);

            return report;
        }

        internal async Task TermProgramReportXlsxClick()
        {
            byte[] report = await TermProgramReport(OutputType.Excel);
            var fileName = $"Term_Program_{_storageService.UnitName.Replace(' ', '_')}.xlsx";

            // Send the data to JS to actually download the file
            await JS.InvokeVoidAsync("BlazorDownloadFile", fileName, "application/vnd.ms-excel", report);
        }

        internal async Task TermProgramReportPdfClick()
        {
            byte[] report = await TermProgramReport(OutputType.PDF);
            var fileName = $"Term_Program_{_storageService.UnitName.Replace(' ', '_')}.pdf";

            // Send the data to JS to actually download the file
            await JS.InvokeVoidAsync("BlazorDownloadFile", fileName, "application/pdf", report);
        }

        private async Task<byte[]> TermProgramReport(OutputType outputType = OutputType.PDF)
        {
            var groupName = _storageService.GroupName ?? "Group Name";
            var unitName = _storageService.UnitName ?? "Unit Name";
            var section = _storageService.Section;

            var serialisedTermProgramData = JsonConvert.SerializeObject(model.Events, _settings);
            var report = await _reportService.GetTermProgramReport(groupName, section, unitName, outputType, serialisedTermProgramData);

            return report;
        }


    }

}
