using Blazored.LocalStorage;
using Topo.Model.TestBuild;

namespace Topo.Services
{
    // Test build only: the switches for changing Terrain (kept in this browser), and what was last held back, for the notice on the page
    public class TestWriteGuard
    {
        private const string Key = "TopoTestWrites";
        private readonly ILocalStorageService _localStorage;
        private TestWriteSettings? _settings;

        public TestWriteGuard(ILocalStorageService localStorage)
        {
            _localStorage = localStorage;
        }

        // What was held back since the notice was last closed, such as "PUT achievements.terrain.scouts.com.au/units/…"
        public List<string> HeldBack { get; } = new List<string>();
        public event Action? Changed;

        public async Task<TestWriteSettings> SettingsAsync()
        {
            if (_settings == null)
            {
                try
                {
                    _settings = await _localStorage.GetItemAsync<TestWriteSettings>(Key);
                }
                catch (Exception)
                {
                    // a setting that cannot be read leaves changes held back
                }
                _settings ??= new TestWriteSettings();
            }
            return _settings;
        }

        public async Task SaveAsync() => await _localStorage.SetItemAsync(Key, await SettingsAsync());

        public void Report(HttpRequestMessage request)
        {
            HeldBack.Add($"{request.Method} {request.RequestUri?.Host}{request.RequestUri?.AbsolutePath}");
            Changed?.Invoke();
        }

        public void Dismiss()
        {
            HeldBack.Clear();
            Changed?.Invoke();
        }
    }
}
