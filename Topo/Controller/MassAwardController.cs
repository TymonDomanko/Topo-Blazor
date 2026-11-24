using Microsoft.AspNetCore.Components;
using Syncfusion.Blazor.Calendars;
using Topo.Model.AdditionalAwards;
using Topo.Model.Approvals;
using Topo.Services;

namespace Topo.Controller
{
    public class MassAwardController : ComponentBase
    {
        [Inject]
        public StorageService _storageService { get; set; } = default!;

        [Inject]
        public IApprovalsService _approvalsService { get; set; } = default!;

        [Inject]
        public IProgramService _programService { get; set; } = default!;

        [Inject]
        public IAdditionalAwardCheckService _additionalAwards { get; set; } = default!;

        [Inject]
        public NavigationManager NavigationManager { get; set; } = default!;

        public string GroupName = string.Empty;
        public Dictionary<string, string> Units = new();
        public string UnitId = string.Empty;

        // null until the pending awards have been read
        public AwardRequestList? Awards;
        // What the page lists: all the pending awards, or only those for people who attended the chosen event
        public AwardRequestList? Shown;
        public AwardSelection? Selection;

        // The day the awards are presented on. It decides which events can be picked to say who was there.
        public DateTime? AwardDate;
        public DateTime Today => DateTime.Today;
        // The earliest day the date can be, and how far back the events are read
        public DateTime EarliestDate => Today.AddMonths(-24);
        // Every event of the unit in that time, the days they were on, and the ones on the chosen day
        private List<AwardEventChoice> _allEvents = new();
        public HashSet<DateTime> EventDates = new();
        public bool EventsRead;
        // An escape for when the unit's calendar is not one of those selected in Terrain, so no day has an event
        public bool AllowAnyDate;
        public List<AwardEventChoice> Events = new();
        public string SelectedEventId = string.Empty;
        public bool EventsLoading;
        public string? EventMessage;

        // Awarding: the leader presses Award, is shown what will be sent, and confirms; nothing is sent before that
        public bool ConfirmOpen;
        public string? AwardMessage;
        public bool IsAwarding;
        public int AwardsTried;
        public int AwardsToSend;
        public AwardBatchResult? LastResult;
        public int StillListed;
        private HashSet<string>? _attended;
        // How many people the chosen event lists as attending, so a short list can be told from an event nobody was ticked on
        public int AttendedCount => _attended?.Count ?? 0;
        public bool IsLoading;
        public string? LoadError;
        // The Camper, Walkabout and Years of Service awards to hand over are listed with the pending awards, as one more badge. They have no
        // award request in Terrain, so awarding one records it instead: a Camper or Walkabout Award in Terrain's additional awards, a Years of
        // Service badge in Topo's records event (comments on an event in Terrain). By the id they are listed under.
        private Dictionary<string, AwardRow> _topoAwards = new();
        public string? TopoNote;

        protected override async Task OnInitializedAsync()
        {
            if (!_storageService.IsAuthenticated || _storageService.IsYouthMember)
            {
                NavigationManager.NavigateTo("index");
                return;
            }

            GroupName = _storageService.GroupNameDisplay;
            Units = _storageService.Units;
            UnitId = _storageService.UnitId;
            if (!string.IsNullOrEmpty(UnitId))
            {
                await RefreshClick();
            }
        }

        internal async Task UnitChange(ChangeEventArgs e)
        {
            UnitId = e.Value?.ToString() ?? "";
            _storageService.UnitId = UnitId;
            Awards = null;
            Shown = null;
            Selection = null;
            LoadError = null;
            if (!string.IsNullOrEmpty(UnitId))
            {
                await RefreshClick();
            }
        }

        // Reads the pending awards from Terrain. Reading again clears what was ticked, since the list may have changed.
        internal async Task RefreshClick()
        {
            if (string.IsNullOrEmpty(UnitId))
            {
                return;
            }
            var unitId = UnitId;
            IsLoading = true;
            LoadError = null;
            try
            {
                var awards = await WithTopoAwardsAsync(unitId, await _approvalsService.GetAwardRequests(unitId));
                // The leader picked another unit while this was loading; that unit's own read decides what is shown
                if (unitId != UnitId)
                {
                    return;
                }
                Awards = awards;
                ShowEveryone();
            }
            catch (Exception)
            {
                if (unitId != UnitId)
                {
                    return;
                }
                Awards = null;
                Shown = null;
                Selection = null;
                LoadError = "Could not read the pending awards from Terrain.";
            }
            finally
            {
                if (unitId == UnitId)
                {
                    IsLoading = false;
                }
            }
            if (unitId == UnitId && Awards != null)
            {
                await LoadEvents();
            }
        }

