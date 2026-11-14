using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Newtonsoft.Json;
using Topo.Model.Common;
using Topo.Model.Members;
using Topo.Model.PatrolPlanner;
using Topo.Model.ReportGeneration;
using Topo.Services;

namespace Topo.Controller
{
    // Backs the Patrol Planner drag-and-drop board. The board is a local, in-memory "what if" plan: nothing
    // reaches Terrain until the leader chooses "Push to Terrain" and confirms the list of changes it shows.
    public class PatrolPlannerController : ComponentBase, IDisposable
    {
        [Inject]
        public IMembersService _membersService { get; set; }

        [Inject]
        public IAttendanceService _attendanceService { get; set; }

        [Inject]
        public SpinnerService _spinnerService { get; set; }

        [Inject]
        public StorageService _storageService { get; set; }

        [Inject]
        public IReportService _reportService { get; set; }

        [Inject]
        public IPatrolPushService _patrolPushService { get; set; }

        [Inject]
        public ILocalStorageService _localStorageService { get; set; }

        [Inject]
        IJSRuntime JS { get; set; }

        [Inject]
        public NavigationManager NavigationManager { get; set; }

        public PatrolPlannerPageViewModel model = new PatrolPlannerPageViewModel();

        // How many Scout nights the attendance figures look back over
        private const int RecentNightsCount = 10;

        public bool Loading;
        public string Progress = "";
        // The attendance is read after the board is up, and again when the unit changes; the one before is stopped
        private readonly AutoRun _auto = new AutoRun();

        public void Dispose() => _auto.Dispose();

        private string? _draggedMemberId;
        private List<string> _originalPatrolNames = new();
        private Dictionary<string, string> _patrolColorOverrides = new();

        protected override async Task OnInitializedAsync()
        {
            if (!_storageService.IsAuthenticated)
                NavigationManager.NavigateTo("index");

            model.Units = _storageService.Units;
            model.GroupName = _storageService.GroupNameDisplay;
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
            _storageService.UnitId = unitId;
            model.UnitName = _storageService.UnitName;
            if (string.IsNullOrEmpty(unitId))
            {
                model.Members = new List<PatrolPlannerMember>();
                model.PatrolNames = new List<string>();
                _originalPatrolNames = new List<string>();
                model.AttendanceNote = "";
                _auto.Stop();
                Loading = false;
                return;
            }

            _patrolColorOverrides = await _localStorageService.GetItemAsync<Dictionary<string, string>>($"{unitId}_PatrolColors") ?? new Dictionary<string, string>();

            var allMembers = await _membersService.GetMembersAsync(unitId);
            model.Members = allMembers
                .Where(m => m.isAdultLeader == 0)
                .Select(m => new PatrolPlannerMember
                {
                    Id = m.id,
                    FirstName = m.first_name,
                    LastName = m.last_name,
                    Age = m.age,
                    UnitCouncil = m.unit_council,
                    OriginalUnitCouncil = m.unit_council,
                    Gender = m.gender,
                    OriginalPatrolName = m.patrol_name,
                    OriginalPatrolDuty = m.patrol_duty,
                    PatrolName = m.patrol_name,
                    PatrolDuty = m.patrol_duty
                })
                .ToList();

            model.PatrolNames = model.Members
                .Select(m => m.OriginalPatrolName)
                .Where(n => !string.IsNullOrEmpty(n))
                .Distinct()
                .OrderBy(n => n)
                .ToList();
            _originalPatrolNames = new List<string>(model.PatrolNames);
            model.AttendanceNote = "";
            // in the background, so the board can be used while it is read
            _ = _auto.RunAsync(LoadAttendance);
        }

        // How many of the last Scout nights each youth came to, from the calendar named after the unit
        private async Task LoadAttendance(CancellationToken cancellationToken)
        {
            // the Reading line says what is happening, so the spinner is not put over the board
            _spinnerService.BeQuiet();
            Loading = true;
            try
            {
                var recent = await _attendanceService.GetRecentNights(model.UnitId, RecentNightsCount,
                    step => { if (!cancellationToken.IsCancellationRequested) { Progress = step; InvokeAsync(StateHasChanged); } }, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (recent == null)
                {
                    model.AttendanceNote = "Attendance isn't shown because there is no calendar named after this unit.";
                    return;
                }
                if (recent.Nights == 0)
                {
                    model.AttendanceNote = "Attendance isn't shown because no Scout nights with attendance were found in the last six months.";
                    return;
                }

                foreach (var member in model.Members)
                {
                    member.NightsCounted = recent.PossibleByMemberId.TryGetValue(member.Id, out var possible) ? possible : recent.Nights;
                    member.NightsAttended = recent.AttendedByMemberId.TryGetValue(member.Id, out var attended) ? attended : 0;
                }
                model.AttendanceNote = $"Attendance is the share of the last {recent.Nights} Scout nights each youth came to, counting only those since they joined the section.";
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                model.AttendanceNote = "Couldn't read the attendance from Terrain.";
            }
            finally
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    Loading = false;
                    Progress = "";
                }
                // nothing is waiting on this, so the page has to be told it has finished
                await InvokeAsync(StateHasChanged);
            }
        }

