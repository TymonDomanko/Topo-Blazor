using System.Globalization;
using Topo.Model.Cache;
using Topo.Model.Logbook;

namespace Topo.Services
{
    public interface ILogbookService
    {
        // The Scouts' logbook entries for the report. Reads come from the data cache while in date, unless a CacheScope asks otherwise.
        public Task<List<MemberLogbookReportViewModel>> GenerateLogbookData(List<KeyValuePair<string, string>> selectedMembers,
            Action<string>? progress = null, CancellationToken cancellationToken = default);
        // When the oldest of what is kept of these Scouts' logbooks was read; null when nothing is
        public Task<DateTime?> OldestReadAsync(IReadOnlyList<string> memberIds);
    }
    public class LogbookService : ILogbookService
    {
        private readonly ITerrainAPIService _terrainAPIService;

        private readonly ILogbookTotalsService _logbookTotals;

        public LogbookService(ITerrainAPIService terrainAPIService, ILogbookTotalsService logbookTotals)
        {
            _terrainAPIService = terrainAPIService;
            _logbookTotals = logbookTotals;
        }

        public async Task<DateTime?> OldestReadAsync(IReadOnlyList<string> memberIds)
        {
            var totals = await _logbookTotals.OldestReadAsync(memberIds);
            var entries = await _terrainAPIService.HeldLogbookSinceAsync(memberIds);
            return totals == null || entries < totals ? entries : totals;
        }

        public async Task<List<MemberLogbookReportViewModel>> GenerateLogbookData(List<KeyValuePair<string, string>> selectedMembers,
            Action<string>? progress = null, CancellationToken cancellationToken = default)
        {
            using var ownScope = CacheScope.Current == null ? CacheScope.Begin(CacheMode.Fresh) : null;
            var scope = CacheScope.Current!;
            var memberLogbooks = new List<MemberLogbookReportViewModel>();
            for (var i = 0; i < selectedMembers.Count; i++)
            {
                var memberKVP = selectedMembers[i];
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Invoke($"{i + 1} of {selectedMembers.Count} Scouts");
                // totals read in the last day serve; older ones are read again, and are only used if Terrain does not answer
                var totals = await _logbookTotals.ForPageAsync(memberKVP.Key, cancellationToken);
                var getMemberLogbookMetrics = totals?.Value ?? new GetMemberLogbookMetricsResultModel();
                var totalNightsCamped = getMemberLogbookMetrics.results.Where(r => r.name == "total_nights_camped").FirstOrDefault()?.value ?? 0;
                var totalKmsHiked = (getMemberLogbookMetrics.results.Where(r => r.name == "total_distance_hiked").FirstOrDefault()?.value ?? 0) / 1000.0f;

                // the Scout's profile is only assumed if something of their logbook has to be read from Terrain
                IDisposable? profile = null;
                scope.BeforeRead = async () =>
                {
                    if (profile == null)
                    {
                        profile = await _logbookTotals.LockProfileAsync(cancellationToken);
                        await _terrainAPIService.AssumeProfile(memberKVP.Key);
                    }
                };
                try
                {
                    var getMemberLogbookSummary = await _terrainAPIService.GetMemberLogbookSummary(memberKVP.Key);
                    if (getMemberLogbookSummary != null)
                    {
                        foreach (var summaryResult in getMemberLogbookSummary.results)
                        {
                            var getLogbookDetail = await _terrainAPIService.GetMemberLogbookDetail(memberKVP.Key, summaryResult.id);
                            if (getLogbookDetail != null)
                            {
                                var activityDate = DateTime.ParseExact(getLogbookDetail.start_date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                                var endDate = DateTime.ParseExact(getLogbookDetail.end_date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                                var nightsCamped = getLogbookDetail.categories.Any(c => c == "camping") ?
                                    (endDate - activityDate).TotalDays :
                                    0;
                                var kmsHiked = getLogbookDetail.categories.Any(c => c == "walking_hike") ?
                                    getLogbookDetail.distance_walkabout / 1000.0f :
                                    0;
                                memberLogbooks.Add(new MemberLogbookReportViewModel
                                {
                                    MemberName = memberKVP.Value,
                                    TotalKilometersHiked = totalKmsHiked,
                                    TotalNightsCamped = totalNightsCamped,
                                    ActivityName = getLogbookDetail.title,
                                    ActivityArea = getLogbookDetail.achievement_meta.branch,
                                    ActivityDate = activityDate,
                                    ActivityLead = getLogbookDetail.details.who_lead,
                                    MemberRole = getLogbookDetail.details.your_role,
                                    KilometersHiked = kmsHiked,
                                    NightsCamped = (float)nightsCamped,
                                    VerifierName = getLogbookDetail.details.verifier.name,
                                    VerifierContact = getLogbookDetail.details.verifier.contact,
                                    Weather = getLogbookDetail.details.weather,
                                    Location = getLogbookDetail.details.location,
                                    Grade = getLogbookDetail.details.activity_grade,
                                    OtherParticipants = getLogbookDetail.details.other_participants,
                                    Purpose = getLogbookDetail.details.purpose
                                });
                            }
                        }
                    }
                }
                finally
                {
                    scope.BeforeRead = null;
                    if (profile != null)
                    {
                        await _terrainAPIService.RevokeAssumedProfiles();
                        profile.Dispose();
                    }
                }
            }

            return memberLogbooks;
        }
    }
}



