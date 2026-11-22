namespace Topo.Services
{
    public class SpinnerService
    {
        // How long to wait after the last request ends before taking the spinner away. A page that reads from Terrain one request
        // after another (each Scout in a unit, say) keeps the one spinner, so its animation carries on instead of starting again
        // each time a request finishes and the next begins.
        private const int HideDelayMilliseconds = 400;

        private int _requests;
        private bool _visible;
        // Changes whenever a request starts or ends, so a hide that was waiting can tell it is out of date
        private int _version;

        private static readonly AsyncLocal<bool> Quietly = new AsyncLocal<bool>();

        // Work that carries on in the background, and tells the page what it is doing itself, calls this so that its requests do not
        // cover the page with the spinner. It lasts for the rest of that work only.
        public void BeQuiet() => Quietly.Value = true;
        public bool IsQuiet => Quietly.Value;

        public event Action? OnShow;
        public event Action? OnHide;

        public void Show()
        {
            _requests++;
            _version++;
            if (!_visible)
            {
                _visible = true;
                OnShow?.Invoke();
            }
        }

        public void Hide()
        {
            if (_requests > 0)
                _requests--;
            // other requests are still running, or there is nothing showing
            if (_requests > 0 || !_visible)
                return;

            var version = ++_version;
            _ = HideAfterDelay(version);
        }

        private async Task HideAfterDelay(int version)
        {
            await Task.Delay(HideDelayMilliseconds);
            if (version == _version && _requests == 0 && _visible)
            {
                _visible = false;
                OnHide?.Invoke();
            }
        }
    }
}
