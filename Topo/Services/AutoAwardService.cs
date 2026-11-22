using Blazored.LocalStorage;
using Topo.Model.AdditionalAwards;

namespace Topo.Services
{
    public interface IAutoAwardService
    {
        AutoAwardSettings Settings { get; }
        bool IsRunning { get; }
        // Raised when the status changes, so a page showing it can redraw
        event Action? Changed;
        // Starts watching for a quiet moment. Safe to call more than once.
        void Start();
        Task SetEnabledAsync(bool enabled);
    }

    // Keeps the list of Camper and Walkabout awards to hand over up to date with nobody doing anything but having Topo open: about once
    // a day, when the leader has not used Topo for a little while, it checks every Scout's logbook for awards Terrain has no record of. The
    // badge to hand over is only listed: it goes into Terrain when a leader presents it. The one thing it writes is the lower levels of a
    // kind when a higher level is already recorded in Terrain, since those were awarded too.
    //
    // Pages come first. It only starts a Scout in a gap between the leader's own requests, and if a page needs Terrain while a Scout's
    // profile is taken on it hands the profile back at once and stops, to try again a few minutes later. It runs only while Topo is open
    // in a browser; there is nothing else to run.
    public class AutoAwardService : IAutoAwardService
    {
        private static readonly TimeSpan Tick = TimeSpan.FromSeconds(5);

        private readonly StorageService _storageService;
        private readonly ILocalStorageService _localStorage;
        private readonly IAdditionalAwardCheckService _checkService;
        private readonly ActivityMonitor _activityMonitor;
        private bool _started;
        private string _loadedGroup = "";

        public AutoAwardService(StorageService storageService, ILocalStorageService localStorage, IAdditionalAwardCheckService checkService, ActivityMonitor activityMonitor)
        {
            _storageService = storageService;
            _localStorage = localStorage;
            _checkService = checkService;
            _activityMonitor = activityMonitor;
        }

        public AutoAwardSettings Settings { get; private set; } = new AutoAwardSettings();
        public bool IsRunning { get; private set; }
        public event Action? Changed;

        private string Key => $"TopoAutoAwards:{_storageService.GroupId}";

        public void Start()
        {
            if (_started)
                return;
            _started = true;
            _ = LoopAsync();
        }

        public async Task SetEnabledAsync(bool enabled)
        {
            await LoadAsync();
            Settings.Enabled = enabled;
            // Turned on, it is due straight away (the next quiet moment)
            if (enabled)
                Settings.LastAttemptUtc = null;
            await SaveAsync();
            Changed?.Invoke();
        }

        private async Task LoopAsync()
        {
            while (true)
            {
                await Task.Delay(Tick);
                try
                {
                    await TickAsync();
                }
                catch (Exception)
                {
                    // Nothing is allowed to stop the loop; the run records its own failures
                }
            }
        }

        private async Task TickAsync()
        {
            if (!_storageService.IsAuthenticated || _storageService.IsYouthMember || string.IsNullOrEmpty(_storageService.GroupId))
            {
                _loadedGroup = "";
                return;
            }
            await LoadAsync();
            if (IsRunning || !AutoAwardSchedule.IsDue(Settings, DateTime.UtcNow) || !_activityMonitor.IsIdleFor(AutoAwardSchedule.QuietFor))
                return;
            await RunAsync();
        }

        private async Task LoadAsync()
        {
            var group = _storageService.GroupId ?? "";
            if (_loadedGroup == group)
                return;
            Settings = await _localStorage.GetItemAsync<AutoAwardSettings>(Key) ?? new AutoAwardSettings();
            _loadedGroup = group;
            Changed?.Invoke();
        }

        private async Task SaveAsync() => await _localStorage.SetItemAsync(Key, Settings);

        private async Task RunAsync()
        {
            IsRunning = true;
            Settings.LastAttemptUtc = DateTime.UtcNow;
            Settings.LastError = "";
            await SaveAsync();
            Changed?.Invoke();
            using var cancel = new CancellationTokenSource();
            try
            {
                // Everything inside here is background work: no spinner, and not counted as the leader using Topo
                using (_activityMonitor.EnterBackground())
                {
                    var pacing = new MonitorPacing(_activityMonitor, cancel.Token);
                    var result = await _checkService.ScanAsync(_storageService.Units.Keys.ToList(), null, pacing);
                    if (result.Stopped)
                    {
                        Settings.LastSummary = "Paused because Topo was in use; it will try again shortly.";
                        return;
                    }

                    Settings.LastSummary = $"Checked {result.MembersChecked} Scouts: {result.ToBePresented} awards to hand over" +
                        (result.NotHighest > 0 ? $" and {result.NotHighest} more covered by a higher level" : "") +
                        (result.LowerLevelsRecorded > 0 ? $"; recorded {result.LowerLevelsRecorded} lower levels in Terrain" : "") +
                        (result.MembersNotRead.Count > 0 ? $"; could not read {result.MembersNotRead.Count} Scouts" : "") + ".";
                    if (result.Failures.Count > 0)
                        Settings.LastError = $"{result.Failures.Count} could not be recorded: {result.Failures[0]}";
                    else
                        Settings.LastRunUtc = DateTime.UtcNow;
                }
            }
            catch (Exception ex)
            {
                Settings.LastError = "The automatic check failed: " + ex.Message;
            }
            finally
            {
                IsRunning = false;
                await SaveAsync();
                Changed?.Invoke();
            }
        }
    }
}
