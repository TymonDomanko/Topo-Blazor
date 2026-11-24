using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Topo.Model.BadgeOrder;
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
        // The day to count from: the last order's, or one the leader picks when no order has been recorded
        public DateTime From = DateTime.Today.AddMonths(-3);
        public bool ChangingDate;
        public bool Loading;
        public string Progress = "";
        public string? Error;
        public List<BadgeOrderLine>? Lines;
        // The day the lines were worked out from
        public DateTime LinesFrom;
        public bool ConfirmPlaced;
        public bool Busy;
        public string Message = "";
        public bool MessageIsError;

        public bool HasLastOrder => Status?.LastOrder != null;
        // The date can be typed when there is no last order, or when the leader has asked to change it
        public bool CanPickDate => !HasLastOrder || ChangingDate;
        public int InShopCount => Lines?.Where(l => l.InShop).Sum(l => l.Count) ?? 0;
        public int ByHandCount => Lines?.Where(l => !l.InShop).Sum(l => l.Count) ?? 0;
        public decimal Total => Lines?.Sum(l => l.Cost) ?? 0;
        public List<string> CartLinks => Lines == null ? new List<string>() : BadgeOrderBuilder.CartLinks(Lines);

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
            if (Status.LastOrder.HasValue)
            {
                From = Status.LastOrder.Value;
            }
        }

        internal async Task ShowBadgesClick()
        {
            if (From.Date > DateTime.Today)
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
                var result = await _badgeOrderService.BuildAsync(UnitId.Length > 0 ? new[] { UnitId } : Units.Keys, From, DateTime.Today, step =>
                {
                    Progress = step;
                    InvokeAsync(StateHasChanged);
                });
                Lines = result.Lines;
                LinesFrom = result.From;
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

        // Records today as the day of the last order, in the group's store (after the leader has confirmed)
        internal async Task ConfirmPlacedClick()
        {
            Busy = true;
            var result = await _badgeOrderService.MarkPlacedAsync(DateTime.Today);
            ConfirmPlaced = false;
            if (result.Succeeded)
            {
                Status = await _badgeOrderService.StatusAsync();
                Message = $"Recorded {DateTime.Today:dd/MM/yyyy} as the day of the last order. The next one counts from then.";
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
