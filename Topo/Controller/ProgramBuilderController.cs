using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Newtonsoft.Json;
using System.Text;
using Topo.Model.Common;
using Topo.Model.Program;
using Topo.Model.ProgramBuilder;
using Topo.Model.ReportGeneration;
using Topo.Model.Cache;
using Topo.Services;

namespace Topo.Controller
{
    // Backs the Program Builder: sets up a term's section nights and duty patrol cycle, takes the activities, camps and hikes, and produces the
    // program as a Word document, pdf and spreadsheet, along with what has to be done in Terrain to match it. Everything entered is kept on
    // this device, for the unit, so a term can be carried on with or started from the last one.
    public class ProgramBuilderController : ComponentBase, IDisposable
    {
        // The value of the duty patrol choice for a night with no patrol on duty
        public const string NoDutyChoice = "__none";

        [Inject]
        public StorageService _storageService { get; set; }

        [Inject]
        public IProgramBuilderService _programBuilderService { get; set; }

        [Inject]
        public IReportService _reportService { get; set; }

        [Inject]
        public ILocalStorageService _localStorageService { get; set; }

        [Inject]
        IJSRuntime JS { get; set; }

        [Inject]
        public NavigationManager NavigationManager { get; set; }

        public string GroupName = "";
        public Dictionary<string, string> Units = new Dictionary<string, string>();
        public string UnitId = "";
        public ProgramDraft draft = new ProgramDraft();
        // The unit's patrols and leaders, from Terrain
        public List<string> Patrols = new List<string>();
        public List<TerrainLeader> Leaders = new List<TerrainLeader>();
        public bool Busy;
        // When the oldest of the activities last read from Terrain was read
        public DateTime? ReadUtc;
        public string Progress = "";
        public string ErrorMessage = "";
        public string Message = "";

        // What Terrain has for the program's dates, once it has been read, to fill the program from and to work out the changes to make
        private List<GetEventResultModel>? _terrainEvents;
        private readonly AutoRun _auto = new AutoRun();

        public void Dispose()
        {
            _auto.Dispose();
            _dragReference?.Dispose();
        }

        public ProgramDocument Document => ProgramLayout.Build(draft);
        public List<ProgramRow> Nights => ProgramPlanner.Nights(draft);
        public string UnitName => Units.TryGetValue(UnitId, out var name) ? name : "";
        // The leaders who can be put in charge: those in the unit's membership by first name, except one whose contact has been renamed, who goes by
        // that name; and the contacts who are not from the membership
        public IEnumerable<string> LeaderNames => Leaders.Select(l =>
            {
                var contact = draft.Contacts.FirstOrDefault(c => c.MemberId != "" && c.MemberId == l.Id && c.Name.Trim() != "");
                return contact != null && !IsOwnName(contact.Name, l) ? contact.Name.Trim() : l.FirstName;
            })
            .Concat(draft.Contacts.Where(c => c.MemberId == "" || !Leaders.Any(l => l.Id == c.MemberId)).Select(c => c.Name))
            .Where(n => n != "").Distinct(StringComparer.OrdinalIgnoreCase);

        private static bool IsOwnName(string name, TerrainLeader leader) =>
            string.Equals(name.Trim(), leader.FirstName, StringComparison.OrdinalIgnoreCase) || string.Equals(name.Trim(), $"{leader.FirstName} {leader.LastName}".Trim(), StringComparison.OrdinalIgnoreCase);

        protected override async Task OnInitializedAsync()
        {
            if (!_storageService.IsAuthenticated)
            {
                NavigationManager.NavigateTo("index");
                return;
            }

            GroupName = _storageService.GroupNameDisplay;
            Units = _storageService.Units ?? new Dictionary<string, string>();
            // the unit already chosen this session, else the one used here last time, so the program is there again when the page is opened
            var unitId = _storageService.UnitId;
            if (string.IsNullOrEmpty(unitId))
            {
                var last = await LastUnit();
                if (last != "" && Units.ContainsKey(last))
                {
                    unitId = last;
                }
            }
            if (!string.IsNullOrEmpty(unitId))
            {
                await LoadUnit(unitId);
            }
        }

        private const string LastUnitKey = "ProgramBuilder_LastUnit";

        private async Task<string> LastUnit()
        {
            try
            {
                return await _localStorageService.GetItemAsStringAsync(LastUnitKey) ?? "";
            }
            catch (Exception)
            {
                // nothing remembered, or it can't be read back
                return "";
            }
        }

