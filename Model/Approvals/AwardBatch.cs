namespace Topo.Model.Approvals
{
    public class AwardOutcome
    {
        public AwardRequest Award { get; init; } = new();
        // null when Terrain accepted it, otherwise why not
        public string? Error { get; init; }
        public bool Worked => Error == null;
    }

    public class AwardBatchResult
    {
        // Why nothing was sent (no awards ticked, no date, a date in the future); null when the batch ran
        public string? Problem { get; init; }
        public List<AwardOutcome> Outcomes { get; init; } = new();
        public List<AwardOutcome> Done => Outcomes.Where(o => o.Worked).ToList();
        public List<AwardOutcome> Failed => Outcomes.Where(o => !o.Worked).ToList();
    }

    // Awards a ticked set of requests one after another. A request that fails is noted and the run carries on with the
    // rest, since Terrain's answer for one says nothing about the next. Each request is sent once.
    public static class AwardBatch
    {
        // "send" does the write for one submission and returns null when it worked, otherwise what went wrong.
        // "progress" is told how many have been tried so far and how many there are.
        public static async Task<AwardBatchResult> RunAsync(
            IEnumerable<AwardRequest> awards,
            DateTime? dateAwarded,
            DateTime today,
            Func<string, ApprovalAction, Task<string?>> send,
            Action<int, int>? progress = null)
        {
            var toAward = awards.Where(a => a.CanAward).ToList();
            if (toAward.Count == 0)
            {
                return new AwardBatchResult { Problem = "Tick at least one award first." };
            }

            var action = ApprovalAction.Award(dateAwarded);
            var problem = action.Problem("award", today);
            if (problem != null)
            {
                return new AwardBatchResult { Problem = problem };
            }

            var outcomes = new List<AwardOutcome>();
            foreach (var award in toAward)
            {
                progress?.Invoke(outcomes.Count, toAward.Count);
                string? error;
                try
                {
                    error = await send(award.Item.SubmissionId, action);
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }
                outcomes.Add(new AwardOutcome { Award = award, Error = error });
            }
            progress?.Invoke(outcomes.Count, toAward.Count);
            return new AwardBatchResult { Outcomes = outcomes };
        }
    }
}
