using Topo.Model.Cache;
using Topo.Model.Logbook;

namespace Topo.Services
{
    public interface ILogbookTotalsService
    {
        // A Scout's totals for a page: what is held if it will do (see CacheScope; without a scope, if it is in date), otherwise read from Terrain.
        // An older copy is handed out when Terrain does not answer; null when there is nothing at all. Assumes the Scout's profile to read, unless
        // the scope has its own way of preparing for a read (CacheScope.BeforeRead).
        Task<CachedRead<GetMemberLogbookMetricsResultModel>?> ForPageAsync(string memberId, CancellationToken cancellationToken = default);
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
        private readonly ITerrainLocks _locks;

        public LogbookTotalsService(ITerrainAPIService terrainAPIService, StorageService storageService, DataCache dataCache, ITerrainLocks locks)
        {
            _locks = locks;
            _terrainAPIService = terrainAPIService;
            _storageService = storageService;
            _dataCache = dataCache;
        }

        private string Key(string memberId) => $"{_storageService.CacheKeyPrefix}|logbook-totals|{memberId}";

        // The totals kept for a Scout, in date or stale; null when none are kept
        private Task<CachedRead<GetMemberLogbookMetricsResultModel>?> HeldAsync(string memberId) =>
            _dataCache.GetAsync<GetMemberLogbookMetricsResultModel>(Key(memberId));

        public async Task<CachedRead<GetMemberLogbookMetricsResultModel>?> ForPageAsync(string memberId, CancellationToken cancellationToken = default)
        {
            var held = await HeldAsync(memberId);
            if (held != null && Serves(held))
            {
                CacheScope.Current?.Held();
                return Used(held);
            }

            // a caller reading more of this Scout than the totals assumes the profile once for it all
            var prepare = CacheScope.Current?.BeforeRead;
            if (prepare != null)
            {
                await prepare();
                return Used(await ReadAsync(memberId) ?? held);
            }

            using var profile = await LockProfileAsync(cancellationToken);
            try
            {
                // assuming a Scout's profile replaces any other, so only handing it back is needed
                await _terrainAPIService.AssumeProfile(memberId);
                return Used(await ReadAsync(memberId) ?? held);
            }
            finally
            {
                await _terrainAPIService.RevokeAssumedProfiles();
            }
        }

        private static bool Serves(CachedRead<GetMemberLogbookMetricsResultModel> held) =>
            CacheScope.Current?.Serves(held.IsStale) ?? !held.IsStale;

        private static CachedRead<GetMemberLogbookMetricsResultModel>? Used(CachedRead<GetMemberLogbookMetricsResultModel>? read)
        {
            if (read != null)
            {
                CacheScope.Current?.Used(read.FetchedUtc, read.IsStale);
            }
            return read;
        }

        // Reads the totals from Terrain and keeps them; null when Terrain did not answer
        private async Task<CachedRead<GetMemberLogbookMetricsResultModel>?> ReadAsync(string memberId)
        {
            var started = _dataCache.UtcNow();
            var totals = await _terrainAPIService.TryGetMemberLogbookMetrics(memberId);
            return totals == null ? null : await _dataCache.PutAsync(Key(memberId), totals, readFrom: started);
        }

        public async Task RefreshAsync(IReadOnlyList<string> memberIds, Action<string>? progress = null, CancellationToken cancellationToken = default)
        {
            using var scope = CacheScope.Begin(CacheMode.Reread);
            for (var i = 0; i < memberIds.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Invoke($"{i + 1} of {memberIds.Count} Scouts");
                await ForPageAsync(memberIds[i], cancellationToken);
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

        public Task<IDisposable> LockProfileAsync(CancellationToken cancellationToken = default) => _locks.ProfileAsync(cancellationToken);
    }
}
