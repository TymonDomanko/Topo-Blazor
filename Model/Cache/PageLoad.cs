namespace Topo.Model.Cache
{
    // Loads what a page shows through the data cache: what is held shows at once, and if any of it is older than a day, or is read every time (a
    // list of events), it is then read again behind the page: what is in date serves, the rest comes from Terrain. A later load (another unit
    // picked, the page left) overtakes an earlier one, which then changes nothing on the page.
    public class PageLoad : IDisposable
    {
        private int _version;
        private CancellationTokenSource? _cancel;

        // Reading with nothing to show yet
        public bool Loading { get; private set; }
        // Reading again behind what is shown; reports wait until it is done
        public bool Updating { get; private set; }
        // The last load or refresh could not be completed
        public bool Failed { get; private set; }
        // When the oldest read behind what is shown was made
        public DateTime? ReadUtc { get; private set; }
        // How far through the read is
        public string Progress { get; private set; } = "";
        // Called when any of these change while a read is under way, for the page to draw them
        public Action? Changed { get; set; }

        // read: gets the page's data, reporting its progress; show: puts it on the page. alwaysUpdate: a page that acts on what it shows reads
        // it again behind the page whenever any of it was held, however recently
        public Task LoadAsync<T>(Func<Action<string>, CancellationToken, Task<T>> read, Action<T> show, bool alwaysUpdate = false)
        {
            var version = Start();
            ReadUtc = null;
            Loading = true;
            return RunAsync(version, async token =>
            {
                var (stale, held) = await PassAsync(version, CacheMode.Held, read, show, token);
                if ((stale || (alwaysUpdate && held)) && version == _version)
                {
                    Loading = false;
                    Updating = true;
                    // the first pass's progress does not stand for the second
                    Progress = "";
                    Changed?.Invoke();
                    // a page that acts on what it shows reads it all again
                    await PassAsync(version, alwaysUpdate ? CacheMode.Reread : CacheMode.Fresh, read, show, token);
                }
            });
        }

        // Reads everything again, leaving what is shown in place until the new data is in. Nothing happens while a read is already under way.
        public Task RefreshAsync<T>(Func<Action<string>, CancellationToken, Task<T>> read, Action<T> show)
        {
            if (Loading || Updating)
            {
                return Task.CompletedTask;
            }
            var version = Start();
            Updating = true;
            return RunAsync(version, token => PassAsync(version, CacheMode.Reread, read, show, token));
        }

        // Abandons any read under way
        public void Cancel()
        {
            Start();
            ReadUtc = null;
        }

        public void Dispose() => Cancel();

        private async Task<(bool Stale, bool Held)> PassAsync<T>(int version, CacheMode mode, Func<Action<string>, CancellationToken, Task<T>> read, Action<T> show,
            CancellationToken token)
        {
            // while updating, what was held is on show
            using var scope = CacheScope.Begin(mode, behindShownData: Updating);
            var data = await read(step => Report(version, step), token);
            if (version != _version)
            {
                return (false, false);
            }
            show(data);
            ReadUtc = scope.OldestUtc;
            return (scope.UsedStale || scope.ToCheck, scope.UsedHeld);
        }

        private async Task RunAsync(int version, Func<CancellationToken, Task> work)
        {
            Changed?.Invoke();
            try
            {
                await work(_cancel!.Token);
            }
            catch (OperationCanceledException)
            {
                // overtaken by a later load, or the page was left
            }
            catch (Exception)
            {
                if (version == _version)
                {
                    Failed = true;
                }
            }
            finally
            {
                if (version == _version)
                {
                    Loading = false;
                    Updating = false;
                    Progress = "";
                    Changed?.Invoke();
                }
            }
        }

        private int Start()
        {
            _cancel?.Cancel();
            _cancel = new CancellationTokenSource();
            Loading = false;
            Updating = false;
            Failed = false;
            Progress = "";
            return ++_version;
        }

        private void Report(int version, string step)
        {
            if (version == _version)
            {
                Progress = step;
                Changed?.Invoke();
            }
        }
    }
}
