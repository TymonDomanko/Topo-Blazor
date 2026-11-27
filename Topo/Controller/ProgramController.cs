using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Microsoft.VisualBasic;
using Newtonsoft.Json;
using Topo.Model.Cache;
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
        // Loading and reading again through the data cache
        public PageLoad _page = new PageLoad();
        public string ErrorMessage = "";
        private string groupCalendarId = string.Empty;
        private string projectPatrolCalendarId = string.Empty;

        private JsonSerializerSettings _settings = new JsonSerializerSettings
        {
            DateParseHandling = DateParseHandling.None
        };

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                await JS.InvokeVoidAsync("BindSelect2");
            }
        }

        protected override async Task OnInitializedAsync()
        {
            if (!_storageService.IsAuthenticated)
                NavigationManager.NavigateTo("index");

            _page.Changed = () => InvokeAsync(StateHasChanged);
            model.GroupName = _storageService.GroupNameDisplay;
            model.Units = _storageService.Units;
            var calendars = await _programService.GetCalendars();
            model.Calendars = calendars;
            // six months each way to start with; more on request
            model.CalendarSearchFromDate = DateTime.Today.AddMonths(-MoreMonths);
            model.CalendarSearchToDate = DateTime.Today.AddMonths(MoreMonths);
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

        internal async Task ShowUnitCalendarClick()
        {
            if (!await GetSelections(_select2Reference))
                return;

            model.DateErrorMessage = "";
            if (model.CalendarSearchToDate < model.CalendarSearchFromDate)
            {
                model.DateErrorMessage = "The search to date must be after the search from date.";
                return;
            }
            if (!(model.SelectedCalendars == null || model.SelectedCalendars.Length == 0))
            {
                //_storageService.UnitId = model.CalendarId;
                // what the data cache holds shows at once; the list is then read from Terrain, with whatever else is out of date
                ErrorMessage = "";
                await _page.LoadAsync(ReadEvents(), Show);
                ShowFailure();
            }
        }

        // The events on the chosen calendars. The list is read from Terrain every time; an event's details come from the data cache while in date,
        // and an event long past is kept until it is read again on request.
        private Func<Action<string>, CancellationToken, Task<List<EventListModel>>> ReadEvents()
        {
            var from = model.CalendarSearchFromDate;
            var to = model.CalendarSearchToDate;
            var calendars = model.SelectedCalendars;
            return (progress, cancellationToken) => _programService.GetEventsForDates(from, to, calendars);
        }

        private void Show(List<EventListModel> events) => model.Events = events.OrderBy(e => e.StartDateTime).ToList();

        private void ShowFailure()
        {
            if (_page.Failed)
            {
                ErrorMessage = "Couldn't read the events from Terrain.";
            }
        }

        // How far each way the page starts, and how much more each request adds
        private const int MoreMonths = 6;

        internal async Task LoadEarlierClick()
        {
            model.CalendarSearchFromDate = model.CalendarSearchFromDate.AddMonths(-MoreMonths);
            await ShowUnitCalendarClick();
        }

        internal async Task LoadLaterClick()
        {
            model.CalendarSearchToDate = model.CalendarSearchToDate.AddMonths(MoreMonths);
            await ShowUnitCalendarClick();
        }

        internal async Task RefreshClick()
        {
            ErrorMessage = "";
            await _page.RefreshAsync(ReadEvents(), Show);
            ShowFailure();
        }

        public void Dispose() => _page.Dispose();

        internal async Task EventAttendanceClick(string eventId)
        {
            using var scope = CacheScope.Begin(CacheMode.Fresh);
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
            using var scope = CacheScope.Begin(CacheMode.Fresh);
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
