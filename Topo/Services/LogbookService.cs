using System.Globalization;
using Topo.Model;
using Topo.Model.Logbook;

namespace Topo.Services
{
    public interface ILogbookService
    {
        // Each Scout's totals and entries, read one Scout at a time (a logbook is only readable with that Scout's profile taken on).
        // onRead is told about each Scout as soon as they are read, so a page can show them while the rest are still coming. A Scout whose
        // logbook Terrain will not give (or that cannot be read) is passed to onFailed by name and the others carry on.
        public Task<List<MemberLogbook>> ReadLogbooksAsync(IReadOnlyList<KeyValuePair<string, string>> members, Action<MemberLogbook>? onRead = null,
            Action<string>? progress = null, CancellationToken cancellationToken = default, Action<string>? onFailed = null);
    }
    public class LogbookService : ILogbookService
    {
        private readonly ITerrainAPIService _terrainAPIService;

        public LogbookService(ITerrainAPIService terrainAPIService)
        {
            _terrainAPIService = terrainAPIService;
        }

        public async Task<List<MemberLogbook>> ReadLogbooksAsync(IReadOnlyList<KeyValuePair<string, string>> members, Action<MemberLogbook>? onRead = null,
            Action<string>? progress = null, CancellationToken cancellationToken = default, Action<string>? onFailed = null)
        {
            var logbooks = new List<MemberLogbook>();
            try
            {
                for (var i = 0; i < members.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    progress?.Invoke($"{members[i].Value} ({i + 1} of {members.Count})");
                    MemberLogbook logbook;
                    try
                    {
                        logbook = await ReadLogbookAsync(members[i].Key, members[i].Value);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        onFailed?.Invoke(members[i].Value);
                        continue;
                    }
                    logbooks.Add(logbook);
                    onRead?.Invoke(logbook);
                }
            }
            finally
            {
                await _terrainAPIService.RevokeAssumedProfiles();
            }
            return logbooks;
        }

        private async Task<MemberLogbook> ReadLogbookAsync(string memberId, string memberName)
        {
            await _terrainAPIService.RevokeAssumedProfiles();
            await _terrainAPIService.AssumeProfile(memberId);
            var getMemberLogbookMetrics = await _terrainAPIService.GetMemberLogbookMetrics(memberId);
            var totalNightsCamped = getMemberLogbookMetrics.results.Where(r => r.name == "total_nights_camped").FirstOrDefault()?.value ?? 0;
            var totalKmsHiked = (getMemberLogbookMetrics.results.Where(r => r.name == "total_distance_hiked").FirstOrDefault()?.value ?? 0) / 1000.0f;
            var logbook = new MemberLogbook { MemberId = memberId, MemberName = memberName, TotalKilometres = totalKmsHiked, TotalNights = totalNightsCamped };
            var getMemberLogbookSummary = await _terrainAPIService.GetMemberLogbookSummary(memberId);
            if (getMemberLogbookSummary != null)
            {
                // The summary only lists the entries; each one's detail is a request of its own. They are read a few at a time
                // (all while this Scout's profile is assumed) and come back in the order listed, so the report is unchanged.
                var logbookDetails = await ReadsAtOnce.SelectAsync(getMemberLogbookSummary.results,
                    summaryResult => _terrainAPIService.GetMemberLogbookDetail(memberId, summaryResult.id));
                foreach (var getLogbookDetail in logbookDetails)
                {
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
                        logbook.Entries.Add(new MemberLogbookReportViewModel
                        {
                            MemberName = memberName,
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
            return logbook;
        }
    }
}
