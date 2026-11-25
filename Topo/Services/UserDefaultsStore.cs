using Blazored.LocalStorage;
using Topo.Model.Defaults;
using Topo.Model.Program;

namespace Topo.Services
{
    // Reads and keeps the defaults chosen in Settings (Model/Defaults/UserDefaults.cs), in this browser's local storage
    public static class UserDefaultsStore
    {
        private const string Key = "TopoDefaults";
        // Where the sign in sheet page keeps the options it was last used with
        private const string SignInOptionsKey = "SignInSheetOptions";

        // Storage that can't be read back is the same as no defaults having been chosen
        public static async Task<UserDefaults?> ReadAsync(ILocalStorageService localStorage)
        {
            try
            {
                return await localStorage.GetItemAsync<UserDefaults>(Key);
            }
            catch (Exception)
            {
                return null;
            }
        }

        // Starts a session with the chosen defaults: the full names tick on every page that has one
        public static async Task ApplyAsync(ILocalStorageService localStorage, StorageService storage)
        {
            var defaults = await ReadAsync(localStorage);
            if (defaults != null)
            {
                storage.UseFullNames = defaults.UseFullNames;
            }
        }

        // Keeps the defaults and puts them to use now: this session's full names setting, and the options the sign in sheet starts from
        public static async Task SaveAsync(ILocalStorageService localStorage, StorageService storage, UserDefaults defaults)
        {
            defaults.SignIn.UseFullNames = defaults.UseFullNames;
            await localStorage.SetItemAsync(Key, defaults);
            await localStorage.SetItemAsync(SignInOptionsKey, defaults.SignIn);
            storage.UseFullNames = defaults.UseFullNames;
        }
    }
}
