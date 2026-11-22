using Topo.Model.BadgeStore;
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

            try
            {
                _spinnerService.Show();
                // A page's request goes first: if background work has a Scout's profile taken on, this waits only for it to be handed back
                await _activityMonitor.PageRequestStartingAsync();
                //  await Task.Delay(1000);
                var response = await base.SendAsync(request, cancellationToken);
                _activityMonitor.RequestEnded();
                _spinnerService.Hide();
                return response;
            }
            catch (Exception ex)
            {
                _activityMonitor.RequestEnded();
                _spinnerService.Hide();
                throw;
            }
        }
    }
}
