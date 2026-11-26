namespace Topo.Model.Cache
{
    public enum CacheMode
    {
        // Use whatever is held, however old, so the page shows at once
        Held,
        // Use what is held only while it is in date
        Fresh,
        // Read again from Terrain whatever is held
        Reread
    }

    // A page asks for its Terrain reads to go through the data cache by reading inside a scope: "using var scope = CacheScope.Begin(...)".
    // Reads made outside one go straight to Terrain, as they always did. The scope notes how old what it used was.
    public sealed class CacheScope : IDisposable
    {
        private static readonly AsyncLocal<CacheScope?> Ambient = new AsyncLocal<CacheScope?>();
        private readonly CacheScope? _outer;

        private CacheScope(CacheMode mode, CacheScope? outer)
        {
            Mode = mode;
            _outer = outer;
        }

        public static CacheScope? Current => Ambient.Value;

        public static CacheScope Begin(CacheMode mode)
        {
            var scope = new CacheScope(mode, Ambient.Value);
            Ambient.Value = scope;
            return scope;
        }

        public CacheMode Mode { get; }
        // When the oldest read the scope used was made; null when it has used none
        public DateTime? OldestUtc { get; private set; }
        // Whether anything it used was older than a day
        public bool UsedStale { get; private set; }
        // Whether anything it used was a held copy rather than read from Terrain just now
        public bool UsedHeld { get; private set; }
        // What has been worked out from reads in this scope and may be asked for again, such as a unit's member list
        public Dictionary<string, object> Worked { get; } = new Dictionary<string, object>();

        // Whether anything it used is read from Terrain every time it is shown (an event list), and so is still to be checked
        public bool ToCheck { get; private set; }

        public void Check() => ToCheck = true;

        // Run just before a read goes to Terrain, for work that is only needed then (assuming a Scout's profile)
        public Func<Task>? BeforeRead { get; set; }

        // Whether a held read serves, or has to be made again
        public bool Serves(bool isStale) => Mode == CacheMode.Held || (Mode == CacheMode.Fresh && !isStale);

        public void Used(DateTime fetchedUtc, bool isStale)
        {
            if (OldestUtc == null || fetchedUtc < OldestUtc)
            {
                OldestUtc = fetchedUtc;
            }
            UsedStale |= isStale;
        }

        public void Held() => UsedHeld = true;

        // Reads made inside go straight to Terrain, whatever page they were started from, for work that is about to change Terrain:
        // "using var live = CacheScope.Outside();"
        public static IDisposable Outside()
        {
            var outer = Ambient.Value;
            Ambient.Value = null;
            return new Restore(outer);
        }

        // What an inner scope used counts towards the scope around it too
        public void Dispose()
        {
            if (_outer != null)
            {
                if (OldestUtc != null)
                {
                    _outer.Used(OldestUtc.Value, UsedStale);
                }
                _outer.UsedHeld |= UsedHeld;
                _outer.ToCheck |= ToCheck;
            }
            Ambient.Value = _outer;
        }

        private sealed class Restore : IDisposable
        {
            private readonly CacheScope? _scope;

            public Restore(CacheScope? scope)
            {
                _scope = scope;
            }

            public void Dispose() => Ambient.Value = _scope;
        }
    }
}
