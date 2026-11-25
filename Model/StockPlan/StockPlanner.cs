using Topo.Model.Approvals;
using Topo.Model.BadgeOrder;
using Topo.Model.History;

namespace Topo.Model.StockPlan
{
    // One badge on the stock plan: how many were awarded over the last year, how many Topo can see are coming, and how many to keep
    public class StockLine
    {
        public string Group { get; set; } = "";
        public string Name { get; set; } = "";
        public ShopVariant? Shop { get; set; }
        // Awarded over the years looked back
        public int Awarded { get; set; }
        // How many months those years are
        public int HistoryMonths { get; set; } = StockPlanner.HistoryMonths;
        // Needed soon that Topo can see: requests waiting to be approved, and years of service anniversaries coming up
        public int Coming { get; set; }
        public int Suggested { get; set; }
        public double PerMonth => Awarded / (double)Math.Max(1, HistoryMonths);
        public decimal Value => Shop == null ? 0 : Shop.Price * Suggested;
    }

    // A year of service anniversary still to come, on its day
    public class DatedBadge
    {
        public DateTime Day { get; set; }
        public string AwardId { get; set; } = "";
    }

    // Works out how many of each badge to keep in stock. History is how many were awarded over the years looked back (a year, the default, smooths out
    // the busy and quiet terms; more years smooth out the odd big or small year); what is coming is what Topo can see will be needed: requests waiting for approval, and the years of service
    // anniversaries that fall inside the months to be covered. For each badge the stock is whichever is more, the history spread over the months to
    // cover or what is known to be coming, with a spare fifth on top, and at least one of anything awarded in the last year.
    public static class StockPlanner
    {
        // The default years looked back
        public const int HistoryMonths = 12;
        public const double Buffer = 0.2;

        public static int Suggested(int awarded, int coming, int coverMonths, int historyMonths = HistoryMonths)
        {
            if (awarded <= 0 && coming <= 0)
            {
                return 0;
            }
            var months = Math.Max(1, coverMonths);
            var need = Math.Max(awarded * months / (double)Math.Max(1, historyMonths), coming);
            return Math.Max(1, (int)Math.Ceiling(need * (1 + Buffer) - 1e-9));
        }

        // The stock plan: the history and what is coming, matched up badge by badge, and the stock to keep for the months to cover
        public static List<StockLine> Plan(IEnumerable<BadgeOrderLine> history, IEnumerable<BadgeOrderLine> pending, IEnumerable<DatedBadge> anniversaries, DateTime today, int coverMonths, int historyMonths = HistoryMonths)
        {
            var coming = new List<BadgeOrderLine>();
            foreach (var line in pending)
            {
                coming.Add(line);
            }
            var cutoff = today.Date.AddMonths(Math.Max(1, coverMonths));
            foreach (var group in anniversaries.Where(a => a.Day.Date > today.Date && a.Day.Date <= cutoff).GroupBy(a => a.AwardId))
            {
                if (YearsOfService.YearsOfServiceAwards.TryParse(group.Key, out var section, out var years))
                {
                    BadgeOrderBuilder.AddAward(coming, group.Key, YearsOfService.YearsOfServiceAwards.AwardName(section, years), group.Count());
                }
            }

            var plan = new List<StockLine>();
            StockLine Find(BadgeOrderLine line) =>
                plan.FirstOrDefault(p => p.Group == line.Group && (line.Shop != null ? p.Shop?.VariantId == line.Shop.VariantId : p.Shop == null && p.Name == line.Name))
                ?? AddLine(plan, line);
            foreach (var line in history)
            {
                Find(line).Awarded += line.Count;
            }
            foreach (var line in coming)
            {
                Find(line).Coming += line.Count;
            }
            foreach (var line in plan)
            {
                line.HistoryMonths = historyMonths;
                line.Suggested = Suggested(line.Awarded, line.Coming, coverMonths, historyMonths);
            }
            return plan
                .OrderBy(l => Array.IndexOf(OrderOfGroups, l.Group))
                .ThenBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static readonly string[] OrderOfGroups =
        {
            BadgeOrderBuilder.YearsOfServiceGroup, BadgeOrderBuilder.MilestonesGroup, BadgeOrderBuilder.OasGroup, BadgeOrderBuilder.SiaGroup,
            BadgeOrderBuilder.AdditionalGroup, BadgeOrderBuilder.IntroGroup, BadgeOrderBuilder.PeakGroup
        };

        private static StockLine AddLine(List<StockLine> plan, BadgeOrderLine line)
        {
            var added = new StockLine { Group = line.Group, Name = line.Name, Shop = line.Shop };
            plan.Add(added);
            return added;
        }

        // The badges Terrain's requests waiting for approval will need, for one unit's section. Milestones, Outdoor Adventure Skill stages, Special
        // Interest Areas and introductions to the section; the other kinds of request are not badges to stock.
        public static List<BadgeOrderLine> FromRequests(IEnumerable<Result> requests, string section)
        {
            var lines = new List<BadgeOrderLine>();
            foreach (var request in requests.Where(r => !string.Equals(r.submission.outcome, "rejected", StringComparison.OrdinalIgnoreCase)))
            {
                var meta = request.achievement.achievement_meta;
                switch (request.achievement.type)
                {
                    case "milestone":
                        BadgeOrderBuilder.AddMilestone(lines, section, meta.stage, 1);
                        break;
                    case "outdoor_adventure_skill":
                        BadgeOrderBuilder.AddOas(lines, ScoutingHistoryBuilder.Title(meta.stream), meta.stage, 1);
                        break;
                    case "special_interest_area":
                        var area = ScoutingHistoryBuilder.SiaName(meta.sia_area);
                        if (area != "")
                        {
                            BadgeOrderBuilder.AddSia(lines, section, area, 1);
                        }
                        break;
                    case "intro_section":
                        BadgeOrderBuilder.AddIntro(lines, section, 1);
                        break;
                }
            }
            return lines;
        }
    }
}
