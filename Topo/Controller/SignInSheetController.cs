using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Newtonsoft.Json;
using Topo.Model.Members;
using Topo.Model.Program;
using Topo.Model.ReportGeneration;
using Topo.Model.Cache;
using Topo.Services;

namespace Topo.Controller
{
    public class SignInSheetController : ComponentBase, IDisposable
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
        public Blazored.LocalStorage.ILocalStorageService _localStorageService { get; set; }

        public ElementReference _select2Reference;

        public SignInSheetPageViewModel model = new SignInSheetPageViewModel();

        // Live preview of the sheet for the chosen event. Names, columns and layout are applied on the
        // page; only the units ticked change who is invited, so only they refetch.
        public string EventId = "";
        public List<MemberListModel>? PreviewMembers;
        public bool PreviewFailed;

        private const string OptionsKey = "SignInSheetOptions";

        public bool AllUnitsSelected => model.Units.Count > 0 && model.SelectedUnits.Count == model.Units.Count;

        private DotNetObjectReference<SignInSheetController>? _selfReference;
        // set when the calendar selection was changed in code, so the select2 box can be refreshed to show it
        private bool _syncCalendarSelection;
        // Loading and reading again through the data cache: the event list and the sheet's people each load on their own
        public PageLoad _eventsPage = new PageLoad();
        public PageLoad _previewPage = new PageLoad();
        public bool Reading => _eventsPage.Loading || _previewPage.Loading;
        public bool Updating => _eventsPage.Updating || _previewPage.Updating;
        // When the oldest of what the event list and the sheet were made from was read
        public DateTime? ReadUtc => _eventsPage.ReadUtc == null || _previewPage.ReadUtc < _eventsPage.ReadUtc ? _previewPage.ReadUtc : _eventsPage.ReadUtc;

        // Reads the events and the sheet's people again, leaving what is shown in place until they are in
        internal async Task RefreshClick()
        {
            if (model.SelectedCalendars.Length > 0 && model.CalendarSearchToDate >= model.CalendarSearchFromDate)
            {
                await _eventsPage.RefreshAsync(ReadEvents(), ShowEvents);
            }
            if (model.SelectedUnits.Count > 0)
            {
                await _previewPage.RefreshAsync(ReadPreview(), ShowPreview);
            }
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                await JS.InvokeVoidAsync("BindSelect2");
                _selfReference = DotNetObjectReference.Create(this);
                await JS.InvokeVoidAsync("bindSelect2Change", _select2Reference, _selfReference);
            }

