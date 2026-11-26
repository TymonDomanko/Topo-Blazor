using Blazored.LocalStorage;
using Topo.Model.Cache;

namespace Topo.Services
{
    // Whether this browser keeps what pages read (the data cache). It does unless a person turns it off on the Settings page.
    public static class CacheSetting
    {
        private const string Key = "TopoDataCache";
        private const string Off = "off";

        // Sets the cache up as chosen; storage that can't be read is the same as nothing having been chosen
        public static async Task ApplyAsync(ILocalStorageService localStorage, DataCache dataCache)
        {
            try
            {
                dataCache.Enabled = await localStorage.GetItemAsync<string>(Key) != Off;
            }
            catch (Exception)
            {
                dataCache.Enabled = true;
            }
        }

        // Keeps the choice and puts it to use; turning the cache off removes what it held
        public static async Task SaveAsync(ILocalStorageService localStorage, DataCache dataCache, bool enabled)
        {
            await localStorage.SetItemAsync(Key, enabled ? "on" : Off);
            if (!enabled)
            {
                await dataCache.ClearAsync();
            }
            dataCache.Enabled = enabled;
        }
    }
}
