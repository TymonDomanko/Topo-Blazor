namespace Topo.Model.Cache
{
    // For a page that reads its data only when a report is made: how old what is held is, and reading it again on request. The report buttons wait
    // while it is.
    public class ReportRefresh : IDisposable
    {
        private readonly CancellationTokenSource _cancel = new CancellationTokenSource();

        public bool Updating { get; private set; }
        public bool Failed { get; private set; }
        // When the oldest of what is held for the page was read; null when nothing is
        public DateTime? ReadUtc { get; private set; }
        public string Progress { get; private set; } = "";
        // Called as the progress changes, for the page to draw it
        public Action? Changed { get; set; }

        public async Task ReadAgeAsync(Func<Task<DateTime?>> oldest) => ReadUtc = await oldest();

        // Makes the page's reads again (read is given somewhere to report its progress), then asks how old what is held now is
        public async Task RefreshAsync(Func<Action<string>, CancellationToken, Task> read, Func<Task<DateTime?>> oldest)
        {
            if (Updating)
            {
                return;
            }
            Updating = true;
            Failed = false;
            try
            {
                using (CacheScope.Begin(CacheMode.Reread, behindShownData: ReadUtc != null))
                {
                    await read(step =>
                    {
                        Progress = step;
                        Changed?.Invoke();
                    }, _cancel.Token);
                }
            }
            catch (OperationCanceledException)
            {
                // the page was left
            }
            catch (Exception)
            {
                Failed = true;
            }
            finally
            {
                await ReadAgeAsync(oldest);
                Updating = false;
                Progress = "";
            }
        }

        public void Dispose() => _cancel.Cancel();
    }
}