        internal async Task RefreshClick()
        {
            _membersService.ClearMemberCache(model.UnitId);
            await UnitChange(model.UnitId);
        }

        // Pushing to Terrain: first read Terrain again and show what would be written (nothing is written yet), then
        // write it only when the leader confirms. A youth member's login can't change patrols, so they aren't offered it.
        public bool PushDialogOpen { get; private set; }
        public bool PushBusy { get; private set; }
        public string PushProgress { get; private set; } = "";
        public PatrolPushPlan? PushPlan { get; private set; }
        public PatrolPushResult? PushResult { get; private set; }

        internal bool CanPush => !_storageService.IsYouthMember && HasChanges && !PushBusy;

        internal async Task PushToTerrainClick()
        {
            PushBusy = true;
            PushResult = null;
            PushProgress = "Checking Terrain";
            PushDialogOpen = true;
            PushPlan = null;
            try
            {
                var removedPatrols = _originalPatrolNames.Except(model.PatrolNames);
                PushPlan = await _patrolPushService.PrepareAsync(model.UnitId, model.Members, removedPatrols);
            }
            finally
            {
                PushBusy = false;
            }
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
                return;

            PushBusy = true;
            try
            {
                PushResult = await _patrolPushService.PushAsync(PushPlan, step =>
                {
                    PushProgress = step;
                    InvokeAsync(StateHasChanged);
                });
            }
            finally
            {
                PushBusy = false;
                // Show Terrain as it is now, even if something went wrong part way: what went through is no longer a change,
                // and what didn't can be planned again. The Wallchart keeps its own copy of each unit's patrols, which is now stale.
                _membersService.ClearMemberCache(model.UnitId);
                _storageService.CachedWallchartItems.RemoveAll(c => c.Key == model.UnitId);
                await UnitChange(model.UnitId);
            }
        }

