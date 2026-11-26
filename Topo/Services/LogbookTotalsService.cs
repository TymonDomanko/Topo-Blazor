using Topo.Model.Cache;
using Topo.Model.Logbook;

namespace Topo.Services
{
    public interface ILogbookTotalsService
    {
        // The totals kept for a Scout, in date or stale; null when none are kept
        Task<CachedRead<GetMemberLogbookMetricsResultModel>?> HeldAsync(string memberId);
        // Reads a Scout's totals from Terrain and keeps them. Their profile must already be assumed. Null when Terrain did not answer.
        Task<CachedRead<GetMemberLogbookMetricsResultModel>?> ReadAsync(string memberId);
        // Reads each Scout's totals again, assuming their profile for it. A Scout who cannot be read keeps what was held.
        Task RefreshAsync(IReadOnlyList<string> memberIds, Action<string>? progress = null, CancellationToken cancellationToken = default);
        // When the oldest of the totals held for these Scouts was read; null when none are held
        Task<DateTime?> OldestReadAsync(IEnumerable<string> memberIds);
        // Terrain holds one assumed profile at a time: hold this from assuming a Scout's profile until the reads that need it are done
        Task<IDisposable> LockProfileAsync(CancellationToken cancellationToken = default);
    }

    // Each Scout's logbook totals (nights camped, distance hiked), which the Wallchart, Additional Awards and Logbook all read, one Scout at a
    // time. They are kept in the data cache, so a total read by one page serves the others for a day.
    public class LogbookTotalsService : ILogbookTotalsService
    {
        private readonly ITerrainAPIService _terrainAPIService;
        private readonly StorageService _storageService;
        private readonly DataCache _dataCache;
        private readonly SemaphoreSlim _profileLock = new SemaphoreSlim(1, 1);

        public LogbookTotalsService(ITerrainAPIService terrainAPIService, StorageService storageService, DataCache dataCache)
        {
            _terrainAPIService = terrainAPIService;
            _storageService = storageService;
            _dataCache = dataCache;
        }

        // Kept by who is signed in and the group, so two people using one browser do not share
        private string Key(string memberId) =>
            $"{_storageService.GetProfilesResult?.profiles?.FirstOrDefault()?.member?.id}|{_storageService.GroupId}|logbook-totals|{memberId}";

        public Task<CachedRead<GetMemberLogbookMetricsResultModel>?> HeldAsync(string memberId) =>
            _dataCache.GetAsync<GetMemberLogbookMetricsResultModel>(Key(memberId));

        public async Task<CachedRead<GetMemberLogbookMetricsResultModel>?> ReadAsync(string memberId)
        {
            var totals = await _terrainAPIService.TryGetMemberLogbookMetrics(memberId);
            return totals == null ? null : await _dataCache.PutAsync(Key(memberId), totals);
        }

        public async Task RefreshAsync(IReadOnlyList<string> memberIds, Action<string>? progress = null, CancellationToken cancellationToken = default)
        {
            for (var i = 0; i < memberIds.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Invoke($"{i + 1} of {memberIds.Count} Scouts");
                using var profile = await LockProfileAsync(cancellationToken);
                try
                {
                    await _terrainAPIService.RevokeAssumedProfiles();
                    await _terrainAPIService.AssumeProfile(memberIds[i]);
                    await ReadAsync(memberIds[i]);
                }
                finally
                {
                    await _terrainAPIService.RevokeAssumedProfiles();
                }
            }
        }

        public async Task<DateTime?> OldestReadAsync(IEnumerable<string> memberIds)
        {
            DateTime? oldest = null;
            foreach (var memberId in memberIds)
            {
                var held = await HeldAsync(memberId);
                if (held != null && (oldest == null || held.FetchedUtc < oldest))
                {
                    oldest = held.FetchedUtc;
                }
            }
            return oldest;
        }

        public async Task<IDisposable> LockProfileAsync(CancellationToken cancellationToken = default)
        {
            await _profileLock.WaitAsync(cancellationToken);
            return new Release(_profileLock);
        }

        private sealed class Release : IDisposable
        {
            private SemaphoreSlim? _lock;

            public Release(SemaphoreSlim profileLock)
            {
                _lock = profileLock;
            }

            public void Dispose() => Interlocked.Exchange(ref _lock, null)?.Release();
        }
    }
}