        // Reads the unit's events for the last two years, so the date picker can block out the days with none
        private async Task LoadEvents()
        {
            var unitId = UnitId;
            EventsLoading = true;
            EventsRead = false;
            _allEvents = new List<AwardEventChoice>();
            EventDates = new HashSet<DateTime>();
            EventMessage = null;
            try
            {
                var events = await _programService.GetEventChoices(EarliestDate, Today, unitId);
                if (unitId != UnitId)
                {
                    return;
                }
                _allEvents = events;
                EventDates = AwardEventChoice.Dates(events);
                EventsRead = true;
                // The leader picks the date themself. One already picked stays, unless it has no event now.
                if (AwardDate != null && !AllowAnyDate && !EventDates.Contains(AwardDate.Value.Date))
                {
                    AwardDate = null;
                }
                if (EventDates.Count == 0)
                {
                    EventMessage = "No events found in the last two years. Events come from the calendars selected in Terrain; tick \"Allow any date\" to choose a day anyway.";
                }
            }
            catch (Exception)
            {
                if (unitId != UnitId)
                {
                    return;
                }
                EventMessage = "Could not read the events from Terrain, so any date can be chosen.";
            }
            finally
            {
                if (unitId == UnitId)
                {
                    EventsLoading = false;
                }
            }
            if (unitId == UnitId)
            {
                await ShowEventsForDate();
            }
        }

        // The picker greys out a day that has no event for the unit
        internal void RenderDay(RenderDayCellEventArgs args)
        {
            if (EventsRead && !AllowAnyDate && !EventDates.Contains(args.Date.Date))
            {
                args.IsDisabled = true;
            }
        }

        // The award date was picked: the day's event is chosen for the leader, and the people who attended are ticked
        internal async Task DateChanged(ChangedEventArgs<DateTime?> args)
        {
            AwardDate = args.Value;
            await ShowEventsForDate();
        }

        internal async Task AllowAnyDateChanged()
        {
            await ShowEventsForDate();
        }

        // Lists the events on the chosen day and chooses the first (earliest) one, which lists and ticks who attended it
        private async Task ShowEventsForDate()
        {
            SelectedEventId = string.Empty;
            Events = AwardDate == null ? new List<AwardEventChoice>() : AwardEventChoice.On(_allEvents, AwardDate.Value);
            ShowEveryone();
            if (Events.Count > 0)
            {
                await SelectEvent(Events[0].Id);
            }
        }

        // The leader picked a different event (or "everyone")
        internal async Task EventChange(ChangeEventArgs e)
        {
            await SelectEvent(e.Value?.ToString() ?? "");
        }

        // List only the people who attended the event, and tick all of their awards. The leader can untick any of them.
        private async Task SelectEvent(string eventId)
        {
            SelectedEventId = eventId;
            EventMessage = null;
            if (string.IsNullOrEmpty(eventId))
            {
                ShowEveryone();
                return;
            }

            EventsLoading = true;
            try
            {
                var attended = await _programService.GetAttendedMemberIds(eventId);
                if (eventId != SelectedEventId)
                {
                    return;
                }
                _attended = attended;
                Shown = Awards?.OnlyMembers(attended);
                Selection = Shown == null ? null : new AwardSelection(Shown);
                Selection?.SelectAll();
                ConfirmOpen = false;
                AwardMessage = null;
            }
            catch (Exception)
            {
                if (eventId != SelectedEventId)
                {
                    return;
                }
                SelectedEventId = string.Empty;
                ShowEveryone();
                EventMessage = "Could not read who attended that event from Terrain.";
            }
            finally
            {
                if (eventId == SelectedEventId || string.IsNullOrEmpty(SelectedEventId))
                {
                    EventsLoading = false;
                }
            }
        }

        // Changing the list clears the ticks, so nothing that has dropped off the page stays ticked
        private void ShowEveryone()
        {
            ConfirmOpen = false;
            AwardMessage = null;
            _attended = null;
            Shown = Awards;
            Selection = Awards == null ? null : new AwardSelection(Awards);
        }

        internal void ToggleAward(string submissionId) => ChangeTicks(() => Selection?.Toggle(submissionId));

        internal void TogglePerson(string memberId) => ChangeTicks(() => Selection?.TogglePerson(memberId));

        internal void SelectAllClick() => ChangeTicks(() => Selection?.SelectAll());

        internal void ClearClick() => ChangeTicks(() => Selection?.Clear());

        // What was ticked is what the confirmation shows, so any change to the ticks closes it
        private void ChangeTicks(Action change)
        {
            if (IsAwarding)
            {
                return;
            }
            change();
            ConfirmOpen = false;
            AwardMessage = null;
        }

