using Topo.Model.AdditionalAwards;
using Topo.Model.Cache;
using Topo.Services;

namespace Topo
{
    public class DisplaySpinnerAutomaticallyHttpMessageHandler : DelegatingHandler
    {
        private readonly SpinnerService _spinnerService;
        private readonly ActivityMonitor _activityMonitor;
        public DisplaySpinnerAutomaticallyHttpMessageHandler(SpinnerService spinnerService, ActivityMonitor activityMonitor)
        {
            _spinnerService = spinnerService;
            _activityMonitor = activityMonitor;
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Work Topo does in the background (the automatic Camper and Walkabout check) shows no spinner and is not counted as the
            // leader using Topo
            if (_activityMonitor.InBackground)
            {
                return await base.SendAsync(request, cancellationToken);
            }

            if (_spinnerService.IsQuiet)
            {
                return await base.SendAsync(request, cancellationToken);
            }

            // a page showing what was held while it is read again says so itself, so the spinner is not put over it
            var showSpinner = !CacheScope.IsBehindShownData;
            if (showSpinner)
            {
                _spinnerService.Show();
            }
            try
            {
                // A page's request goes first: if background work has a Scout's profile taken on, this waits only for it to be handed back
                await _activityMonitor.PageRequestStartingAsync();
                return await base.SendAsync(request, cancellationToken);
            }
            finally
            {
                _activityMonitor.RequestEnded();
                if (showSpinner)
                {
                    _spinnerService.Hide();
                }
            }
        }
    }
}
