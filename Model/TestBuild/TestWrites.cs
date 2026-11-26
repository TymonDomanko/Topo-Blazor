namespace Topo.Model.TestBuild
{
    // Test build only (the everything-live branch): changes to Terrain are held back unless they are allowed in Settings.
    // None of this belongs on the live site.
    public class TestWriteSettings
    {
        // Every change Topo can make in Terrain
        public bool AllowWrites { get; set; }
        // Years of service has its own switch, so it can be tried while everything else is held back
        public bool AllowYearsOfService { get; set; } = true;
    }

    // Marks what is sent to Terrain inside it as part of the years of service workflow: "using var writes = YearsOfServiceWrites.Begin();"
    public sealed class YearsOfServiceWrites : IDisposable
    {
        private static readonly AsyncLocal<bool> Ambient = new AsyncLocal<bool>();
        private readonly bool _outer;

        private YearsOfServiceWrites()
        {
            _outer = Ambient.Value;
            Ambient.Value = true;
        }

        public static bool Active => Ambient.Value;

        public static YearsOfServiceWrites Begin() => new YearsOfServiceWrites();

        public void Dispose() => Ambient.Value = _outer;
    }

    public static class TestWriteRules
    {
        public const string BlockedMessage = "Changes to Terrain are turned off on this test build. Allow them in Settings.";

        // Whether a request would change something in Terrain. Reading does not count, and nor does what reading needs: choosing which calendars
        // are switched on, and taking on and giving back a Scout's profile.
        public static bool IsChange(string method, Uri? uri)
        {
            if (uri == null || !uri.Host.EndsWith(".terrain.scouts.com.au", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            if (method.Equals("GET", StringComparison.OrdinalIgnoreCase) || method.Equals("HEAD", StringComparison.OrdinalIgnoreCase)
                || method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            var path = uri.AbsolutePath.TrimEnd('/').Split('/');
            if (IsHost(uri, "events") && method.Equals("PUT", StringComparison.OrdinalIgnoreCase) && path[^1] == "calendars")
            {
                return false;
            }
            return !(IsHost(uri, "members") && method.Equals("POST", StringComparison.OrdinalIgnoreCase)
                && (path[^1] == "assume-profiles" || path[^1] == "revoke-assumed-profiles"));
        }

        public static bool Allows(TestWriteSettings settings, string method, Uri? uri, bool yearsOfService) =>
            !IsChange(method, uri) || settings.AllowWrites
            || (settings.AllowYearsOfService && yearsOfService && IsRecordsEventChange(method, uri!));

        // What years of service sends: creating the group's records event, and adding and removing comments on it
        private static bool IsRecordsEventChange(string method, Uri uri)
        {
            if (!IsHost(uri, "events"))
            {
                return false;
            }
            var path = uri.AbsolutePath.Trim('/').Split('/');
            var post = method.Equals("POST", StringComparison.OrdinalIgnoreCase);
            var newEvent = post && path.Length == 3 && path[0] == "groups" && path[2] == "events";
            var newComment = post && path.Length == 3 && path[0] == "events" && path[2] == "comments";
            var removedComment = method.Equals("DELETE", StringComparison.OrdinalIgnoreCase) && path.Length == 4 && path[0] == "events" && path[2] == "comments";
            return newEvent || newComment || removedComment;
        }

        private static bool IsHost(Uri uri, string name) => uri.Host.StartsWith($"{name}.", StringComparison.OrdinalIgnoreCase);
    }
}
