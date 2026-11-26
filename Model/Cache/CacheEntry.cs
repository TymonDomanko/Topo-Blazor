namespace Topo.Model.Cache
{
    // One read kept in the browser: what Terrain answered (as JSON) and when
    public class CacheEntry
    {
        public string Key { get; set; } = string.Empty;
        public string Json { get; set; } = string.Empty;
        // Milliseconds since 1970, UTC
        public long FetchedAt { get; set; }
        // Kept until it is read again on request: it neither goes stale nor is dropped for its age
        public bool Keep { get; set; }
        // The shape of what was kept; entries of another version are not used (see DataCache.Version)
        public int Version { get; set; }
    }

    // Where entries are kept. A store that cannot be used (no IndexedDB, private browsing) behaves as an empty one.
    public interface ICacheStore
    {
        Task<CacheEntry?> GetAsync(string key);
        Task PutAsync(CacheEntry entry);
        Task ClearAsync();
        // Removes every entry fetched before the given time (milliseconds since 1970, UTC)
        Task RemoveFetchedBeforeAsync(long fetchedAt);
    }
}