        // Checks there is something to award and a good date, then shows what will be sent. Nothing is sent yet.
        internal void AwardClick()
        {
            if (IsAwarding || Selection == null)
            {
                return;
            }
            LastResult = null;
            AwardMessage = null;
            if (Selection.Count == 0)
            {
                AwardMessage = "Tick at least one award first.";
                return;
            }
            AwardMessage = ApprovalAction.Award(AwardDate).Problem("award", Today);
            // A day with no event for the unit cannot be chosen in the picker, but a date can be typed in
            if (AwardMessage == null && EventsRead && !AllowAnyDate && !EventDates.Contains(AwardDate!.Value.Date))
            {
                AwardMessage = "There is no event for the unit on that date.";
            }
            ConfirmOpen = AwardMessage == null;
        }

        internal void CancelConfirmClick()
        {
            if (IsAwarding)
            {
                return;
            }
            ConfirmOpen = false;
        }

        // The leader confirmed: award the ticked requests one after another, then read Terrain's list again
        internal async Task ConfirmAwardClick()
        {
            if (IsAwarding || Selection == null || !ConfirmOpen)
            {
                return;
            }
            var selected = Selection.SelectedAwards();
            var date = AwardDate;
            var unitId = UnitId;
            ConfirmOpen = false;
            IsAwarding = true;
            AwardsTried = 0;
            AwardsToSend = selected.Count;
            try
            {
                var result = await AwardBatch.RunAsync(selected, date, Today,
                    (submissionId, action) => SendAsync(submissionId, action),
                    (tried, total) =>
                    {
                        AwardsTried = tried;
                        AwardsToSend = total;
                        StateHasChanged();
                    });
                LastResult = result;
                AwardMessage = result.Problem;
                if (result.Outcomes.Count > 0)
                {
                    await ReloadAfterAwards(unitId, result);
                }
            }
            finally
            {
                IsAwarding = false;
            }
        }

        // Reads Terrain's pending awards again. Awards that went through drop off; ones that failed stay ticked so the
        // leader can try them again.
        private async Task ReloadAfterAwards(string unitId, AwardBatchResult result)
        {
            try
            {
                var awards = await WithTopoAwardsAsync(unitId, await _approvalsService.GetAwardRequests(unitId));
                if (unitId != UnitId)
                {
                    return;
                }
                // An award that went through but is still pending in Terrain has an awarded response recorded, so it is not listed
                StillListed = result.Done.Count(o => awards.Hidden.Contains(o.Award.Item.SubmissionId));
                Awards = awards;
                Shown = _attended == null ? awards : awards.OnlyMembers(_attended);
                Selection = new AwardSelection(Shown);
                foreach (var failed in result.Failed)
                    Selection.Toggle(failed.Award.Item.SubmissionId);
            }
            catch (Exception)
            {
                AwardMessage = "The awards were sent, but Terrain's pending list could not be read again. Use Read Terrain again to check.";
            }
        }

        // Adds the Camper, Walkabout and Years of Service awards to hand over to the pending awards. If they cannot be worked out the pending awards are still
        // shown, with a note.
        private async Task<AwardRequestList> WithTopoAwardsAsync(string unitId, AwardRequestList awards)
        {
            TopoNote = null;
            _topoAwards = new Dictionary<string, AwardRow>();
            try
            {
                var rows = (await _additionalAwards.PendingAsync(unitId)).Where(r => r.Status == AwardRowStatus.ToBePresented).ToList();
                _topoAwards = rows.ToDictionary(r => PendingApprovalItem.TopoIdPrefix + r.Key);
                return awards.WithAdditionalAwards(rows);
            }
            catch (Exception)
            {
                TopoNote = "The Camper, Walkabout and Years of Service awards could not be worked out, so they are not listed.";
                return awards;
            }
        }

        // One award: a Terrain award request goes to Terrain's approvals, a Camper or Walkabout Award is recorded in Terrain's additional
        // awards (with the lower levels it covers, on the same day). Returns null when it worked, otherwise what went wrong.
        private async Task<string?> SendAsync(string submissionId, ApprovalAction action)
        {
            if (!submissionId.StartsWith(PendingApprovalItem.TopoIdPrefix, StringComparison.Ordinal))
            {
                return await _approvalsService.PerformAction(submissionId, action);
            }
            if (!_topoAwards.TryGetValue(submissionId, out var row))
            {
                return "That award is no longer on the list to hand over.";
            }
            var written = await _additionalAwards.PresentAsync(new[] { row }, action.DateAwarded!.Value);
            if (written.Failures.Count == 0 && written.Done + written.Skipped > 0)
            {
                return null;
            }
            return written.Failures.FirstOrDefault() ?? "It was not recorded.";
        }
    }
}
