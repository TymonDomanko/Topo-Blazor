namespace Topo.Model.Common
{
    // Runs a read a moment after the last thing the person changed, in place of a button to press. A read that is still going is
    // told to stop, and the new one starts only once it has, so two never use the unit's shared calendar at the same time.
    public sealed class AutoRun : IDisposable
    {
        private CancellationTokenSource? _current;
        private Task _previous = Task.CompletedTask;

        public Task RunAsync(Func<CancellationToken, Task> work, int delayMilliseconds = 0)
        {
            _current?.Cancel();
            var source = _current = new CancellationTokenSource();
            var run = RunAfter(_previous, source.Token, work, delayMilliseconds);
            _previous = run;
            return run;
        }

        // Stops what is running without starting anything, for when the unit or options no longer give anything to read
        public void Stop() => _current?.Cancel();

        private static async Task RunAfter(Task previous, CancellationToken token, Func<CancellationToken, Task> work, int delayMilliseconds)
        {
            // a read that failed must not stop the ones after it
            try
            {
                await previous;
            }
            catch (Exception)
            {
            }
            try
            {
                if (delayMilliseconds > 0)
                    await Task.Delay(delayMilliseconds, token);
                token.ThrowIfCancellationRequested();
                await work(token);
            }
            catch (OperationCanceledException)
            {
            }
        }

        public void Dispose() => _current?.Cancel();
    }
}
