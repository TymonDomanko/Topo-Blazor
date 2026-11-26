using Microsoft.JSInterop;
using Topo.Model.Cache;

namespace Topo.Services
{
    // Keeps the data cache in the browser's IndexedDB (wwwroot/js/dataCache.js), which holds far more than local storage. If the browser will not
    // let it be used, nothing is kept and the pages read from Terrain as they always did.
    public class IndexedDbCacheStore : ICacheStore
    {
        private readonly IJSRuntime _js;

        public IndexedDbCacheStore(IJSRuntime js)
        {
            _js = js;
        }

        public async Task<CacheEntry?> GetAsync(string key)
        {
            try
            {
                return await _js.InvokeAsync<CacheEntry?>("topoCache.get", key);
            }
            catch (JSException)
            {
                return null;
            }
        }

        public Task PutAsync(CacheEntry entry) => Try("topoCache.put", entry);

        public Task ClearAsync() => Try("topoCache.clear");

        public Task RemoveFetchedBeforeAsync(long fetchedAt) => Try("topoCache.removeFetchedBefore", fetchedAt);

        private async Task Try(string function, params object[] arguments)
        {
            try
            {
                await _js.InvokeVoidAsync(function, arguments);
            }
            catch (JSException)
            {
                // the browser's store cannot be used: carry on without it
            }
        }
    }
}
