using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Newtonsoft.Json;
using System.Text;
using Topo.Model.Common;
using Topo.Model.Program;
using Topo.Model.ProgramBuilder;
using Topo.Model.ReportGeneration;
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
        public string Progress = "";
        public string ErrorMessage = "";
        public string Message = "";

        // What Terrain has for the program's dates, once it has been read, to fill the program from and to work out the changes to make
        private List<GetEventResultModel>? _terrainEvents;
        private readonly AutoRun _auto = new AutoRun();

        public void Dispose() => _auto.Dispose();

        public ProgramDocument Document => ProgramLayout.Build(draft);
        public List<ProgramRow> Nights => ProgramPlanner.Nights(draft);
        public bool TerrainRead => _terrainEvents != null;
        public List<ProgramChange> Changes => _terrainEvents == null ? new List<ProgramChange>() : ProgramTerrain.Changes(draft, _terrainEvents);
        public List<TerrainEvent> ToAdd => ProgramTerrain.ToAdd(draft, _terrainEvents);
        public string UnitName => Units.TryGetValue(UnitId, out var name) ? name : "";
        public IEnumerable<string> LeaderNames => Leaders.Select(l => l.FirstName).Concat(draft.Contacts.Select(c => c.Name)).Where(n => n != "").Distinct(StringComparer.OrdinalIgnoreCase);

        protected override async Task OnInitializedAsync()
        {
            if (!_storageService.IsAuthenticated)
            {
                NavigationManager.NavigateTo("index");
                return;
            }

            GroupName = _storageService.GroupNameDisplay;
            Units = _storageService.Units ?? new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(_storageService.UnitId))
                await LoadUnit(_storageService.UnitId);
        }

        internal async Task UnitChange(ChangeEventArgs e) => await LoadUnit(e.Value?.ToString() ?? "");

        private static string StorageKey(string unitId) => $"ProgramBuilder_{unitId}";

        private async Task LoadUnit(string unitId)
        {
            UnitId = unitId;
            _terrainEvents = null;
            Message = "";
            ErrorMessage = "";
            if (string.IsNullOrEmpty(unitId))
            {
                draft = new ProgramDraft();
                return;
            }
            _storageService.UnitId = unitId;

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
                return;
            }

            draft = NewDraft();
            await Save();
            await FindSectionNight();
        }

        // A program from today for about a term, with every patrol taking turns on duty, keeping what is not about the term from the last: the section
        // night, the usual times, the locations and contacts, and what is shown
        private ProgramDraft NewDraft(ProgramDraft? last = null)
        {
            var start = DateTime.Today;
            return new ProgramDraft
            {
                Title = $"{UnitName} Program {start.Year}", UnitName = UnitName, Section = _storageService.Section,
                Start = start, End = start.AddDays(76), MeetingDay = last?.MeetingDay ?? DayOfWeek.Wednesday,
                DutyPatrols = new List<string>(Patrols), FirstDutyPatrol = Patrols.FirstOrDefault() ?? "",
                Locations = last?.Locations ?? new List<ProgramLocation>(), Contacts = last?.Contacts ?? new List<ProgramContact>(),
                NightStart = last?.NightStart ?? "19:00", NightEnd = last?.NightEnd ?? "21:00", CampStart = last?.CampStart ?? "18:00", CampEnd = last?.CampEnd ?? "10:00",
                OtherStart = last?.OtherStart ?? "09:00", OtherEnd = last?.OtherEnd ?? "17:00",
                ShowGame = last?.ShowGame ?? true, ShowLocation = last?.ShowLocation ?? true, ShowChallengeArea = last?.ShowChallengeArea ?? false,
                ShowDutyPatrol = last?.ShowDutyPatrol ?? true, ShowLeader = last?.ShowLeader ?? true,
                ShowLocations = last?.ShowLocations ?? true, ShowContacts = last?.ShowContacts ?? true
            };
        }

        internal async Task Save()
        {
            if (string.IsNullOrEmpty(UnitId))
                return;
            draft.UnitName = UnitName;
            draft.Section = _storageService.Section;
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
                return;

            draft = NewDraft(draft);
            _terrainEvents = null;
            Message = "Cleared.";
            ErrorMessage = "";
            await Save();
        }

        internal async Task FindSectionNight()
        {
            Busy = true;
            Progress = "the unit's calendar";
            try
            {
                var day = await _programBuilderService.FindSectionNight(UnitId);
                if (day == null)
                    Message = "Couldn't tell which night the section meets, from the calendar named after the unit, so choose it.";
                else
                {
                    draft.MeetingDay = day.Value;
                    Message = $"The section meets on a {day.Value}.";
                    await Save();
                }
            }
            catch (Exception)
            {
                ErrorMessage = "Couldn't read the unit's calendar from Terrain.";
            }
            finally
            {
                Busy = false;
                Progress = "";
            }
        }

        // Reads what Terrain has between the dates, and fills the program from it when asked to
        private async Task ReadTerrain(bool fill)
        {
            ErrorMessage = "";
            Message = "";
            await _auto.RunAsync(async cancellationToken =>
            {
                Busy = true;
                try
                {
                    var events = await _programBuilderService.GetTerrainEvents(UnitId, draft.Start, draft.End,
                        step => { if (!cancellationToken.IsCancellationRequested) { Progress = step; InvokeAsync(StateHasChanged); } }, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (events == null)
                    {
                        ErrorMessage = "There is no calendar named after this unit, so there is nothing to read from Terrain.";
                        return;
                    }
                    _terrainEvents = events;
                    if (fill)
                    {
                        var (filled, added) = ProgramFill.Apply(draft, events);
                        Message = $"Filled in {filled} section {(filled == 1 ? "night" : "nights")} and added {added} {(added == 1 ? "activity" : "activities")} from Terrain.";
                        await Save();
                    }
                    else
                        Message = $"Terrain has {events.Count(e => e.start_datetime.Date >= draft.Start.Date && e.start_datetime.Date <= draft.End.Date)} activities between the dates.";
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
                    }
                }
            });
        }

        internal async Task FillFromTerrainClick() => await ReadTerrain(fill: true);

        internal async Task CheckTerrainClick() => await ReadTerrain(fill: false);

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
                draft.DutyPatrols.Add(name);
            if (draft.FirstDutyPatrol == "")
                draft.FirstDutyPatrol = name;
            await Save();
        }

        // The colour named in a patrol's name, like "Drop Bear (Green)", as a stripe on its chip; only a plain word is used
        internal static string DutyChipStyle(string patrol)
        {
            var open = patrol.LastIndexOf('(');
            var close = patrol.LastIndexOf(')');
            if (open < 0 || close != patrol.Length - 1 || close - open < 2)
                return "";
            var colour = patrol.Substring(open + 1, close - open - 1).Trim();
            return colour.All(char.IsLetter) ? "border-left-color: " + colour : "";
        }

        internal async Task RemoveDutyPatrol(string name)
        {
            draft.DutyPatrols.Remove(name);
            if (draft.FirstDutyPatrol == name)
                draft.FirstDutyPatrol = draft.DutyPatrols.FirstOrDefault() ?? "";
            await Save();
        }

        internal async Task MoveDutyPatrol(string name, int by)
        {
            var at = draft.DutyPatrols.IndexOf(name);
            var to = at + by;
            if (at < 0 || to < 0 || to >= draft.DutyPatrols.Count)
                return;
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
                draft.NightEdits[key] = row = new ProgramRow { Date = date.Date };
            return row;
        }

        public static string DutyChoice(ProgramRow edit) => edit.NoDuty ? NoDutyChoice : edit.DutyPatrol;

        internal async Task SetDutyChoice(ProgramRow edit, ChangeEventArgs e)
        {
            var choice = e.Value?.ToString() ?? "";
            edit.NoDuty = choice == NoDutyChoice;
            edit.DutyPatrol = edit.NoDuty ? "" : choice;
            await Save();
        }

        // ---- the ideas, dragged onto a night, from one night to another, or back

        // The idea being typed in
        public ProgramIdea NewIdea = new ProgramIdea();
        // What is being dragged: an idea from the list (its id), or what is on a night (its date)
        private (bool FromIdeas, string Key)? _dragged;

        internal async Task AddIdea()
        {
            if (NewIdea.Title.Trim() == "")
                return;
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

        internal void DragIdea(ProgramIdea idea) => _dragged = (true, idea.Id);

        internal void DragNight(DateTime night) => _dragged = (false, ProgramPlanner.NightKey(night));

        internal async Task DropOnNight(DateTime night)
        {
            var dragged = _dragged;
            _dragged = null;
            if (dragged == null)
                return;
            var done = dragged.Value.FromIdeas
                ? ProgramIdeas.Assign(draft, dragged.Value.Key, night)
                : ProgramIdeas.Move(draft, DateTime.ParseExact(dragged.Value.Key, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), night);
            if (done)
                await Save();
        }

        // Dropping what is on a night back among the ideas
        internal async Task DropOnIdeas()
        {
            var dragged = _dragged;
            _dragged = null;
            if (dragged == null || dragged.Value.FromIdeas)
                return;
            if (ProgramIdeas.Unassign(draft, DateTime.ParseExact(dragged.Value.Key, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)))
                await Save();
        }

        // ---- camps, hikes and the rest

        internal async Task AddExtra(ProgramRowKind kind)
        {
            var date = draft.Extras.Count > 0 ? draft.Extras.Max(r => r.Date) : draft.Start;
            draft.Extras.Add(new ProgramRow { Kind = kind, Date = date.Date, EndDate = kind == ProgramRowKind.Camp ? date.Date.AddDays(2) : null });
            await Save();
        }

        internal async Task RemoveExtra(ProgramRow row)
        {
            draft.Extras.Remove(row);
            await Save();
        }

        // ---- locations and contacts

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

        // Plain text built in the browser, as the Patrol Planner's changes are, since it is a flat list
        internal async Task ChangesCsvClick()
        {
            var bytes = Encoding.UTF8.GetBytes(ProgramTerrain.Csv(Changes));
            await JS.InvokeVoidAsync("BlazorDownloadFile", $"Program_Changes_{FileStem}.csv", "text/csv", bytes);
        }

        // An activity to a file, as Terrain's import takes them, for those not in Terrain
        internal async Task ImportZipClick()
        {
            var context = new TerrainContext { UnitId = UnitId, UnitName = UnitName, Leaders = Leaders };
            var bytes = ProgramTerrain.Zip(ToAdd, context);
            await JS.InvokeVoidAsync("BlazorDownloadFile", $"Terrain_Activities_{FileStem}.zip", "application/zip", bytes);
        }
    }
}