            if (_syncCalendarSelection)
            {
                _syncCalendarSelection = false;
                await JS.InvokeVoidAsync("setSelectedValues", _select2Reference, model.SelectedCalendars);
            }
        }

        public void Dispose()
        {
            _selfReference?.Dispose();
            _eventsPage.Dispose();
            _previewPage.Dispose();
        }

        protected override async Task OnInitializedAsync()
        {
            if (!_storageService.IsAuthenticated)
            {
                NavigationManager.NavigateTo("index");
                return;
            }

            _eventsPage.Changed = () => InvokeAsync(StateHasChanged);
            _previewPage.Changed = () => InvokeAsync(StateHasChanged);
            model.GroupName = _storageService.GroupNameDisplay;
            model.Units = _storageService.Units ?? new Dictionary<string, string>();
            model.Calendars = await _programService.GetCalendars();
            // upcoming events: from today to the end of the four months starting this quarter
            var quarter = (DateTime.Now.Month + 2) / 3;
            var quarterStartMonth = (quarter - 1) * 3 + 1;
            model.CalendarSearchFromDate = DateTime.Today;
            model.CalendarSearchToDate = new DateTime(DateTime.Now.Year, quarterStartMonth, 1).AddMonths(4).AddDays(-1);
            if (!string.IsNullOrEmpty(_storageService.UnitId) && model.Units.ContainsKey(_storageService.UnitId))
            {
                SetUnit(_storageService.UnitId, true);
            }

            var savedOptions = await _localStorageService.GetItemAsync<SignInSheetOptions>(OptionsKey);
            if (savedOptions != null)
            {
                model.Options = savedOptions;
            }

            await LoadEventsAsync();
            await RefreshPreview();
        }

        // Saved per browser so the options don't have to be re-picked every time.
        private async Task SaveOptions()
        {
            await _localStorageService.SetItemAsync(OptionsKey, model.Options);
        }

        internal async Task ToggleUnit(string unitId, bool selected)
        {
            SetUnit(unitId, selected);
            await UnitsChanged();
        }

        internal async Task ToggleAllUnits(bool selected)
        {
            foreach (var unitId in model.Units.Keys)
                SetUnit(unitId, selected);
            await UnitsChanged();
        }

        // Ticks or unticks a unit, and its calendar with it
        private void SetUnit(string unitId, bool selected)
        {
            if (selected)
            {
                model.SelectedUnits.Add(unitId);
            }
            else
            {
                model.SelectedUnits.Remove(unitId);
            }
            SetCalendarOfUnit(unitId, selected);
        }

        private async Task UnitsChanged()
        {
            // one unit on its own is the unit the rest of Topo works on
            if (model.SelectedUnits.Count == 1)
            {
                _storageService.UnitId = model.SelectedUnits.First();
            }
            await LoadEventsAsync();
            await RefreshPreview();
        }

        // The units ticked, in the order they are listed
        private List<string> SelectedUnitIds() => model.Units.Keys.Where(model.SelectedUnits.Contains).ToList();

        // A unit's calendar (the one named after it) is ticked and unticked with it; calendars picked by hand stay as they are
        private void SetCalendarOfUnit(string unitId, bool selected)
        {
            var calendarId = FindCalendarForUnit(model.Units[unitId]);
            if (calendarId == null)
            {
                return;
            }

            var calendars = model.SelectedCalendars.Where(c => c != calendarId).ToList();
            if (selected)
            {
                calendars.Add(calendarId);
            }
            model.SelectedCalendars = calendars.ToArray();
            _syncCalendarSelection = true;
        }

        // A calendar named the same as the unit; failing that, one whose name contains the unit's name
        // (or the other way round).
        private string? FindCalendarForUnit(string? unitName)
        {
            if (string.IsNullOrWhiteSpace(unitName))
            {
                return null;
            }

            var name = unitName.Trim();
            var calendars = model.Calendars.Select(c => (id: c.Key, title: c.Value.Trim())).ToList();
            return (calendars.FirstOrDefault(c => string.Equals(c.title, name, StringComparison.OrdinalIgnoreCase)).id
                ?? calendars.FirstOrDefault(c => c.title.Contains(name, StringComparison.OrdinalIgnoreCase)).id
                ?? calendars.FirstOrDefault(c => c.title.Length > 0 && name.Contains(c.title, StringComparison.OrdinalIgnoreCase)).id);
        }

        // Called from the calendar picker when the person changes the selection.
        [JSInvokable]
        public async Task OnCalendarsChanged()
        {
            model.SelectedCalendars = (await JS.InvokeAsync<List<string>>("getSelectedValues", _select2Reference)).ToArray();
            await LoadEventsAsync();
            StateHasChanged();
        }

        // Loads the events for the chosen calendars and dates, from the data cache first and then from Terrain. A later change overtakes an
        // earlier one that is still being read.
        internal async Task LoadEventsAsync()
        {
            model.CalendarErrorMessage = "";
            model.DateErrorMessage = "";
            if (model.SelectedCalendars.Length == 0)
            {
                model.CalendarErrorMessage = "Please select at least one calendar";
                _eventsPage.Cancel();
                model.Events = null;
            }
            else if (model.CalendarSearchToDate < model.CalendarSearchFromDate)
            {
                model.DateErrorMessage = "The search to date must be after the search from date.";
                _eventsPage.Cancel();
                model.Events = null;
            }
            else
            {
                await _eventsPage.LoadAsync(ReadEvents(), ShowEvents);
            }

            // the previously chosen event may no longer be in the list
            if (EventId != "" && !_eventsPage.Loading && !_eventsPage.Updating && (model.Events == null || !model.Events.Any(e => e.Id == EventId)))
            {
                EventId = "";
                await RefreshPreview();
            }
        }

        private Func<Action<string>, CancellationToken, Task<List<EventListModel>>> ReadEvents()
        {
            var from = model.CalendarSearchFromDate;
            var to = model.CalendarSearchToDate;
            var calendars = model.SelectedCalendars;
            return (progress, cancellationToken) => _programService.GetEventsForDates(from, to, calendars, cancellationToken);
        }

        private void ShowEvents(List<EventListModel> events) => model.Events = events.OrderBy(e => e.StartDateTime).ToList();

        internal async Task EventChange(ChangeEventArgs e)
        {
            EventId = e.Value?.ToString() ?? "";
            await RefreshPreview();
        }

        private async Task RefreshPreview()
        {
            PreviewFailed = false;
            // the sheet is for a unit; with no event it has a blank title
            if (model.SelectedUnits.Count == 0)
            {
                _previewPage.Cancel();
                PreviewMembers = null;
                return;
            }

            await _previewPage.LoadAsync(ReadPreview(), ShowPreview);
            if (_previewPage.Failed)
            {
                PreviewMembers = null;
                PreviewFailed = true;
            }
        }

        private Func<Action<string>, CancellationToken, Task<List<MemberListModel>?>> ReadPreview()
        {
            var eventId = EventId;
            var unitIds = SelectedUnitIds();
            return async (progress, cancellationToken) => string.IsNullOrEmpty(eventId)
                ? await _programService.GetMembersForUnits(unitIds)
                : await _programService.GetInviteesForEvent(eventId, unitIds);
        }

        private void ShowPreview(List<MemberListModel>? members)
        {
            PreviewMembers = members;
            PreviewFailed = members == null;
        }

        internal async Task FullNamesChange(ChangeEventArgs e)
        {
            model.Options.UseFullNames = (bool)(e.Value ?? false);
            await SaveOptions();
        }

        internal async Task AttendanceModeChange(ChangeEventArgs e)
        {
            model.Options.UseTimeInOutSignature = e.Value?.ToString() == "timeinout";
            await SaveOptions();
        }

        internal async Task RegisteredColumnChange(ChangeEventArgs e)
        {
            model.Options.IncludeRegisteredColumn = (bool)(e.Value ?? false);
            await SaveOptions();
        }

        internal async Task PaidColumnChange(ChangeEventArgs e)
        {
            model.Options.IncludePaidColumn = (bool)(e.Value ?? false);
            await SaveOptions();
        }

        internal async Task ParticipationColumnsChange(ChangeEventArgs e)
        {
            model.Options.IncludeParticipationColumns = (bool)(e.Value ?? false);
            await SaveOptions();
        }

        internal async Task DownloadClick(OutputType outputType)
        {
            if (PreviewMembers == null || Updating)
            {
                return;
            }

            // no event chosen: the sheet has no event name
            var eventName = string.IsNullOrEmpty(EventId)
                ? ""
                : model.Events?.Where(e => e.Id == EventId).FirstOrDefault()?.EventDisplay ?? "";
            var groupName = _storageService.GroupName ?? "Group Name";
            // the units' names (the sheet is for all of them) and, when they are all one section, its logo
            var units = SelectedUnitIds();
            var unitName = string.Join(", ", units.Select(id => model.Units[id]));
            var sections = units.Select(id => (_storageService.UnitSections ?? new Dictionary<string, string>()).GetValueOrDefault(id, "")).Distinct().ToList();
            var section = sections.Count == 1 ? sections[0] : "";

            var serialisedSortedMemberList = JsonConvert.SerializeObject(PreviewMembers.OrderBy(m => m.first_name).ThenBy(m => m.last_name));
            var report = await _reportService.GetSignInSheetReport(groupName, section, unitName, outputType, serialisedSortedMemberList, eventName, model.Options);

            var isPdf = outputType == OutputType.PDF;
            // the units' names and the event's date hold commas and slashes, which don't belong in a file name
            var nameInFile = System.Text.RegularExpressions.Regex.Replace(unitName, @"[\s,]+", "_");
            var eventInFile = System.Text.RegularExpressions.Regex.Replace(eventName, @"[\s,/\\:*?""<>|]+", "_");
            var fileName = (string.IsNullOrEmpty(eventName) ? $"SignInSheet_{nameInFile}" : $"SignInSheet_{nameInFile}_{eventInFile}") + (isPdf ? ".pdf" : ".xlsx");
            await JS.InvokeVoidAsync("BlazorDownloadFile", fileName, isPdf ? "application/pdf" : "application/vnd.ms-excel", report);
        }
    }
}
