using Microsoft.JSInterop;

namespace Topo.Services
{
    // Terrain holds one assumed profile, and one set of selected calendars, for an account at a time. Whatever switches either, or relies on it
    // staying put, holds the matching lock while it does: within this tab, and against Topo's other tabs in the browser.
    public interface ITerrainLocks
    {
        Task<IDisposable> ProfileAsync(CancellationToken cancellationToken = default);
        Task<IDisposable> CalendarsAsync(CancellationToken cancellationToken = default);
    }

    public class TerrainLocks : ITerrainLocks
    {
        private readonly IJSRuntime _js;
        private readonly SemaphoreSlim _profile = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _calendars = new SemaphoreSlim(1, 1);

        public TerrainLocks(IJSRuntime js)
        {
            _js = js;
        }

        public Task<IDisposable> ProfileAsync(CancellationToken cancellationToken = default) => AcquireAsync(_profile, "profile", cancellationToken);

        public Task<IDisposable> CalendarsAsync(CancellationToken cancellationToken = default) => AcquireAsync(_calendars, "calendars", cancellationToken);

        private async Task<IDisposable> AcquireAsync(SemaphoreSlim own, string name, CancellationToken cancellationToken)
        {
            await own.WaitAsync(cancellationToken);
            var shared = 0;
            try
            {
                shared = await _js.InvokeAsync<int>("topoLock.acquire", name);
            }
            catch (JSException)
            {
                // the browser cannot share a lock between tabs: this tab's own still holds
            }
            var release = new Release(this, own, shared);
            if (cancellationToken.IsCancellationRequested)
            {
                release.Dispose();
                cancellationToken.ThrowIfCancellationRequested();
            }
            return release;
        }

        private sealed class Release : IDisposable
        {
            private readonly TerrainLocks _locks;
            private readonly int _shared;
            private SemaphoreSlim? _own;

            public Release(TerrainLocks locks, SemaphoreSlim own, int shared)
            {
                _locks = locks;
                _own = own;
                _shared = shared;
            }

            public void Dispose()
            {
                var own = Interlocked.Exchange(ref _own, null);
                if (own == null)
                {
                    return;
                }
                if (_shared > 0)
                {
                    // nothing waits on the answer
                    _ = _locks._js.InvokeVoidAsync("topoLock.release", _shared).AsTask();
                }
                own.Release();
            }
        }
    }
}
