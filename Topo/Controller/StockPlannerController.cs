using Microsoft.AspNetCore.Components;
using Topo.Model.StockPlan;
using Topo.Services;

namespace Topo.Controller
{
    public class StockPlannerController : ComponentBase
    {
        [Inject]
        public StorageService _storageService { get; set; } = default!;

        [Inject]
        public IStockPlanService _stockPlanService { get; set; } = default!;

        [Inject]
        public NavigationManager NavigationManager { get; set; } = default!;

        public string GroupName = "";
        // The units to choose from, and the one chosen (blank for every unit)
        public Dictionary<string, string> Units = new Dictionary<string, string>();
        public string UnitId = "";
        // How many years back to count what was awarded. Changing it needs the badges read again, so it takes effect on "Show stock plan".
        public int HistoryYears = 1;
        // How many months the stock should last. Changing it works out the table again without reading Terrain again.
        public int CoverMonths = 3;
        public bool Loading;
        public string Progress = "";
        public string? Error;
        public StockPlanData? Data;
        public List<StockLine> Plan = new List<StockLine>();

        public int TotalToKeep => Plan.Sum(l => l.Suggested);
        public decimal TotalValue => Plan.Sum(l => l.Value);
        public bool AnyNotInShop => Plan.Any(l => l.Shop == null);

        protected override void OnInitialized()
        {
            if (!_storageService.IsAuthenticated)
            {
                NavigationManager.NavigateTo("index");
                return;
            }
            GroupName = _storageService.GroupNameDisplay;
            Units = _storageService.Units ?? new Dictionary<string, string>();
        }

        internal void UnitChange(ChangeEventArgs e)
        {
            UnitId = e.Value?.ToString() ?? "";
            // What was read was for the other units
            Data = null;
            Plan = new List<StockLine>();
        }

        internal void YearsChange(ChangeEventArgs e)
        {
            HistoryYears = int.TryParse(e.Value?.ToString(), out var years) ? Math.Clamp(years, 1, StockPlanService.MostHistoryYears) : HistoryYears;
        }

        internal void CoverChange(ChangeEventArgs e)
        {
            CoverMonths = int.TryParse(e.Value?.ToString(), out var months) ? Math.Clamp(months, 1, StockPlanService.MostCoverMonths) : CoverMonths;
            Recalculate();
        }

        private void Recalculate()
        {
            Plan = Data == null ? new List<StockLine>() : StockPlanner.Plan(Data.History, Data.Pending, Data.Anniversaries, DateTime.Today, CoverMonths, Data.HistoryYears * 12);
        }

        internal async Task ShowClick()
        {
            Error = null;
            Data = null;
            Plan = new List<StockLine>();
            Loading = true;
            Progress = "";
            try
            {
                Data = await _stockPlanService.ReadAsync(UnitId.Length > 0 ? new[] { UnitId } : Units.Keys, HistoryYears, step =>
                {
                    Progress = step;
                    InvokeAsync(StateHasChanged);
                });
                Recalculate();
            }
            catch (Exception ex)
            {
                Error = "The badges could not be read from Terrain: " + ex.Message;
            }
            Loading = false;
        }
    }
}
