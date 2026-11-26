using Topo.Model.Cache;

namespace Topo.Services
{
    // Sits in the HTTP client every Terrain request goes through, so that whatever writes to Terrain, by whatever route, what the data cache held
    // of it is dropped (Model/Cache/WriteEffects.cs) before the writer carries on.
    public class CacheInvalidationHandler : DelegatingHandler
    {
        private readonly DataCache _dataCache;
        private readonly StorageService _storageService;

        public CacheInvalidationHandler(DataCache dataCache, StorageService storageService)
        {
            _dataCache = dataCache;
            _storageService = storageService;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri?.AbsoluteUri ?? "";
            var affected = WriteEffects.Of(request.Method.Method, uri);
            try
            {
                return await base.SendAsync(request, cancellationToken);
            }
            finally
            {
                // also when it failed: a request that got no answer may still have been carried out
                if (affected != null)
                {
                    await _dataCache.ForgetAsync(affected);
                    if (uri.Contains("//members.terrain.scouts.com.au/"))
                    {
                        // the member lists held for the session are out of date too
                        _storageService.CachedMembers.Clear();
                    }
                }
            }
        }
    }
}
