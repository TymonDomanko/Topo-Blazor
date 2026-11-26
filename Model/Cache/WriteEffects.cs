namespace Topo.Model.Cache
{
    // Which kept reads a request that writes to Terrain may have made wrong, so they are dropped and read again when next wanted. It leans to
    // dropping too much: a write it does not know drops everything.
    public static class WriteEffects
    {
        private const string Members = "//members.terrain.scouts.com.au/";
        private const string Events = "//events.terrain.scouts.com.au/";
        private const string Achievements = "//achievements.terrain.scouts.com.au/";
        private const string AnyTerrain = ".terrain.scouts.com.au/";

        // Says, for a kept read's key, whether the request may have changed what it holds; null when the request changes nothing that is kept
        // (a read, signing in, choosing calendars, assuming a Scout's profile, or anything that is not Terrain's)
        public static Func<string, bool>? Of(string method, string requestUri)
        {
            if (string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase) || string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase)
                || string.Equals(method, "OPTIONS", StringComparison.OrdinalIgnoreCase) || !requestUri.Contains(AnyTerrain))
            {
                return null;
            }
            var path = requestUri.Split('?')[0].TrimEnd('/').Split('/');

            if (requestUri.Contains(Members))
            {
                if (path[^1] == "assume-profiles" || path[^1] == "revoke-assumed-profiles")
                {
                    return null;
                }
                // patrols, duties and unit council: the member lists and the figures worked out from them
                return key => key.Contains(Members);
            }

            if (requestUri.Contains(Events))
            {
                // the account's selected calendars: nothing kept depends on them
                if (path[^1] == "calendars")
                {
                    return null;
                }
                // /events/{id} and what hangs off it (comments)
                var at = Array.LastIndexOf(path, "events");
                var eventUri = at > 2 && at + 1 < path.Length ? $"{Events}events/{path[at + 1]}" : null;
                var isComment = path.Contains("comments");
                // changing or removing an event can change the credits Terrain works out from who came
                var changesCredits = eventUri != null && !isComment;
                // a change to an event can change any kept list; a comment changes what is kept of that event's comments
                return key => key.Contains("/proposals")
                    || (!isComment && key.Contains("|list|"))
                    || (isComment && eventUri != null && key.EndsWith($"{eventUri}/comments", StringComparison.Ordinal))
                    || (eventUri != null && key.EndsWith(eventUri, StringComparison.Ordinal))
                    || (changesCredits && (IsAchievement(key) || key.EndsWith("/metrics", StringComparison.Ordinal)));
            }

            if (requestUri.Contains(Achievements))
            {
                var logbook = Array.IndexOf(path, "logbook");
                if (logbook > 0)
                {
                    var memberId = path[logbook - 1];
                    return key => key.Contains($"{Achievements}members/{memberId}/logbook") || key.EndsWith($"|logbook-totals|{memberId}", StringComparison.Ordinal);
                }
                // awards, approvals and records: any unit's or Scout's records and submissions may show it, and so may the unit's figures
                return key => IsAchievement(key) || key.EndsWith("/metrics", StringComparison.Ordinal);
            }

            return key => true;
        }

        // A kept read of records or submissions (logbooks are written to on their own)
        private static bool IsAchievement(string key) => key.Contains(Achievements) && !key.Contains("/logbook");
    }
}
