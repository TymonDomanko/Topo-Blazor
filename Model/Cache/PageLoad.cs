namespace Topo.Model.Cache
{
    // Loads what a page shows through the data cache: what is held shows at once, and if any of it is older than a day it is then read again
    // behind the page. A later load (another unit picked, the page left) overtakes an earlier one, which then changes nothing on the page.
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

        // read: gets the page's data, reporting its progress; show: puts it on the page
        public Task LoadAsync<T>(Func<Action<string>, CancellationToken, Task<T>> read, Action<T> show)
        {
            var version = Start();
            ReadUtc = null;
            Loading = true;
            return RunAsync(version, async token =>
            {
                var stale = await PassAsync(version, CacheMode.Held, read, show, token);
                if (stale && version == _version)
                {
                    Loading = false;
                    Updating = true;
                    Changed?.Invoke();
                    await PassAsync(version, CacheMode.Reread, read, show, token);
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

        private async Task<bool> PassAsync<T>(int version, CacheMode mode, Func<Action<string>, CancellationToken, Task<T>> read, Action<T> show,
            CancellationToken token)
        {
            using var scope = CacheScope.Begin(mode);
            var data = await read(step => Report(version, step), token);
            if (version != _version)
            {
                return false;
            }
            show(data);
            ReadUtc = scope.OldestUtc;
            return scope.UsedStale;
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
