using System.Globalization;

namespace Topo.Model.Approvals
{
    public enum ApprovalActionKind
    {
        Approve,
        RequestImprovement,
        Award
    }

    // One thing the leader can do to a pending submission, in the form Terrain's assessments call takes.
    // Terrain's own Approvals screen offers Approve and "Request improvement" (each with an optional comment of up to 500
    // characters) on review and approval submissions, and Award (with the date it was awarded, which cannot be in the
    // future) on award submissions.
    public class ApprovalAction
    {
        public const int MaxCommentLength = 500;

        public ApprovalActionKind Kind { get; }
        public string Comment { get; }
        public DateTime? DateAwarded { get; }

        private ApprovalAction(ApprovalActionKind kind, string comment, DateTime? dateAwarded)
        {
            Kind = kind;
            Comment = comment;
            DateAwarded = dateAwarded;
        }

        public static ApprovalAction Approve(string? comment) => new(ApprovalActionKind.Approve, (comment ?? "").Trim(), null);

        public static ApprovalAction RequestImprovement(string? comment) => new(ApprovalActionKind.RequestImprovement, (comment ?? "").Trim(), null);

        public static ApprovalAction Award(DateTime? dateAwarded) => new(ApprovalActionKind.Award, "", dateAwarded?.Date);

        // The value of "outcome" in the request body
        public string Outcome => Kind switch
        {
            ApprovalActionKind.Approve => "approved",
            ApprovalActionKind.RequestImprovement => "rejected",
            _ => "awarded"
        };

        // What the leader sees on the button and in the confirmation
        public string Label => Kind switch
        {
            ApprovalActionKind.Approve => "Approve",
            ApprovalActionKind.RequestImprovement => "Request improvement",
            _ => "Award"
        };

        // The same words Terrain shows after the action
        public string PastTense => Kind switch
        {
            ApprovalActionKind.Approve => "Approved",
            ApprovalActionKind.RequestImprovement => "Improvement requested",
            _ => "Awarded"
        };

        // null when the action can be sent, otherwise what is wrong with it. "today" is the leader's own day, used to
        // refuse an award date in the future.
        public string? Problem(string submissionType, DateTime? today = null)
        {
            var isAward = string.Equals(submissionType, "award", StringComparison.OrdinalIgnoreCase);
            if (Kind == ApprovalActionKind.Award)
            {
                if (!isAward)
                {
                    return "Only award requests can be awarded.";
                }
                if (DateAwarded == null)
                {
                    return "Choose the date the achievement was awarded.";
                }
                if (today != null && DateAwarded.Value.Date > today.Value.Date)
                {
                    return "The award date cannot be in the future.";
                }
                return null;
            }
            if (isAward)
            {
                return "Award requests cannot be approved or sent back for improvement.";
            }
            if (Comment.Length > MaxCommentLength)
            {
                return $"The comment can be at most {MaxCommentLength} characters.";
            }
            return null;
        }

        // The JSON body Terrain's own page sends: a comment for approve and improve, the date for an award
        public Dictionary<string, string> ToRequestBody() => Kind == ApprovalActionKind.Award
            ? new() { ["outcome"] = Outcome, ["date_awarded"] = DateAwarded!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) }
            : new() { ["outcome"] = Outcome, ["comment"] = Comment };
    }
}
