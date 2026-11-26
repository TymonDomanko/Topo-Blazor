using System.Net;
using System.Text;

namespace Topo.Model.TestBuild
{
    // Sits in the HTTP client every Terrain request goes through. A change that is not allowed is never sent: it is answered here as refused.
    public class TestWriteGuardHandler : DelegatingHandler
    {
        private readonly Func<Task<TestWriteSettings>> _settings;
        private readonly Action<HttpRequestMessage> _heldBack;

        public TestWriteGuardHandler(Func<Task<TestWriteSettings>> settings, Action<HttpRequestMessage> heldBack)
        {
            _settings = settings;
            _heldBack = heldBack;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var method = request.Method.Method;
            // read before anything is awaited, while this is still the caller's work
            var yearsOfService = YearsOfServiceWrites.Active;
            if (!TestWriteRules.IsChange(method, request.RequestUri)
                || TestWriteRules.Allows(await _settings(), method, request.RequestUri, yearsOfService))
            {
                return await base.SendAsync(request, cancellationToken);
            }
            _heldBack(request);
            return new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                RequestMessage = request,
                // the message is plain text with nothing to escape
                Content = new StringContent($"{{\"message\":\"{TestWriteRules.BlockedMessage}\"}}", Encoding.UTF8, "application/json")
            };
        }
    }
}
