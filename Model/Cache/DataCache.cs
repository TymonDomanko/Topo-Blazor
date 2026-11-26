using System.Text.Json;

namespace Topo.Model.Cache
{
    // A kept read and how old it is
    public class CachedRead<T>
    {
        public CachedRead(T value, DateTime fetchedUtc, bool isStale, bool isKept = false)
        {
            IsKept = isKept;
            Value = value;
            FetchedUtc = fetchedUtc;
            IsStale = isStale;
        }

        public T Value { get; }
        public DateTime FetchedUtc { get; }
        // Older than a day: it can still be shown, but should be read again
        public bool IsStale { get; }
        // Kept until read again on request, so its age is not part of how old a page's data is
        public bool IsKept { get; }
    }

    // Keeps what pages have read from Terrain (docs/data-cache.md): nothing is read ahead. A read is in date for a day; an older one
    // is still handed out, marked stale, for the page to show while it reads it again; after 30 days it is dropped. Everything is asked of the
    // browser's store, so what one tab drops after writing to Terrain is gone for the others too.
    public class DataCache
    {
        // Raise when what is kept changes shape, so older entries are left behind
        public const int Version = 1;
        public static readonly TimeSpan Fresh = TimeSpan.FromDays(1);
        public static readonly TimeSpan Dropped = TimeSpan.FromDays(30);
        // The entry that says when something was last dropped because Terrain was written to
        private const string ChangedKey = "changed";

        private readonly ICacheStore _store;
        private bool _swept;

        public DataCache(ICacheStore store)
        {
            _store = store;
        }

        // Turned off, nothing is handed out or kept, and every read goes to Terrain
        public bool Enabled { get; set; } = true;

        // The clock, which the tests set
        public Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

        public async Task<CachedRead<T>?> GetAsync<T>(string key)
        {
            await SweepAsync();
            if (!Enabled)
            {
                return null;
            }
            var entry = await _store.GetAsync(key);
            if (entry == null)
            {
                return null;
            }

            var fetched = DateTimeOffset.FromUnixTimeMilliseconds(entry.FetchedAt).UtcDateTime;
            var age = entry.Keep ? TimeSpan.Zero : UtcNow() - fetched;
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
            return value == null ? null : new CachedRead<T>(value, fetched, age >= Fresh, entry.Keep);
        }

        // readFrom: when the read that gave the value started. Something written to Terrain since then may not be in it, so it is not kept.
        public async Task<CachedRead<T>> PutAsync<T>(string key, T value, bool keep = false, DateTime? readFrom = null)
        {
            var now = UtcNow();
            if (!Enabled || (readFrom != null && (await _store.GetAsync(ChangedKey))?.FetchedAt >= Milliseconds(readFrom.Value)))
            {
                return new CachedRead<T>(value, now, false);
            }
            var entry = new CacheEntry
            {
                Key = key,
                Json = JsonSerializer.Serialize(value),
                FetchedAt = Milliseconds(now),
                Keep = keep,
                Version = Version
            };
            await _store.PutAsync(entry);
            return new CachedRead<T>(value, now, false, keep);
        }

        // A Terrain read. Inside a CacheScope it is served from the cache when what is held will do, and kept when it is made; when Terrain does not
        // answer, an older copy is handed out. Outside a scope it is just the read.
        // keep: whether an answer is one to hold until it is asked for again (see CacheEntry.Keep)
        public async Task<string> ReadThroughAsync(string key, Func<Task<(bool Succeeded, string Body)>> read, Func<string, bool>? keep = null)
        {
            var scope = CacheScope.Current;
            if (scope == null)
            {
                return (await read()).Body;
            }

            var held = await GetAsync<string>(key);
            if (held != null && scope.Serves(held.IsStale))
            {
                Note(scope, held);
                return held.Value;
            }

            if (scope.BeforeRead != null)
            {
                await scope.BeforeRead();
            }
            var started = UtcNow();
            var (succeeded, body) = await read();
            if (succeeded)
            {
                Note(scope, await PutAsync(key, body, keep?.Invoke(body) ?? false, started));
                return body;
            }
            if (held != null)
            {
                Note(scope, held);
                return held.Value;
            }
            return body;
        }

        private static void Note(CacheScope scope, CachedRead<string> read)
        {
            if (!read.IsKept)
            {
                scope.Used(read.FetchedUtc, read.IsStale);
            }
        }

        public Task RemoveAsync(string key) => _store.RemoveAsync(new[] { key });

        // Drops what a write to Terrain may have made wrong (see WriteEffects)
        public async Task ForgetAsync(Func<string, bool> affected)
        {
            var keys = (await _store.KeysAsync()).Where(k => k != ChangedKey && affected(k)).ToList();
            if (keys.Count > 0)
            {
                await _store.RemoveAsync(keys);
            }
            await _store.PutAsync(new CacheEntry { Key = ChangedKey, FetchedAt = Milliseconds(UtcNow()), Version = Version });
        }

        // Everything goes: the cache holds young people's records, so it does not stay behind after logging out
        public async Task ClearAsync()
        {
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