        internal async Task UnitChange(ChangeEventArgs e) => await LoadUnit(e.Value?.ToString() ?? "");

        // The stages of making the program, in order, and the one that is showing
        internal static readonly string[] StageNames = { "Set up", "Plan the Program", "Locations and contacts", "The program" };
        internal int Stage { get; private set; }
        private DotNetObjectReference<ProgramBuilderController>? _dragReference;
        private bool _scrollToStages;
        internal void GoToStage(int stage)
        {
            Stage = Math.Clamp(stage, 0, StageNames.Length - 1);
            _scrollToStages = true;
        }

        // Once the new stage is showing, start it from the top
        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                // the script that picks up ideas and nights reports what was dropped to this page
                _dragReference = DotNetObjectReference.Create(this);
                try
                {
                    await JS.InvokeVoidAsync("ProgramDrag.init", _dragReference);
                }
                catch (Exception)
                {
                    // without it nothing can be dragged, and the rest of the page still works
                }
            }
            if (_scrollToStages)
            {
                _scrollToStages = false;
                await JS.InvokeVoidAsync("ScrollToStages");
            }
        }

        private static string StorageKey(string unitId) => $"ProgramBuilder_{unitId}";

        private async Task LoadUnit(string unitId)
        {
            UnitId = unitId;
            Stage = 0;
            _terrainEvents = null;
            Message = "";
            ErrorMessage = "";
            if (string.IsNullOrEmpty(unitId))
            {
                draft = new ProgramDraft();
                return;
            }
            _storageService.UnitId = unitId;
            try
            {
                await _localStorageService.SetItemAsStringAsync(LastUnitKey, unitId);
            }
            catch (Exception)
            {
                // not being able to remember the unit costs only choosing it again next time
            }

            using var ownScope = CacheScope.Current == null ? CacheScope.Begin(CacheMode.Fresh) : null;
            var people = await _programBuilderService.GetPeople(unitId);
            Patrols = people.Patrols;
            Leaders = people.Leaders;

            ProgramDraft? saved = null;
            try
            {
                saved = await _localStorageService.GetItemAsync<ProgramDraft>(StorageKey(unitId));
            }
            catch (Exception)
            {
                // what was saved can't be read back, which is the same as nothing having been saved
            }
            if (saved != null)
            {
                draft = saved;
                if (ProgramFill.AddLeaderContacts(draft, Leaders) > 0)
                {
                    await Save();
                }
                // a program that has not had its night and times found yet (the calendar could not be read, or it is from before they were) looks for
                // them now, and one that has keeps what was chosen
                if (!draft.UsualFound)
                {
                    await FindUsual();
                }
                AutoFill();
                return;
            }

            draft = NewDraft();
            ProgramFill.AddLeaderContacts(draft, Leaders);
            await Save();
            await FindUsual();
            // a new program starts the duty cycle where Terrain says the last one left off
            await ContinueDuty(automatic: true);
            AutoFill();
        }

        // Reads Terrain and fills the program in from it by itself: on opening a unit, and a moment after the dates are changed. Anything already
        // entered, or already filled in from an activity, is left as it is, so doing it again only picks up what is new.
        private void AutoFill(int delayMilliseconds = 0)
        {
            if (UnitId == "" || draft.Start.Year < 2000 || draft.End < draft.Start)
            {
                return;
            }
            _ = ReadTerrain(fill: true, automatic: true, delayMilliseconds);
        }

        // A program from today for about a term, with every patrol taking turns on duty, keeping what is not about the term from the last: the section
        // night, the usual times, the locations and contacts, and what is shown
        private ProgramDraft NewDraft(ProgramDraft? last = null)
        {
            var start = DateTime.Today;
            return new ProgramDraft
            {
                Title = $"{UnitName} Program {start.Year}", UnitName = UnitName, Section = _storageService.Section,
                Start = start, End = start.AddDays(76), MeetingDay = last?.MeetingDay ?? DayOfWeek.Wednesday, UsualFound = last?.UsualFound ?? false,
                DutyPatrols = new List<string>(Patrols), FirstDutyPatrol = Patrols.FirstOrDefault() ?? "",
                Locations = last?.Locations ?? new List<ProgramLocation>(), Contacts = last?.Contacts ?? new List<ProgramContact>(),
                NightStart = last?.NightStart ?? "19:00", NightEnd = last?.NightEnd ?? "21:00", CampStart = last?.CampStart ?? "18:00", CampEnd = last?.CampEnd ?? "10:00",
                OtherStart = last?.OtherStart ?? "09:00", OtherEnd = last?.OtherEnd ?? "17:00",
                ShowGame = last?.ShowGame ?? false, ShowLocation = last?.ShowLocation ?? true, ShowChallengeArea = last?.ShowChallengeArea ?? false,
                ShowDutyPatrol = last?.ShowDutyPatrol ?? true, ShowLeader = last?.ShowLeader ?? true,
                ShowLocations = last?.ShowLocations ?? true, ShowContacts = last?.ShowContacts ?? true
            };
        }

        internal async Task Save()
        {
            if (string.IsNullOrEmpty(UnitId))
            {
                return;
            }
            draft.UnitName = UnitName;
            draft.Section = _storageService.Section;
            // a place goes into the list of locations once something in the program is there, however it got there
            ProgramFill.AddProgramLocations(draft);
            try
            {
                await _localStorageService.SetItemAsync(StorageKey(UnitId), draft);
            }
            catch (Exception)
            {
                ErrorMessage = "Couldn't save the program on this device.";
            }
        }

        // Starts again: the dates, ideas, activities, camps, hikes, breaks and duty cycle go, and the section night, usual times, locations, contacts and
        // what is shown are kept
        internal async Task ClearClick()
        {
            if (!await JS.InvokeAsync<bool>("confirm", "Clear the program? The dates, ideas, activities, camps, hikes and breaks are removed; the section night, usual times, locations, contacts and what is shown are kept."))
            {
                return;
            }

            // the duty cycle carries on from where this program ended
            var next = ProgramDuty.NextAfterProgram(draft);
            draft = NewDraft(draft);
            if (next != "")
            {
                draft.FirstDutyPatrol = next;
            }
            _terrainEvents = null;
            Message = "Cleared.";
            ErrorMessage = "";
            await Save();
        }

        // Takes the order the patrols have been on duty in, and who is next, from the duty patrols written in the activities Terrain has before the
        // program: the last four months, and when there is none there, the year before that
        internal async Task ContinueDutyClick() => await ContinueDuty(automatic: false);

        private async Task ContinueDuty(bool automatic)
        {
            try
            {
                var events = await _programBuilderService.GetTerrainEvents(UnitId, draft.Start.AddDays(-120), draft.Start.AddDays(-1)) ?? new List<GetEventResultModel>();
                var cycle = ProgramDuty.CycleFrom(events, draft.Start, draft.DutyPatrols);
                if (cycle == null)
                {
                    var older = await _programBuilderService.GetTerrainEvents(UnitId, draft.Start.AddDays(-485), draft.Start.AddDays(-121));
                    cycle = ProgramDuty.CycleFrom(events.Concat(older ?? new List<GetEventResultModel>()), draft.Start, draft.DutyPatrols);
                }
                if (cycle == null)
                {
                    if (!automatic)
                    {
                        Message = "No duty patrol is written in the activities Terrain has before the program, so the cycle starts as it was.";
                    }
                    return;
                }
                draft.DutyPatrols = cycle.Order;
                draft.FirstDutyPatrol = cycle.First;
                Message = $"The duty cycle carries on from Terrain: {string.Join(", ", cycle.Order)}, with {cycle.First} first on duty.";
                await Save();
            }
            catch (Exception) when (automatic)
            {
                // starting the cycle from the beginning is what happens without it
            }
        }

        // Finds the section night and the usual times from the last few months of the calendar, when a unit is opened. What it can't tell is left as it was.
        // Nothing is said of a calendar that is missing or can't be read: it is tried again the next time the unit is opened.
        private async Task FindUsual()
        {
            Busy = true;
            Progress = "the unit's calendar";
            try
            {
                var usual = await _programBuilderService.FindUsual(UnitId);
                if (usual == null)
                {
                    return;
                }

                var found = new List<string>();
                if (usual.Day != null)
                {
                    draft.MeetingDay = usual.Day.Value;
                    found.Add($"the section meets on a {usual.Day.Value}");
                }
                if (usual.NightStart != "" && usual.NightEnd != "")
                {
                    draft.NightStart = usual.NightStart;
                    draft.NightEnd = usual.NightEnd;
                    found.Add($"nights run {usual.NightStart} to {usual.NightEnd}");
                }
                if (usual.CampStart != "" && usual.CampEnd != "")
                {
                    draft.CampStart = usual.CampStart;
                    draft.CampEnd = usual.CampEnd;
                    found.Add($"camps {usual.CampStart} to {usual.CampEnd}");
                }
                if (usual.OtherStart != "" && usual.OtherEnd != "")
                {
                    draft.OtherStart = usual.OtherStart;
                    draft.OtherEnd = usual.OtherEnd;
                    found.Add($"other activities {usual.OtherStart} to {usual.OtherEnd}");
                }
                if (found.Count == 0)
                {
                    Message = "Couldn't tell which night the section meets or its times, from the calendar, so choose them.";
                }
                else
                {
                    Message = $"From the calendar: {string.Join(", ", found)}.";
                    draft.UsualFound = true;
                    await Save();
                }
            }
            catch (Exception)
            {
                // tried again the next time the unit is opened
            }
            finally
            {
                Busy = false;
                Progress = "";
            }
        }

        // Reads what Terrain has between the dates, and fills the program from it when asked to
        private async Task ReadTerrain(bool fill, bool automatic = false, int delayMilliseconds = 0)
        {
            await _auto.RunAsync(async cancellationToken =>
            {
                ErrorMessage = "";
                Message = "";
                Busy = true;
                await InvokeAsync(StateHasChanged);
                using var ownScope = CacheScope.Current == null ? CacheScope.Begin(CacheMode.Fresh) : null;
                var scope = CacheScope.Current!;
                try
                {
                    // the youth's proposals are offered as ideas whether or not the unit has a calendar to read; they are asked for together with the events
                    // as neither needs the other
                    var proposalsTask = fill ? _programBuilderService.GetProposals(UnitId) : Task.FromResult<List<ProposedActivityModel>?>(null);
                    var eventsTask = _programBuilderService.GetTerrainEvents(UnitId, draft.Start, draft.End,
                        step => { if (!cancellationToken.IsCancellationRequested) { Progress = step; InvokeAsync(StateHasChanged); } }, cancellationToken);
                    await Task.WhenAll(proposalsTask, eventsTask);
                    var events = eventsTask.Result;
                    var ideasAdded = proposalsTask.Result == null ? 0 : ProgramIdeas.AddProposals(draft, proposalsTask.Result);
                    var ideasNote = ideasAdded == 0 ? "" : $" {ideasAdded} {(ideasAdded == 1 ? "idea" : "ideas")} from the youth's proposals.";
                    cancellationToken.ThrowIfCancellationRequested();
                    if (events == null)
                    {
                        // not worth an error when nobody asked for it: a unit may well have no calendar of its own
                        var none = "There is no calendar named after this unit, so there is nothing to read from Terrain.";
                        if (automatic)
                        {
                            Message = none + ideasNote;
                        }
                        else
                        {
                            ErrorMessage = none;
                        }
                        if (ideasAdded > 0)
                        {
                            await Save();
                        }
                        return;
                    }
                    _terrainEvents = events;
                    ReadUtc = scope.OldestUtc;
                    if (fill)
                    {
                        var (filled, added) = ProgramFill.Apply(draft, events);
                        var places = ProgramFill.AddProgramLocations(draft);
                        Message = filled + added + places == 0
                            ? "Nothing new in Terrain to fill in." + ideasNote
                            : $"Filled in {filled} section {(filled == 1 ? "night" : "nights")}, added {added} {(added == 1 ? "activity" : "activities")} and {places} {(places == 1 ? "location" : "locations")} from Terrain." + ideasNote;
                        await Save();
                    }
                    else
                    {
                        Message = $"Terrain has {events.Count(e => e.start_datetime.Date >= draft.Start.Date && e.start_datetime.Date <= draft.End.Date)} activities between the dates.";
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    ErrorMessage = "Couldn't read the activities from Terrain.";
                }
                finally
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        Busy = false;
                        Progress = "";
                        await InvokeAsync(StateHasChanged);
                    }
                }
            }, delayMilliseconds);
        }

        internal async Task RefreshFromTerrainClick()
        {
            // asked for, so everything is read from Terrain again
            using (CacheScope.Begin(CacheMode.Reread))
            {
                await ReadTerrain(fill: true);
            }
        }

        // The dates are taken only when whole, as a date box gives a part of one at a time while it is typed
        internal async Task SetStart(ChangeEventArgs e)
        {
            if (!TryDate(e.Value, out var start))
            {
                return;
            }
            draft.Start = start;
            await Save();
            AutoFill(800);
        }

        internal async Task SetEnd(ChangeEventArgs e)
        {
            if (!TryDate(e.Value, out var end))
            {
                return;
            }
            draft.End = end;
            await Save();
            AutoFill(800);
        }

        // ---- the set up

        internal async Task AddBreak()
        {
            var from = draft.End.AddDays(-14);
            draft.Breaks.Add(new ProgramBreak { From = from, To = from.AddDays(13), Label = "Term Break" });
            await Save();
        }

        internal async Task RemoveBreak(ProgramBreak item)
        {
            draft.Breaks.Remove(item);
            await Save();
        }

        internal IEnumerable<string> PatrolsNotInCycle => Patrols.Where(p => !draft.DutyPatrols.Contains(p, StringComparer.OrdinalIgnoreCase));

        internal async Task AddDutyPatrol(ChangeEventArgs e)
        {
            var name = e.Value?.ToString() ?? "";
            if (name != "" && !draft.DutyPatrols.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                draft.DutyPatrols.Add(name);
            }
            if (draft.FirstDutyPatrol == "")
            {
                draft.FirstDutyPatrol = name;
            }
            await Save();
        }

        // The colour named in a patrol's name, like "Drop Bear (Green)", as a stripe on its chip; only a plain word is used
        internal static string DutyChipStyle(string patrol)
        {
            var open = patrol.LastIndexOf('(');
            var close = patrol.LastIndexOf(')');
            if (open < 0 || close != patrol.Length - 1 || close - open < 2)
            {
                return "";
            }
            var colour = patrol.Substring(open + 1, close - open - 1).Trim();
            return colour.All(char.IsLetter) ? "border-left-color: " + colour : "";
        }

        internal async Task RemoveDutyPatrol(string name)
        {
            draft.DutyPatrols.Remove(name);
            if (draft.FirstDutyPatrol == name)
            {
                draft.FirstDutyPatrol = draft.DutyPatrols.FirstOrDefault() ?? "";
            }
            await Save();
        }

        // The patrol being dragged in the duty cycle, and the chip it is over
        private string? _draggedPatrol;
        public string? DutyOver { get; private set; }

        internal void DragDutyPatrol(string name)
        {
            _draggedPatrol = name;
            DutyOver = null;
        }

        internal void OverDutyPatrol(string name)
        {
            if (_draggedPatrol != null && _draggedPatrol != name)
            {
                DutyOver = name;
            }
        }

        internal void EndDutyDrag()
        {
            _draggedPatrol = null;
            DutyOver = null;
        }

        // Dropping a patrol on another puts it in that one's place in the cycle; on the empty space after the last, at the end
        internal async Task DropDutyPatrol(string? onto)
        {
            var name = _draggedPatrol;
            EndDutyDrag();
            if (name == null || name == onto)
            {
                return;
            }
            var at = draft.DutyPatrols.IndexOf(name);
            if (at < 0)
            {
                return;
            }
            // where it ends up is where the other one was, whichever way it moves
            var to = onto == null ? draft.DutyPatrols.Count - 1 : draft.DutyPatrols.IndexOf(onto);
            if (to < 0)
            {
                return;
            }
            draft.DutyPatrols.RemoveAt(at);
            draft.DutyPatrols.Insert(to, name);
            await Save();
        }

        // A time box holds its time as text, so it is set by hand
        internal Task SetTime(Action<string> set, ChangeEventArgs e)
        {
            set(e.Value?.ToString() ?? "");
            return Save();
        }

        // ---- the nights

        // What is filled in for a night, made when it is first wanted so the page can fill it in
        public ProgramRow EditFor(DateTime date)
        {
            var key = ProgramPlanner.NightKey(date);
            if (!draft.NightEdits.TryGetValue(key, out var row))
            {
                draft.NightEdits[key] = row = new ProgramRow { Date = date.Date };
            }
            return row;
        }

        public static string DutyChoice(ProgramRow edit) => edit.NoDuty ? NoDutyChoice : edit.DutyPatrol;

        // A click moves a night on to the next choice: the cycle's own patrol, each patrol in the cycle in turn, then no duty, then round again
        internal async Task CycleDuty(ProgramRow edit)
        {
            var choices = new List<string> { "" };
            choices.AddRange(draft.DutyPatrols);
            choices.Add(NoDutyChoice);
            await ChooseDuty(edit, choices[(choices.IndexOf(DutyChoice(edit)) + 1) % choices.Count]);
        }

        internal async Task ChooseDuty(ProgramRow edit, string choice)
        {
            edit.NoDuty = choice == NoDutyChoice;
            edit.DutyPatrol = edit.NoDuty ? "" : choice;
            CloseMenu();
            await Save();
        }

        // A click moves a row on to the next of the locations entered: the usual one, then each in turn, then round again
        internal async Task CycleLocation(ProgramRow row)
        {
            var choices = new List<string> { "" };
            choices.AddRange(draft.Locations.Select(l => l.Name.Trim()).Where(n => n != "").Distinct(StringComparer.OrdinalIgnoreCase));
            var at = choices.FindIndex(c => string.Equals(c, row.Location, StringComparison.OrdinalIgnoreCase));
            await ChooseLocation(row, choices[(at + 1) % choices.Count]);
        }

        internal async Task ChooseLocation(ProgramRow row, string name)
        {
            row.Location = name;
            CloseMenu();
            await Save();
        }

        // A click moves a row on to the next of the leaders (those from Terrain and the contacts): the usual one, then each in turn, then round again
        internal async Task CycleLeader(ProgramRow row)
        {
            var choices = new List<string> { "" };
            choices.AddRange(LeaderNames);
            var at = choices.FindIndex(c => string.Equals(c, row.Leader, StringComparison.OrdinalIgnoreCase));
            await ChooseLeader(row, choices[(at + 1) % choices.Count]);
        }

        internal async Task ChooseLeader(ProgramRow row, string name)
        {
            row.Leader = name;
            CloseMenu();
            await Save();
        }

        // The challenge areas as Terrain names them
        internal static readonly string[] AreaKeys = { "outdoors", "community", "personal_growth", "creative" };

        // A click moves a row on to the next challenge area: the program's usual one, then each in turn, then round again
        internal async Task CycleArea(ProgramRow row)
        {
            var choices = new List<string> { "" };
            choices.AddRange(AreaKeys);
            var at = choices.FindIndex(c => string.Equals(c, row.ChallengeArea, StringComparison.OrdinalIgnoreCase));
            await ChooseArea(row, choices[(at + 1) % choices.Count]);
        }

        internal async Task ChooseArea(ProgramRow row, string key)
        {
            row.ChallengeArea = key;
            CloseMenu();
            await Save();
        }

        // The parts of the Scout Method an activity uses: any number can be ticked, so the list stays open while they are
        internal async Task ToggleMethod(ProgramRow row, string key)
        {
            row.ScoutMethod = ProgramScoutMethod.Toggle(row.ScoutMethod, key);
            await Save();
        }

        internal async Task ClearMethod(ProgramRow row)
        {
            row.ScoutMethod = new List<string>();
            await Save();
        }

        // The list of choices opened by a right click on a night's duty patrol or a row's location, for the row and kind ("duty", "location", "leader", "area" or "method") it is open for
        public ProgramRow? MenuRow { get; private set; }
        public string MenuKind { get; private set; } = "";

        internal void OpenMenu(ProgramRow row, string kind)
        {
            MenuRow = row;
            MenuKind = kind;
        }

        internal void CloseMenu()
        {
            MenuRow = null;
            MenuKind = "";
        }

        public IEnumerable<string> LocationChoices => draft.Locations.Select(l => l.Name.Trim()).Where(n => n != "").Distinct(StringComparer.OrdinalIgnoreCase);

        // ---- the ideas, dragged onto a night, from one night to another, or back

        // The idea being typed in
        public ProgramIdea NewIdea = new ProgramIdea();

        internal async Task AddIdea()
        {
            if (NewIdea.Title.Trim() == "")
            {
                return;
            }
            NewIdea.Title = NewIdea.Title.Trim();
            draft.Ideas.Add(NewIdea);
            NewIdea = new ProgramIdea { ChallengeArea = NewIdea.ChallengeArea };
            await Save();
        }

        internal async Task RemoveIdea(ProgramIdea idea)
        {
            draft.Ideas.Remove(idea);
            await Save();
        }

        // Told by the script (js/programBuilderDrag.js) when something is put down: an idea or what is on a night (kind "idea" or "night", and its
        // id or date), onto a night (its date) or back among the ideas
        [JSInvokable]
        public async Task DropDragged(string kind, string key, string onto, string to)
        {
            var done = false;
            if (onto == "night" && TryNight(to, out var night))
            {
                if (kind == "idea")
                {
                    done = ProgramIdeas.Assign(draft, key, night);
                }
                else if (kind == "night" && TryNight(key, out var from))
                {
                    done = ProgramIdeas.Move(draft, from, night);
                }
            }
            else if (onto == "ideas" && kind == "night" && TryNight(key, out var taken))
            {
                done = ProgramIdeas.Unassign(draft, taken);
            }
            if (done)
            {
                await Save();
            }
            StateHasChanged();
        }

        private static bool TryNight(string key, out DateTime night) =>
            DateTime.TryParseExact(key, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out night);

        // ---- camps, hikes and the rest

        internal async Task AddExtra(ProgramRowKind kind)
        {
            // with no date, so it waits at the top of the plan until it is given one
            draft.Extras.Add(new ProgramRow { Kind = kind });
            await Save();
        }

        // A date box gives a date a part at a time while it is typed (the year 0002 on the way to 2026), so only a whole date in a sensible year is taken
        private static bool TryDate(object? value, out DateTime date) =>
            DateTime.TryParseExact(value?.ToString(), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out date)
            && date.Year >= 2000 && date.Year <= 2100;

        // Moving the first day carries the last day with it, so a camp keeps its length
        internal async Task SetExtraFrom(ProgramRow extra, ChangeEventArgs e)
        {
            if (!TryDate(e.Value, out var from))
            {
                return;
            }
            // a camp given its first day gets the usual three days, to be changed
            if (!ProgramPlanner.IsDated(extra) && extra.Kind == ProgramRowKind.Camp && extra.EndDate == null)
            {
                extra.EndDate = from.AddDays(2);
            }
            else if (extra.EndDate != null && ProgramPlanner.IsDated(extra))
            {
                var length = (extra.EndDate.Value.Date - extra.Date.Date).Days;
                extra.EndDate = from.AddDays(length is >= 0 and <= 366 ? length : 0);
            }
            extra.Date = from;
            await Save();
        }

        // The last day is never before the first; blank for a single day
        internal async Task SetExtraTo(ProgramRow extra, ChangeEventArgs e)
        {
            if (!ProgramPlanner.IsDated(extra))
            {
                return;
            }
            if (string.IsNullOrEmpty(e.Value?.ToString()))
            {
                extra.EndDate = null;
            }
            else if (TryDate(e.Value, out var to))
            {
                extra.EndDate = to < extra.Date.Date ? extra.Date.Date : to;
            }
            else
            {
                return;
            }
            await Save();
        }

        internal async Task RemoveExtra(ProgramRow row)
        {
            draft.Extras.Remove(row);
            await Save();
        }

        // ---- locations and contacts

        // A place as it is in the list of locations if it is there already (whatever the capitals), else as typed. It goes into the list when the
        // row it is for is in the program (see Save).
        internal string LocationNamed(string name)
        {
            name = (name ?? "").Trim();
            return draft.Locations.FirstOrDefault(l => string.Equals(l.Name.Trim(), name, StringComparison.OrdinalIgnoreCase))?.Name.Trim() ?? name;
        }

        // The place being typed in the list of locations that a right click opens
        public string NewLocationName = "";

        internal async Task AddLocationTo(ProgramRow row)
        {
            if (NewLocationName.Trim() == "")
            {
                return;
            }
            var name = LocationNamed(NewLocationName);
            NewLocationName = "";
            await ChooseLocation(row, name);
        }

        internal async Task AddLocation()
        {
            draft.Locations.Add(new ProgramLocation());
            await Save();
        }

        internal async Task RemoveLocation(ProgramLocation item)
        {
            draft.Locations.Remove(item);
            await Save();
        }

        // Renaming a contact renames the leader in charge of everything that leader is in charge of; a leader from the membership is known on those by
        // first name, so that counts as the old name too. Clearing the name leaves them as they are.
        internal async Task RenameContact(ProgramContact contact, ChangeEventArgs e)
        {
            var oldName = contact.Name.Trim();
            var newName = (e.Value?.ToString() ?? "").Trim();
            contact.Name = newName;
            if (newName != "" && !string.Equals(oldName, newName, StringComparison.Ordinal))
            {
                var olds = new List<string> { oldName };
                var leader = contact.MemberId == "" ? null : Leaders.FirstOrDefault(l => l.Id == contact.MemberId);
                if (leader != null)
                {
                    olds.Add(leader.FirstName);
                }
                ProgramLeaders.Rename(draft, olds, newName);
            }
            await Save();
        }

        internal async Task AddContact()
        {
            draft.Contacts.Add(new ProgramContact());
            await Save();
        }

        internal async Task RemoveContact(ProgramContact item)
        {
            draft.Contacts.Remove(item);
            await Save();
        }

        // ---- the downloads

        private string FileStem => UnitName.Replace(' ', '_');

        internal async Task WordClick()
        {
            var bytes = ProgramDocx.Create(Document);
            await JS.InvokeVoidAsync("BlazorDownloadFile", $"Program_{FileStem}.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", bytes);
        }

        internal async Task Download(OutputType outputType)
        {
            var report = await _reportService.GetProgramCalendarReport(_storageService.GroupName ?? "Group Name", _storageService.Section, UnitName, outputType, JsonConvert.SerializeObject(Document));
            var isPdf = outputType == OutputType.PDF;
            await JS.InvokeVoidAsync("BlazorDownloadFile", $"Program_{FileStem}" + (isPdf ? ".pdf" : ".xlsx"), isPdf ? "application/pdf" : "application/vnd.ms-excel", report);
        }

        private TerrainContext Context() => new TerrainContext { UnitId = UnitId, UnitName = UnitName, Leaders = Leaders, UserName = _storageService.MemberName ?? "" };

        // ---- adding the program to the unit's calendar in Terrain: first read Terrain again and show what would be added (nothing is sent yet), then
        // send it only when the leader confirms. A youth member's login can't add activities, so they aren't offered it.
        public bool PushDialogOpen { get; private set; }
        public bool PushBusy { get; private set; }
        public string PushProgress { get; private set; } = "";
        public ProgramPushPlan? PushPlan { get; private set; }
        public ProgramPushResult? PushResult { get; private set; }

        internal bool CanPush => !_storageService.IsYouthMember && UnitId != "" && !Busy && !PushBusy;

        internal async Task PushToTerrainClick()
        {
            PushBusy = true;
            PushResult = null;
            PushPlan = null;
            PushProgress = "Checking Terrain";
            PushDialogOpen = true;
            try
            {
                PushPlan = await _programBuilderService.PreparePush(draft, Context(), step => { PushProgress = step; InvokeAsync(StateHasChanged); });
            }
            catch (Exception)
            {
                PushPlan = new ProgramPushPlan { Error = "Couldn't read the unit's calendar from Terrain, so nothing can be added." };
            }
            finally
            {
                PushBusy = false;
            }
        }

        // Cancels an activity that is in Terrain, after asking: it goes from the unit's calendar for everyone. The program keeps the night or activity, now
        // with nothing in Terrain behind it.
        internal async Task DeleteFromTerrainClick(ProgramRow row)
        {
            if (row.TerrainEventId == "" || _storageService.IsYouthMember)
            {
                return;
            }
            if (!await JS.InvokeAsync<bool>("confirm", $"Delete \"{row.Activity}\" ({row.Date:ddd d MMM}) from the unit's calendar in Terrain? It is removed there for everyone, and this can't be undone from here."))
            {
                return;
            }

            ErrorMessage = "";
            var result = await _programBuilderService.DeleteEvent(row.TerrainEventId);
            if (!result.Success)
            {
                ErrorMessage = $"\"{row.Activity}\" was not deleted from Terrain: {result.Message}.";
                return;
            }
            row.TerrainEventId = "";
            Message = $"\"{row.Activity}\" was deleted from the unit's calendar in Terrain.";
            await Save();
            _ = ReadTerrain(fill: false);
        }

        internal void CancelPushClick()
        {
            PushDialogOpen = false;
            PushPlan = null;
            PushResult = null;
        }

        internal async Task ConfirmPushClick()
        {
            if (PushPlan == null || !PushPlan.HasWork || PushBusy)
            {
                return;
            }

            PushBusy = true;
            try
            {
                PushResult = await _programBuilderService.Push(PushPlan, draft, Context(), step => { PushProgress = step; InvokeAsync(StateHasChanged); });
            }
            finally
            {
                PushBusy = false;
                // what went into Terrain is now filled in from it, so it is not added again; and what Terrain has is read again for the changes to make
                await Save();
                _ = ReadTerrain(fill: false);
            }
        }
    }
}
