using Topo.Model.BadgeOrder;
using Topo.Model.BadgeStore;
using Topo.Model.Members;

namespace Topo.Services
{
    public class BadgeOrderStatus
    {
        // The group has the store event Topo keeps its records in (the last order's day is kept there)
        public bool StoreFound { get; set; }
        public DateTime? LastOrder { get; set; }
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
        // Reads the group's store from Terrain: whether it exists and the day of the last order
        Task<BadgeOrderStatus> StatusAsync();
        // The badges awarded in the units (every unit of the group when none are given) from one day to another, as an order for the Scout Shop
        Task<BadgeOrderResult> BuildAsync(IEnumerable<string> unitIds, DateTime from, DateTime to, Action<string>? progress = null, CancellationToken cancellationToken = default);
        // Records that an order was placed on a day, so the next one counts from then. Only on a leader's say so: it changes the group's store.
        Task<BadgeStoreWriteResult> MarkPlacedAsync(DateTime day);
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
            try
            {
                var contents = await ReadStoreAsync();
                return new BadgeOrderStatus { StoreFound = contents.StoreEvents > 0, LastOrder = BadgeOrderRecords.LastOrder(contents, GroupId) };
            }
            catch (Exception ex)
            {
                return new BadgeOrderStatus { Problem = ex.Message };
            }
        }

        public async Task<BadgeOrderResult> BuildAsync(IEnumerable<string> unitIds, DateTime from, DateTime to, Action<string>? progress = null, CancellationToken cancellationToken = default)
        {
            var chosen = unitIds.Where(id => _storageService.Units.ContainsKey(id)).Distinct().ToList();
            var request = new SectionReportRequest { UnitIds = chosen.Count > 0 ? chosen : _storageService.Units.Keys.ToList(), IsGroup = true, From = from.Date, To = to.Date };
            var report = await _sectionReportService.GetSectionReport(request, progress, cancellationToken);
            return new BadgeOrderResult { From = from.Date, To = to.Date, Lines = BadgeOrderBuilder.Build(report.Report) };
        }

        public async Task<BadgeStoreWriteResult> MarkPlacedAsync(DateTime day)
        {
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
            return await _badgeStoreService.RecordAwardAsync(user, GroupId, GroupId, BadgeOrderRecords.LastOrderAwardId, day.Date, null, id => id == user || trusted.Contains(id));
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
