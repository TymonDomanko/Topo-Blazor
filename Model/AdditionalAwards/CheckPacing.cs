namespace Topo.Model.AdditionalAwards
{
    // How the logbook check behaves when it runs in the background: it waits for a gap between the leader's own requests before each
    // Scout, and stops at once, handing the Scout's profile back, if a page needs Terrain. The check on the page itself passes none.
    public interface ICheckPacing
    {
        // Waits for a gap before the next Scout; throws OperationCanceledException if the run is cancelled
        Task WaitForGapAsync();
        // Throws OperationCanceledException if a page needs Terrain, and the Scout's profile should be handed back
        void ThrowIfPageNeedsTerrain();
        void ProfileTakenOn();
        void ProfileReturned();
    }

    public class MonitorPacing : ICheckPacing
    {
        private readonly ActivityMonitor _monitor;
        private readonly CancellationToken _cancel;

        public MonitorPacing(ActivityMonitor monitor, CancellationToken cancel = default)
        {
            _monitor = monitor;
            _cancel = cancel;
        }

        public Task WaitForGapAsync() => _monitor.WaitForQuietAsync(AutoAwardSchedule.QuietFor, _cancel);

        public void ThrowIfPageNeedsTerrain()
        {
            _cancel.ThrowIfCancellationRequested();
            _monitor.ThrowIfAbortRequested();
        }

        public void ProfileTakenOn() => _monitor.ProfileTakenOn();

        public void ProfileReturned() => _monitor.ProfileReturned();
    }
}
