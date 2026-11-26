using System.Text.Json;

namespace Topo.Model.Cache
{
    // Events that ended more than four months ago rarely change, so what was read of one is kept until the page's data is read again on request
    public static class OldEvents
    {
        private const int Months = 4;

        // Whether Terrain's answer for one event is of an event that ended that long before the given time. An answer without an end time is not.
        public static bool IsOld(string eventJson, DateTime nowUtc)
        {
            try
            {
                using var document = JsonDocument.Parse(eventJson);
                return document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty("end_datetime", out var end)
                    && end.TryGetDateTime(out var ended)
                    && ended.ToUniversalTime() < nowUtc.AddMonths(-Months);
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }
}
