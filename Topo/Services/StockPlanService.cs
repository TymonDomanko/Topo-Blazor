using Topo.Model.BadgeOrder;
using Topo.Model.StockPlan;

namespace Topo.Services
{
    // What the stock planner reads: the badges awarded in the last year, what is waiting for approval now, and the years of service coming up
    public class StockPlanData
    {
        public List<BadgeOrderLine> History { get; set; } = new List<BadgeOrderLine>();
        public List<BadgeOrderLine> Pending { get; set; } = new List<BadgeOrderLine>();
        public List<DatedBadge> Anniversaries { get; set; } = new List<DatedBadge>();
        // How many years the history looks back
        public int HistoryYears { get; set; } = 1;
    }

    public interface IStockPlanService
    {
        // Reads the units' badges for the stock plan (every unit of the group when none are given), the awards over the years to look back
        Task<StockPlanData> ReadAsync(IEnumerable<string> unitIds, int historyYears, Action<string>? progress = null, CancellationToken cancellationToken = default);
    }

    // The stock planner (Model/StockPlan): the history is the Badge Order's reading of the last twelve months, so both count the same way.
    public class StockPlanService : IStockPlanService
    {
        // The most months of cover the page offers, which is how far ahead the years of service anniversaries are read
        public const int MostCoverMonths = 24;
        // The most years the page can look back
        public const int MostHistoryYears = 10;

        private readonly StorageService _storageService;
        private readonly ITerrainAPIService _terrainAPIService;
        private readonly IBadgeOrderService _badgeOrderService;
        private readonly IYearsOfServiceService _yearsOfService;

        public StockPlanService(StorageService storageService, ITerrainAPIService terrainAPIService, IBadgeOrderService badgeOrderService, IYearsOfServiceService yearsOfService)
        {
            _storageService = storageService;
            _terrainAPIService = terrainAPIService;
            _badgeOrderService = badgeOrderService;
            _yearsOfService = yearsOfService;
        }

        public async Task<StockPlanData> ReadAsync(IEnumerable<string> unitIds, int historyYears, Action<string>? progress = null, CancellationToken cancellationToken = default)
        {
            var units = unitIds.Where(id => _storageService.Units.ContainsKey(id)).Distinct().ToList();
            if (units.Count == 0)
            {
                units = _storageService.Units.Keys.ToList();
            }
            var today = DateTime.Today;
            var years = Math.Clamp(historyYears, 1, MostHistoryYears);
            var data = new StockPlanData { HistoryYears = years };

            var history = await _badgeOrderService.BuildAsync(units.ToDictionary(id => id, _ => today.AddYears(-years)), today, progress, cancellationToken);
            data.History = history.Lines;

            foreach (var unitId in units)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var section = _storageService.GetProfilesResult?.profiles?.FirstOrDefault(p => p.unit?.id == unitId)?.unit?.section ?? "";
                if (section.Length == 0)
                {
                    continue;
                }
                progress?.Invoke($"{_storageService.Units[unitId]}: requests waiting for approval");
                var pending = await _terrainAPIService.GetUnitApprovals(unitId, "pending");
                foreach (var line in StockPlanner.FromRequests(pending.results, section))
                {
                    BadgeOrderBuilder.MergeInto(data.Pending, line);
                }
            }

            progress?.Invoke("years of service coming up");
            data.Anniversaries = await _yearsOfService.UpcomingAsync(units, today, today.AddMonths(MostCoverMonths));
            return data;
        }
    }
}
