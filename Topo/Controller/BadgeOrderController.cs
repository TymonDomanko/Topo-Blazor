using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Topo.Model.BadgeOrder;
using Topo.Model.Cache;
using Topo.Services;

namespace Topo.Controller
{
    public class BadgeOrderController : ComponentBase
    {
        [Inject]
        public StorageService _storageService { get; set; } = default!;

        [Inject]
        public IBadgeOrderService _badgeOrderService { get; set; } = default!;

        [Inject]
        IJSRuntime JS { get; set; } = default!;

        [Inject]
        public NavigationManager NavigationManager { get; set; } = default!;

        public string GroupName = "";
        // The units to choose from, and the one chosen (blank for every unit)
        public Dictionary<string, string> Units = new Dictionary<string, string>();
        public string UnitId = "";
        public BadgeOrderStatus? Status;
        // The day to count from for a unit with no order recorded (or for every unit chosen, when the leader changes the date)
        public DateTime From = DateTime.Today.AddMonths(-3);
        public bool ChangingDate;
        public bool Loading;
        public string Progress = "";
        public string? Error;
        public List<BadgeOrderLine>? Lines;
        // The units the lines are for, which "Mark order placed" records
        public List<string> LinesUnits = new List<string>();
        public bool ConfirmPlaced;
        public bool Busy;
        public string Message = "";
        public bool MessageIsError;

        // The units the order is for: the one chosen, or every unit
        public List<string> ChosenUnits => UnitId.Length > 0 && Units.ContainsKey(UnitId) ? new List<string> { UnitId } : Units.Keys.ToList();
        public DateTime? LastOrderOf(string unitId) => Status != null && Status.LastOrderByUnit.TryGetValue(unitId, out var day) ? day : null;
        public List<string> ChosenUnitsWithoutOrder => ChosenUnits.Where(u => LastOrderOf(u) == null).ToList();
        // The date can be typed for units with no order, or for them all when the leader asks to change it
        public bool CanPickDate => ChangingDate || ChosenUnitsWithoutOrder.Count > 0;
        // The day a unit is counted from
        public DateTime FromFor(string unitId) => ChangingDate ? From : LastOrderOf(unitId) ?? From;
        public string UnitName(string unitId) => Units.TryGetValue(unitId, out var name) ? name : unitId;
        public int InShopCount => Lines?.Where(l => l.InShop).Sum(l => l.Count) ?? 0;
        public int ByHandCount => Lines?.Where(l => !l.InShop).Sum(l => l.Count) ?? 0;
        public List<string> CartLinks => Lines == null ? new List<string>() : BadgeOrderBuilder.CartLinks(Lines);
        // Where a leader signs in to the Scout Shop (Topo never signs in for them)
        public const string ScoutShopLoginUrl = ScoutShopCatalog.ShopUrl + "account/login";

        protected override async Task OnInitializedAsync()
        {
            if (!_storageService.IsAuthenticated)
            {
                NavigationManager.NavigateTo("index");
                return;
            }
            GroupName = _storageService.GroupNameDisplay;
            Units = _storageService.Units ?? new Dictionary<string, string>();
            Status = await _badgeOrderService.StatusAsync();
        }

        // When the oldest of what the list was made from was read
        public DateTime? ReadUtc;

        // The list uses what the data cache holds while it is in date; "Refresh data" reads everything for it again
        internal Task ShowBadgesClick() => ShowBadges(CacheMode.Fresh);

        internal Task RefreshClick() => ShowBadges(CacheMode.Reread);

        private async Task ShowBadges(CacheMode mode)
        {
            if (CanPickDate && From.Date > DateTime.Today)
            {
                Error = "The day to count from can't be in the future.";
                return;
            }
            Error = null;
            Message = "";
            ConfirmPlaced = false;
            Lines = null;
            Loading = true;
            Progress = "";
            try
            {
                using var scope = CacheScope.Begin(mode);
                var units = ChosenUnits;
                var result = await _badgeOrderService.BuildAsync(units.ToDictionary(u => u, FromFor), DateTime.Today, step =>
                {
                    Progress = step;
                    InvokeAsync(StateHasChanged);
                });
                Lines = result.Lines;
                LinesUnits = units;
                ReadUtc = scope.OldestUtc;
            }
            catch (Exception ex)
            {
                Error = "The badges could not be read from Terrain: " + ex.Message;
            }
            Loading = false;
        }

        internal void UnitChange(ChangeEventArgs e)
        {
            UnitId = e.Value?.ToString() ?? "";
            // What was worked out was for the other units
            Lines = null;
            ConfirmPlaced = false;
            ChangingDate = false;
            Message = "";
        }

        internal void ChangeDateClick() => ChangingDate = true;

        internal void Change(BadgeOrderLine line, int by) => line.Count = Math.Max(0, line.Count + by);

        internal async Task CopyClick()
        {
            if (Lines == null)
            {
                return;
            }
            await JS.InvokeVoidAsync("navigator.clipboard.writeText", BadgeOrderBuilder.AsText(Lines));
            Message = "Copied the list, one badge to a line with its quantity.";
            MessageIsError = false;
        }

        internal void MarkPlacedClick()
        {
            Message = "";
            ConfirmPlaced = true;
        }

        internal void CancelPlacedClick() => ConfirmPlaced = false;

        // Records today as the day of the last order for the units the lines are for, in the group's store (after the leader has confirmed)
        internal async Task ConfirmPlacedClick()
        {
            Busy = true;
            var result = await _badgeOrderService.MarkPlacedAsync(LinesUnits, DateTime.Today);
            ConfirmPlaced = false;
            if (result.Succeeded)
            {
                Status = await _badgeOrderService.StatusAsync();
                Message = $"Recorded {DateTime.Today:dd/MM/yyyy} as the day of the last order for {string.Join(", ", LinesUnits.Select(UnitName))}. The next order counts each of them from then.";
                MessageIsError = false;
                ChangingDate = false;
            }
            else
            {
                Message = result.Message;
                MessageIsError = true;
            }
            Busy = false;
        }
    }
}
