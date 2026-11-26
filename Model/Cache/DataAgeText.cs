using System.Globalization;

namespace Topo.Model.Cache
{
    // How a page says when its data was read
    public static class DataAgeText
    {
        // "today 9:42am", "yesterday 4:10pm" or "3 days ago", from local times
        public static string When(DateTime readLocal, DateTime nowLocal)
        {
            var days = (nowLocal.Date - readLocal.Date).Days;
            if (days > 1)
            {
                return $"{days} days ago";
            }
            var time = readLocal.ToString("h:mmtt", CultureInfo.InvariantCulture).ToLowerInvariant();
            return days == 1 ? $"yesterday {time}" : $"today {time}";
        }
    }
}
