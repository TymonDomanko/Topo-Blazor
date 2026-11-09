namespace Topo.Services
{
    public class SpinnerService
    {
        private static readonly AsyncLocal<bool> Quietly = new AsyncLocal<bool>();

        // Work that carries on in the background, and tells the page what it is doing itself, calls this so that its requests do not
        // cover the page with the spinner. It lasts for the rest of that work only.
        public void BeQuiet() => Quietly.Value = true;
        public bool IsQuiet => Quietly.Value;

        public event Action OnShow;
        public event Action OnHide;

        public void Show()
        {
            OnShow?.Invoke();
        }

        public void Hide()
        {
            OnHide?.Invoke();
        }
    }
}
