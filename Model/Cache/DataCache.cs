using System.Text.Json;

namespace Topo.Model.Cache
{
    // A kept read and how old it is
    public class CachedRead<T>
    {
        public CachedRead(T value, DateTime fetchedUtc, bool isStale)
        {
            Value = value;
            FetchedUtc = fetchedUtc;
            IsStale = isStale;
        }

        public T Value { get; }
        public DateTime FetchedUtc { get; }
        // Older than a day: it can still be shown, but should be read again
        public bool IsStale { get; }
    }

    // Keeps what pages have read from Terrain (docs/background-cache-design.md): nothing is read ahead. A read is in date for a day; an older one
    // is still handed out, marked stale, for the page to show while it reads it again; after 30 days it is dropped. The copy in memory saves
    // asking the browser's store twice in a session.
    public class DataCache
    {
        // Raise when what is kept changes shape, so older entries are left behind
        public const int Version = 1;
        public static readonly TimeSpan Fresh = TimeSpan.FromDays(1);
        public static readonly TimeSpan Dropped = TimeSpan.FromDays(30);

        private readonly ICacheStore _store;
        private readonly Dictionary<string, CacheEntry> _memory = new Dictionary<string, CacheEntry>();
        private bool _swept;

        public DataCache(ICacheStore store)
        {
            _store = store;
        }

        // The clock, which the tests set
        public Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

        public async Task<CachedRead<T>?> GetAsync<T>(string key)
        {
            await SweepAsync();
            if (!_memory.TryGetValue(key, out var entry))
            {
                entry = await _store.GetAsync(key);
                if (entry == null)
                {
                    return null;
                }
                _memory[key] = entry;
            }

            var fetched = DateTimeOffset.FromUnixTimeMilliseconds(entry.FetchedAt).UtcDateTime;
            var age = UtcNow() - fetched;
            if (entry.Version != Version || age >= Dropped)
            {
                return null;
            }

            T? value;
            try
            {
                value = JsonSerializer.Deserialize<T>(entry.Json);
            }
            catch (JsonException)
            {
                return null;
            }
            return value == null ? null : new CachedRead<T>(value, fetched, age >= Fresh);
        }

        public async Task<CachedRead<T>> PutAsync<T>(string key, T value)
        {
            var now = UtcNow();
            var entry = new CacheEntry
            {
                Key = key,
                Json = JsonSerializer.Serialize(value),
                FetchedAt = Milliseconds(now),
                Version = Version
            };
            _memory[key] = entry;
            await _store.PutAsync(entry);
            return new CachedRead<T>(value, now, false);
        }

        // Everything goes: the cache holds young people's records, so it does not stay behind after logging out
        public async Task ClearAsync()
        {
            _memory.Clear();
            await _store.ClearAsync();
        }

        // Once a session, drops what is too old to be used, so it does not stay in the browser just because no page asks for it again
        private async Task SweepAsync()
        {
            if (_swept)
            {
                return;
            }
            _swept = true;
            await _store.RemoveFetchedBeforeAsync(Milliseconds(UtcNow() - Dropped));
        }

        private static long Milliseconds(DateTime utc) => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
    }
}
