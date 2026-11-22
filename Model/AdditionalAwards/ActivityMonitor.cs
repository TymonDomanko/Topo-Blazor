namespace Topo.Model.AdditionalAwards
{
    // Tells whether the leader is using Topo, so work meant for quiet moments (the automatic Camper and Walkabout check) only runs when
    // nothing else is happening and stops as soon as the leader does something. Terrain keeps one Scout's profile taken on at a time,
    // so that work must never overlap what the leader is doing.
    //
    // Requests the leader's pages make are counted. Requests the background work makes itself are not (they are made inside
    // EnterBackground), so it does not see itself as activity.
    public class ActivityMonitor
    {
        private static readonly AsyncLocal<bool> Background = new AsyncLocal<bool>();
        private readonly Func<DateTime> _now;
        private int _inFlight;
        private DateTime _lastActivityUtc;

        public ActivityMonitor() : this(() => DateTime.UtcNow)
        {
        }

        public ActivityMonitor(Func<DateTime> utcNow)
        {
            _now = utcNow;
            _lastActivityUtc = utcNow();
        }

        // Goes up each time the leader does something, so work that started earlier can tell it should stop
        public int Version { get; private set; }

        public bool InBackground => Background.Value;

        // Everything awaited inside the scope counts as background work
        public IDisposable EnterBackground()
        {
            var was = Background.Value;
            Background.Value = true;
            return new Scope(() => Background.Value = was);
        }

        // A page asked Terrain for something (not counted when it is background work)
        public void RequestStarted()
        {
            if (InBackground)
                return;
            _inFlight++;
            Touch();
        }

        public void RequestEnded()
        {
            if (InBackground)
                return;
            if (_inFlight > 0)
                _inFlight--;
            Touch();
        }

        // The leader went to another page, or did something else that is not a request
        public void UserActivity() => Touch();

        // Pages come first. While background work has a Scout's profile taken on, a page's request would be answered as that Scout,
        // so it is held for the moment the background work needs to hand the profile back. Anything else goes straight through.
        private TaskCompletionSource? _profileReturned;

        // True when a page needs Terrain while background work has a profile taken on; the background work stops and hands it back
        public bool AbortRequested { get; private set; }

        // Called for each request a page makes, before it is sent. Counts it, and if background work has a profile taken on, asks it to
        // stop and waits (at most a few seconds) until it has handed the profile back.
        public async Task PageRequestStartingAsync()
        {
            if (InBackground)
                return;
            RequestStarted();
            var returned = _profileReturned;
            if (returned == null)
                return;
            AbortRequested = true;
            await Task.WhenAny(returned.Task, Task.Delay(MaxHold));
        }

        public static readonly TimeSpan MaxHold = TimeSpan.FromSeconds(4);

        // The background work has taken a Scout's profile on
        public void ProfileTakenOn()
        {
            AbortRequested = false;
            _profileReturned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        // The background work has handed the profile back
        public void ProfileReturned()
        {
            AbortRequested = false;
            _profileReturned?.TrySetResult();
            _profileReturned = null;
        }

        // Waits until nothing has happened for the quiet time, so background work only starts a Scout in a gap between page requests
        public async Task WaitForQuietAsync(TimeSpan quiet, CancellationToken cancel)
        {
            while (!IsIdleFor(quiet))
                await Task.Delay(250, cancel);
        }

        public void ThrowIfAbortRequested()
        {
            if (AbortRequested)
                throw new OperationCanceledException("A page needs Terrain.");
        }

        // True when no request is running and nothing has happened for the given time
        public bool IsIdleFor(TimeSpan quiet) => _inFlight == 0 && _now() - _lastActivityUtc >= quiet;

        private void Touch()
        {
            _lastActivityUtc = _now();
            Version++;
        }

        private class Scope : IDisposable
        {
            private readonly Action _restore;

            public Scope(Action restore)
            {
                _restore = restore;
            }

            public void Dispose() => _restore();
        }
    }

    // What Topo remembers, in this browser, about the automatic check for one group
    public class AutoAwardSettings
    {
        // On unless a leader turns it off in Settings, so it needs nobody to do anything but open Topo. It only reads from Terrain.
        public bool Enabled { get; set; } = true;
        public DateTime? LastRunUtc { get; set; }
        public DateTime? LastAttemptUtc { get; set; }
        public string LastSummary { get; set; } = "";
        public string LastError { get; set; } = "";
    }

    public static class AutoAwardSchedule
    {
        public static readonly TimeSpan Every = TimeSpan.FromHours(24);
        // After a run that stopped or failed, the next try waits this long
        public static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(5);
        // Nothing has been going on for this long before it starts, and before it starts each Scout. Short, because a page's own request
        // always goes first anyway; this is only the pause after one before background work carries on.
        public static readonly TimeSpan QuietFor = TimeSpan.FromSeconds(3);

        public static bool IsDue(AutoAwardSettings settings, DateTime nowUtc)
        {
            if (!settings.Enabled)
                return false;
            if (settings.LastAttemptUtc.HasValue && nowUtc - settings.LastAttemptUtc.Value < RetryAfter)
                return false;
            return !settings.LastRunUtc.HasValue || nowUtc - settings.LastRunUtc.Value >= Every;
        }
    }
}
