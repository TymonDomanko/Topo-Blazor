using Topo.Model.BadgeOrder;
using Topo.Model.Cache;
using Topo.Model.BadgeStore;
using Topo.Model.Members;

namespace Topo.Services
{
    public class BadgeOrderStatus
    {
        // The group has the store event Topo keeps its records in (each unit's last order is kept there)
        public bool StoreFound { get; set; }
        // The day of the last order for each unit that has had one
        public Dictionary<string, DateTime> LastOrderByUnit { get; set; } = new Dictionary<string, DateTime>();
        public string Problem { get; set; } = "";
    }

    public class BadgeOrderResult
    {
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public List<BadgeOrderLine> Lines { get; set; } = new List<BadgeOrderLine>();
    }

    public interface IBadgeOrderService
    {
        // Reads the group's store from Terrain: whether it exists and the day of each unit's last order
        Task<BadgeOrderStatus> StatusAsync();
        // The badges awarded in the units from each one's own day (a unit not in the dictionary has no day, so is left out) to the last day, as an
        // order for the Scout Shop
        Task<BadgeOrderResult> BuildAsync(IReadOnlyDictionary<string, DateTime> fromByUnit, DateTime to, Action<string>? progress = null, CancellationToken cancellationToken = default);
        // Records that an order was placed for the units on a day, so the next one counts each from then. Only on a leader's say so: it changes
        // the group's store. Stops at the first unit that fails and says which.
        Task<BadgeStoreWriteResult> MarkPlacedAsync(IEnumerable<string> unitIds, DateTime day);
    }

    // The badge order (Model/BadgeOrder): what the group has awarded since its last order, matched to the Scout Shop's products.
    public class BadgeOrderService : IBadgeOrderService
    {
        private readonly StorageService _storageService;
        private readonly IMembersService _membersService;
        private readonly IBadgeStoreService _badgeStoreService;
        private readonly ISectionReportService _sectionReportService;

        public BadgeOrderService(StorageService storageService, IMembersService membersService, IBadgeStoreService badgeStoreService, ISectionReportService sectionReportService)
        {
            _storageService = storageService;
            _membersService = membersService;
            _badgeStoreService = badgeStoreService;
            _sectionReportService = sectionReportService;
        }

        public async Task<BadgeOrderStatus> StatusAsync()
        {
            // what is changed is worked out from Terrain as it is now, whatever page asked
            using var live = CacheScope.Outside();
            try
            {
                var contents = await ReadStoreAsync();
                return new BadgeOrderStatus { StoreFound = contents.StoreEvents > 0, LastOrderByUnit = BadgeOrderRecords.LastOrders(contents, _storageService.Units.Keys) };
            }
            catch (Exception ex)
            {
                return new BadgeOrderStatus { Problem = ex.Message };
            }
        }

        public async Task<BadgeOrderResult> BuildAsync(IReadOnlyDictionary<string, DateTime> fromByUnit, DateTime to, Action<string>? progress = null, CancellationToken cancellationToken = default)
        {
            var units = fromByUnit.Keys.Where(id => _storageService.Units.ContainsKey(id)).ToList();
            if (units.Count == 0)
            {
                return new BadgeOrderResult { From = to.Date, To = to.Date };
            }
            var earliest = units.Min(id => fromByUnit[id].Date);
            var request = new SectionReportRequest
            {
                UnitIds = units, IsGroup = true, From = earliest, To = to.Date,
                FromByUnit = units.ToDictionary(id => id, id => fromByUnit[id].Date)
            };
            var report = await _sectionReportService.GetSectionReport(request, progress, cancellationToken);
            return new BadgeOrderResult { From = earliest, To = to.Date, Lines = BadgeOrderBuilder.Build(report.Report) };
        }

        public async Task<BadgeStoreWriteResult> MarkPlacedAsync(IEnumerable<string> unitIds, DateTime day)
        {
            // what is changed is worked out from Terrain as it is now, whatever page asked
            using var live = CacheScope.Outside();
            HashSet<string> trusted;
            try
            {
                trusted = await TrustedLeaders.AdultLeaderIdsAsync(_storageService, _membersService);
            }
            catch (Exception ex)
            {
                return new BadgeStoreWriteResult { Outcome = BadgeStoreWriteOutcome.Failed, Message = $"The records could not be read. {ex.Message}".Trim() };
            }
            var user = UserId;
            var last = new BadgeStoreWriteResult { Outcome = BadgeStoreWriteOutcome.AlreadyRecorded, Message = "Nothing to record." };
            foreach (var unitId in unitIds.Where(id => _storageService.Units.ContainsKey(id)).Distinct())
            {
                last = await _badgeStoreService.RecordAwardAsync(user, GroupId, unitId, BadgeOrderRecords.LastOrderAwardId, day.Date, null, id => id == user || trusted.Contains(id));
                if (!last.Succeeded)
                {
                    return new BadgeStoreWriteResult { Outcome = last.Outcome, Message = $"{_storageService.Units[unitId]}: {last.Message}" };
                }
            }
            return last;
        }

        private async Task<BadgeStoreContents> ReadStoreAsync()
        {
            if (string.IsNullOrEmpty(GroupId) || string.IsNullOrEmpty(UserId))
            {
                throw new InvalidOperationException("Not signed in to a group.");
            }
            var trusted = await TrustedLeaders.AdultLeaderIdsAsync(_storageService, _membersService);
            var user = UserId;
            return await _badgeStoreService.ReadAsync(user, GroupId, id => id == user || trusted.Contains(id));
        }

        private string GroupId => _storageService.GroupId ?? "";

        private string UserId => _storageService.GetProfilesResult?.profiles?.FirstOrDefault()?.member?.id ?? "";
    }
}
