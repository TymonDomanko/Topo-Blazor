using Topo.Model.AdditionalAwards;
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
                return await base.SendAsync(request, cancellationToken);

            if (_spinnerService.IsQuiet)
                return await base.SendAsync(request, cancellationToken);

            _spinnerService.Show();
            try
            {
                // A page's request goes first: if background work has a Scout's profile taken on, this waits only for it to be handed back
                await _activityMonitor.PageRequestStartingAsync();
                return await base.SendAsync(request, cancellationToken);
            }
            finally
            {
                _activityMonitor.RequestEnded();
                _spinnerService.Hide();
            }
        }
    }
}