        internal void AddPatrolClick()
        {
            var name = model.NewPatrolName?.Trim() ?? "";
            if (string.IsNullOrEmpty(name))
                return;
            if (!model.PatrolNames.Any(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase)))
            {
                model.PatrolNames.Add(name);
            }
            model.NewPatrolName = "";
        }

        internal void RemovePatrol(string patrolName)
        {
            model.PatrolNames.Remove(patrolName);
            foreach (var member in model.Members.Where(m => m.PatrolName == patrolName))
            {
                member.PatrolName = "";
            }
            // Any saved colour override for this patrol name is deliberately left in local storage:
            // harmless if the patrol is gone for good, and remembered for free if it's re-added later.
        }

        // A Unit Leader still has a patrol_name in Terrain's data (the UL role sits above patrols,
        // not inside them), so they're pulled out into their own banner here rather than appearing
        // in a column. GetColumnMembers excludes them for the same reason.
        internal IEnumerable<PatrolPlannerMember> ULMembers =>
            model.Members.Where(m => m.PatrolDuty == "UL").OrderBy(m => m.FirstName).ThenBy(m => m.LastName);

        internal IEnumerable<string> AllColumnNames
        {
            get
            {
                var columns = new List<string>(model.PatrolNames);
                if (model.Members.Any(m => string.IsNullOrEmpty(m.PatrolName)))
                {
                    columns.Add(string.Empty);
                }
                return columns;
            }
        }

        // PL first, then APL, then everyone else oldest-to-youngest - matches how a patrol is
        // normally listed on paper (leaders at top, members by seniority underneath).
        internal IEnumerable<PatrolPlannerMember> GetColumnMembers(string patrolName)
        {
            return model.Members
                .Where(m => m.PatrolName == patrolName && m.PatrolDuty != "UL")
                .OrderBy(m => m.PatrolDuty == "PL" ? 0 : m.PatrolDuty == "APL" ? 1 : 2)
                .ThenByDescending(m => GetAgeInMonths(m.Age));
        }

        internal IEnumerable<PatrolPlannerMember> ChangedMembers =>
            model.Members.Where(m => m.IsChanged).OrderBy(m => m.FirstName).ThenBy(m => m.LastName);

        internal bool HasChanges => model.Members.Any(m => m.IsChanged);

        private static int GetAgeInMonths(string age)
        {
            // age is formatted like "14y 9m"
            var parts = age.Replace("y", "").Replace("m", "").Split(' ');
            if (parts.Length == 2 && int.TryParse(parts[0], out var years) && int.TryParse(parts[1], out var months))
            {
                return years * 12 + months;
            }
            return 0;
        }

        private static readonly string[] PatrolPalette = new[]
        {
            "#CFE3FF", "#FFE0C2", "#D4F0D0", "#F6D6E0", "#E3D6FF",
            "#FFF3B0", "#C9EFEF", "#E6D3B3", "#D9D9F3", "#FFD9D9"
        };

        // Light tints to use when a patrol is literally named after a colour (e.g. "Red Patrol").
        private static readonly Dictionary<string, string> NamedPatrolColors = new(StringComparer.OrdinalIgnoreCase)
        {
            { "red", "#FFD9D9" },
            { "blue", "#CFE3FF" },
            { "green", "#D4F0D0" },
            { "yellow", "#FFF3B0" },
            { "orange", "#FFE0C2" },
            { "purple", "#E3D6FF" },
            { "violet", "#E3D6FF" },
            { "indigo", "#D9D9F3" },
            { "pink", "#F6D6E0" },
            { "magenta", "#F6D6E0" },
            { "brown", "#E6D3B3" },
            { "tan", "#E6D3B3" },
            { "cyan", "#C9EFEF" },
            { "teal", "#C9EFEF" },
            { "turquoise", "#C9EFEF" },
            { "gold", "#FFE9A8" },
            { "maroon", "#E8C2C2" },
            { "navy", "#C2D4E8" },
            { "black", "#D9D9D9" },
            { "white", "#F5F5F5" },
            { "grey", "#E2E2E2" },
            { "gray", "#E2E2E2" },
            { "silver", "#E8E8E8" }
        };

        // Colour priority: a colour the leader picked for this patrol (saved locally) wins first,
        // then a colour word found in the patrol's own name (e.g. "Drop Bear (Green)"), and only
        // then a stable colour assigned purely by the patrol's position in the alphabetised list.
        internal string GetPatrolColor(string patrolName)
        {
            if (string.IsNullOrEmpty(patrolName))
                return "#E2E2E2";

            if (_patrolColorOverrides.TryGetValue(patrolName, out var overrideColor) && !string.IsNullOrEmpty(overrideColor))
                return overrideColor;

            // Split on brackets/punctuation too, since real patrol names often read "Name (Colour)".
            var words = patrolName.Split(new[] { ' ', '-', '_', '(', ')', ',', '.' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var word in words)
            {
                if (NamedPatrolColors.TryGetValue(word, out var namedColor))
                    return namedColor;
            }

            var index = model.PatrolNames.IndexOf(patrolName);
            if (index < 0)
                return "#E2E2E2";
            return PatrolPalette[index % PatrolPalette.Length];
        }

        // Patrol colour choices are a local display preference, not Terrain data, so they're kept in
        // this browser's local storage (keyed by unit) rather than anywhere near the member/report models.
        internal async Task SetPatrolColor(string patrolName, string colorHex)
        {
            if (string.IsNullOrEmpty(patrolName) || string.IsNullOrEmpty(colorHex))
                return;
            _patrolColorOverrides[patrolName] = colorHex;
            await _localStorageService.SetItemAsync($"{model.UnitId}_PatrolColors", _patrolColorOverrides);
        }

        // Counts use the same M/F/NB labels as the card badges (GetColumnMembers/PatrolMemberCard)
        // so the tally and the cards never disagree on how gender is written.
        internal (int total, int male, int female, int nonBinary, int unspecified, string averageAge) GetColumnStats(string patrolName)
        {
            var members = GetColumnMembers(patrolName).ToList();
            var averageAge = "";
            if (members.Any())
            {
                var avgMonths = (int)Math.Round(members.Average(m => GetAgeInMonths(m.Age)));
                averageAge = $"{avgMonths / 12}y {avgMonths % 12}m";
            }
            return (members.Count,
                members.Count(m => m.Gender == "M"),
                members.Count(m => m.Gender == "F"),
                members.Count(m => m.Gender == "NB"),
                members.Count(m => string.IsNullOrEmpty(m.Gender)),
                averageAge);
        }

        // The average attendance of the members now in a column, so it follows them as they are moved; blank when none of them has any
        internal string GetColumnAttendance(string patrolName)
        {
            var shares = GetColumnMembers(patrolName).Select(m => m.AttendanceShare).Where(s => s != null).Select(s => s!.Value).ToList();
            return shares.Count == 0 ? "" : $"{Math.Round(shares.Average() * 100)}%";
        }

        // Flags a column's count badge when it's more than one member away from the average across
        // real patrols (Unassigned is excluded from the average - it isn't meant to be "balanced").
        internal string GetCountClass(string patrolName)
        {
            var countsByColumn = AllColumnNames
                .Where(c => !string.IsNullOrEmpty(c))
                .Select(c => GetColumnMembers(c).Count())
                .ToList();
            if (!countsByColumn.Any() || string.IsNullOrEmpty(patrolName))
                return string.Empty;

            var target = (double)countsByColumn.Sum() / countsByColumn.Count;
            var count = GetColumnMembers(patrolName).Count();
            if (Math.Abs(count - target) > 1)
                return "patrol-count-unbalanced";
            return string.Empty;
        }

        internal void OnDragStart(string memberId)
        {
            _draggedMemberId = memberId;
        }

        internal void OnDropToPatrol(string patrolName)
        {
            if (string.IsNullOrEmpty(_draggedMemberId))
                return;

            var member = model.Members.FirstOrDefault(m => m.Id == _draggedMemberId);
            if (member != null)
            {
                member.PatrolName = patrolName;
            }
            _draggedMemberId = null;
        }

        internal void OnDutyChange(PatrolPlannerMember member, string duty)
        {
            member.PatrolDuty = duty;
            // Promoting someone adds them to Unit Council automatically, per the group's convention.
            // Demoting them back to Member does NOT remove them again - that's a separate, deliberate
            // decision a leader should make on its own, not an automatic side effect of this control.
            if (duty == "APL" || duty == "PL" || duty == "UL")
            {
                member.UnitCouncil = true;
            }
        }

        // Cycles unset -> M -> F -> NB -> unset. "NB" (Non-Binary) is a real, explicit state here,
        // not lumped in with "unset" - a leader can tell the two apart in both the tally and the export.
        internal async Task CycleGenderClick(PatrolPlannerMember member)
        {
            member.Gender = member.Gender switch
            {
                "M" => "F",
                "F" => "NB",
                "NB" => "",
                _ => "M"
            };
            await _membersService.SetMemberGenderAsync(model.UnitId, member.Id, member.Gender);
        }

        internal async Task PatrolListPdfClick()
        {
            byte[] report = await GenerateUpdatedPatrolList(OutputType.PDF);
            var fileName = $"Patrol_List_{model.UnitName.Replace(' ', '_')}.pdf";
            await JS.InvokeVoidAsync("BlazorDownloadFile", fileName, "application/pdf", report);
        }

        internal async Task PatrolListXlsxClick()
        {
            byte[] report = await GenerateUpdatedPatrolList(OutputType.Excel);
            var fileName = $"Patrol_List_{model.UnitName.Replace(' ', '_')}.xlsx";
            await JS.InvokeVoidAsync("BlazorDownloadFile", fileName, "application/vnd.ms-excel", report);
        }

        // Reuses the existing PatrolList report type (same one Members.razor already calls) rather than
        // adding a new ReportType: that pipeline is served by the TopoReportFunction Lambda, which would
        // need a separate deployment to pick up a brand new report type. This just feeds it the reshuffled
        // in-memory list instead of Terrain's live data, so it works today with no extra deployment step.
        private async Task<byte[]> GenerateUpdatedPatrolList(OutputType outputType)
        {
            var groupName = _storageService.GroupName ?? "Group Name";
            var unitName = model.UnitName;
            var section = _storageService.Section;

            var updatedList = model.Members
                .Select(m => new MemberListModel
                {
                    first_name = m.FirstName,
                    last_name = m.LastName,
                    patrol_name = m.PatrolName,
                    patrol_duty = m.PatrolDuty,
                    unit_council = m.UnitCouncil,
                    isAdultLeader = 0
                })
                .OrderBy(m => m.patrol_name)
                .ToList();
            var serialisedList = JsonConvert.SerializeObject(updatedList);

            return await _reportService.GetPatrolListReport(groupName, section, unitName, outputType, serialisedList, false);
        }

        // Used for the "Changes to make in Terrain" list only (not the board itself), where the
        // text needs to read as an instruction. A member who had a patrol and now has none needs an
        // active removal in Terrain; one who was never assigned a patrol needs no action there at all.
        internal static string DescribePatrolChange(PatrolPlannerMember member)
        {
            if (string.IsNullOrEmpty(member.PatrolName))
            {
                return string.IsNullOrEmpty(member.OriginalPatrolName)
                    ? "Unassigned"
                    : "Remove from patrol";
            }
            return member.PatrolName;
        }
    }
}
