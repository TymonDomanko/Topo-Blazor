namespace Topo.Model
{
    // A page that asks for the same thing twice (the attendance figure looks back 89 days, and when that finds too few Scout nights, 180 days, which
    // contains the first look) should not ask Terrain twice. This reads each item once: an item whose key is in known is not read again, and
    // what is read is added to known, so a later read of overlapping items reuses it. known belongs to one page load (or one task), so a
    // later page load reads fresh data.
    public static class ReadOnce
    {
        // The answer for each item, in the order given. progress is told how many of the items that had to be read are done, and how many
        // there are to read in all. An answer that comes back null is not kept (it will be asked for again), and is null in the result.
        public static async Task<List<TResult?>> SelectAsync<T, TResult>(IReadOnlyList<T> items, Func<T, string> keyOf, IDictionary<string, TResult>? known,
            Func<T, Task<TResult?>> read, Action<int, int>? progress = null, int atOnce = ReadsAtOnce.Default) where TResult : class
        {
            var missing = known == null ? items.ToList() : items.Where(i => !known.ContainsKey(keyOf(i))).ToList();
            var done = 0;
            var answers = await ReadsAtOnce.SelectAsync(missing, async item =>
            {
                var answer = await read(item);
                progress?.Invoke(Interlocked.Increment(ref done), missing.Count);
                return answer;
            }, atOnce);

            var fresh = new Dictionary<string, TResult?>();
            for (var i = 0; i < missing.Count; i++)
            {
                fresh[keyOf(missing[i])] = answers[i];
                if (known != null && answers[i] != null)
                    known[keyOf(missing[i])] = answers[i]!;
            }
            return items.Select(item =>
            {
                var key = keyOf(item);
                return fresh.TryGetValue(key, out var answer) ? answer : known![key];
            }).ToList();
        }
    }
}
