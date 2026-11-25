using System.Globalization;
using System.Text;
using Topo.Model.SectionReport;
using Topo.Model.YearsOfService;

namespace Topo.Model.BadgeOrder
{
    // One line of the badge order: a badge, how many were awarded, and the Scout Shop product that replaces it (none when the shop does not
    // list one that matches, so it is ordered by hand)
    public class BadgeOrderLine
    {
        public string Group { get; set; } = "";
        public string Name { get; set; } = "";
        public int Count { get; set; }
        public ShopVariant? Shop { get; set; }
        public bool InShop => Shop != null;
        public decimal Cost => Shop == null ? 0 : Shop.Price * Count;
    }

    // Turns what was awarded in a period (the Section/Group Report's badges, for every unit of the group) into an order for the Scout Shop. Each
    // section is worked out on its own, because milestone, Special Interest Area, service and introduction badges are different products for each.
    public static class BadgeOrderBuilder
    {
        public const string YearsOfServiceGroup = "Years of Service";
        public const string MilestonesGroup = "Milestones";
        public const string OasGroup = "Outdoor Adventure Skills";
        public const string SiaGroup = "Special Interest Areas";
        public const string AdditionalGroup = "Additional awards";
        public const string IntroGroup = "Introduction badges";
        public const string PeakGroup = "Peak awards";

        private static readonly string[] GroupOrder = { YearsOfServiceGroup, MilestonesGroup, OasGroup, SiaGroup, AdditionalGroup, IntroGroup, PeakGroup };

        private static readonly Dictionary<string, string> MilestoneProducts = new Dictionary<string, string>
        {
            ["joey"] = "milestone-badges-joey", ["cub"] = "milestone-badges-52015-p", ["scout"] = "milestone-badges-scout",
            ["venturer"] = "milestone-badges-venturer", ["rover"] = "milestone-badges-rover"
        };

        private static readonly Dictionary<string, string> SiaProducts = new Dictionary<string, string>
        {
            ["joey"] = "special-interest-badges-52055-p", ["cub"] = "special-interest-badges-cub", ["scout"] = "special-interest-badges-scout",
            ["venturer"] = "special-interest-badges-venturer", ["rover"] = "special-interest-badges-rover"
        };

        private static readonly Dictionary<string, string> ServiceProducts = new Dictionary<string, string>
        {
            ["joey"] = "joey-service", ["cub"] = "cub-service", ["scout"] = "scout-service-badge", ["venturer"] = "venturer-service", ["rover"] = "rover-service-badge"
        };

        private static readonly Dictionary<string, string> IntroProducts = new Dictionary<string, string>
        {
            ["joey"] = "introduction-to-joey-scouts-badge", ["cub"] = "introduction-to-cub-scouts-badge", ["scout"] = "introduction-to-scouts-badge",
            ["venturer"] = "introduction-to-venturer-scouts-badge", ["rover"] = "introduction-to-rover-scouts-badge"
        };

        // Additional awards that are a single badge in the shop, by Terrain's award id
        private static readonly Dictionary<string, string> SingleAwardProducts = new Dictionary<string, string>
        {
            ["first_aid"] = "first-aid-badge", ["landcare"] = "landcare-badge", ["mental_health"] = "mental-health-first-aid-badge",
            ["messengers_of_peace"] = "messengers-of-peace-badge", ["scouts_of_the_world"] = "scouts-of-the-world-achievement-badge"
        };

        // The lines for one kind of badge, added to a list (the same badge from two places is one line). Used by the order and by the stock planner.
        public static void AddMilestone(List<BadgeOrderLine> lines, string section, int stage, int count)
        {
            var key = (section ?? "").Trim().ToLowerInvariant();
            var sectionName = YearsOfServiceAwards.SectionName(key);
            Add(lines, MilestonesGroup, $"{sectionName} Milestone {stage}", count, Shop(MilestoneProducts, key, $"{sectionName} Milestone {stage}"));
        }

        public static void AddOas(List<BadgeOrderLine> lines, string stream, int stage, int count) =>
            Add(lines, OasGroup, $"OAS {stream}, Stage {stage}", count, ScoutShopCatalog.Find($"oas-{stream.ToLowerInvariant()}-badge", $"Stage {stage}"));

        public static void AddSia(List<BadgeOrderLine> lines, string section, string category, int count)
        {
            var key = (section ?? "").Trim().ToLowerInvariant();
            var option = category == "Art & Literature" ? "Arts & Literature" : category;
            Add(lines, SiaGroup, $"{YearsOfServiceAwards.SectionName(key)} SIA, {category}", count, SiaProducts.TryGetValue(key, out var handle) ? ScoutShopCatalog.FindLoosely(handle, option) : null);
        }

        // An additional award, a Years of Service badge included, by Terrain's id (or Topo's, yos_scout_3)
        public static void AddAward(List<BadgeOrderLine> lines, string awardId, string name, int count)
        {
            var (group, shop) = Additional(awardId);
            Add(lines, group, name, count, shop);
        }

        public static void AddIntro(List<BadgeOrderLine> lines, string section, int count)
        {
            var key = (section ?? "").Trim().ToLowerInvariant();
            Add(lines, IntroGroup, $"Introduction to {YearsOfServiceAwards.SectionName(key)}", count, IntroProducts.TryGetValue(key, out var handle) ? ScoutShopCatalog.Find(handle, "Default Title") : null);
        }

        // What was awarded in the report's period, as an order. Lines with the same badge are added together across the sections.
        public static List<BadgeOrderLine> Build(SectionReportSet report)
        {
            var lines = new List<BadgeOrderLine>();
            foreach (var section in report.Sections)
            {
                var key = (section.Section ?? "").Trim().ToLowerInvariant();
                var sectionName = YearsOfServiceAwards.SectionName(key);

                foreach (var milestone in section.Badges.Milestones)
                {
                    AddMilestone(lines, key, int.TryParse(milestone.Name.Split(' ').LastOrDefault(), out var stage) ? stage : 0, milestone.Count);
                }
                foreach (var oas in section.Badges.OasStages)
                {
                    AddOas(lines, oas.Stream, oas.Stage, oas.Count);
                }
                foreach (var sia in section.Badges.SiaCategories)
                {
                    AddSia(lines, key, sia.Name, sia.Count);
                }
                foreach (var award in section.Badges.AdditionalAwards)
                {
                    AddAward(lines, award.Id, award.Name, award.Count);
                }
                if (section.InvestedInPeriod > 0)
                {
                    AddIntro(lines, key, section.InvestedInPeriod);
                }
                if (section.Badges.PeakAwards > 0)
                {
                    // The report counts Peak Awards without saying which part (or which badge), so they cannot be matched to the shop
                    Add(lines, PeakGroup, $"{sectionName} Peak Award", section.Badges.PeakAwards, null);
                }
            }
            return lines
                .OrderBy(l => Array.IndexOf(GroupOrder, l.Group))
                .ThenBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static ShopVariant? Shop(Dictionary<string, string> products, string section, string option) =>
            products.TryGetValue(section, out var handle) ? ScoutShopCatalog.Find(handle, option) : null;

        // The group and the shop product for an additional award, from the id Terrain (or Topo, for years of service) gives it
        private static (string group, ShopVariant? shop) Additional(string id)
        {
            if (YearsOfServiceAwards.TryParse(id, out var section, out var years))
            {
                var option = section == "joey" || section == "cub" ? $"Year {years}" : $"{years} {(years == 1 ? "Year" : "Years")}";
                return (YearsOfServiceGroup, Shop(ServiceProducts, section, option));
            }
            var parts = id.Split('_');
            if (parts.Length == 3 && parts[0] == "camper" && parts[1] == "award")
            {
                return (AdditionalGroup, ScoutShopCatalog.Find("camper-award", $"{parts[2]} Nights"));
            }
            if (parts.Length == 2 && parts[0] == "walkabout")
            {
                return (AdditionalGroup, ScoutShopCatalog.Find("walkabout-award", $"{parts[1]} kms"));
            }
            if (SingleAwardProducts.TryGetValue(id, out var handle))
            {
                return (AdditionalGroup, ScoutShopCatalog.Find(handle, "Default Title"));
            }
            if (id.StartsWith("duke_of_edinburgh_", StringComparison.Ordinal))
            {
                var level = id.Substring("duke_of_edinburgh_".Length);
                return (AdditionalGroup, ScoutShopCatalog.Find("duke-of-edinburgh-award-badges", level.Length == 0 ? "" : char.ToUpperInvariant(level[0]) + level.Substring(1)));
            }
            return (AdditionalGroup, null);
        }

        // A line added to a list, summed with the same badge already there
        public static void MergeInto(List<BadgeOrderLine> lines, BadgeOrderLine line) => Add(lines, line.Group, line.Name, line.Count, line.Shop);

        private static void Add(List<BadgeOrderLine> lines, string group, string name, int count, ShopVariant? shop)
        {
            if (count <= 0)
            {
                return;
            }
            // The same shop badge from two sections is one line (the Outdoor Adventure Skills are the same badge in every section); a badge
            // the shop does not list is one line for each name
            var existing = lines.FirstOrDefault(l => l.Group == group && (shop != null ? l.Shop?.VariantId == shop.VariantId : l.Shop == null && l.Name == name));
            if (existing != null)
            {
                existing.Count += count;
                return;
            }
            lines.Add(new BadgeOrderLine { Group = group, Name = name, Count = count, Shop = shop });
        }

        // The Scout Shop's cart links for the lines in the shop: a link adds each badge, in the quantity asked for, to a cart and goes to checkout.
        // The whole order is one link unless it would be longer than a browser or server can be trusted to take (about 2,000 characters, which is
        // some 100 lines), when it is split so that each link stays under that.
        public const int MostLinkLength = 1900;

        public static List<string> CartLinks(IEnumerable<BadgeOrderLine> lines, int maxLength = MostLinkLength)
        {
            var prefix = ScoutShopCatalog.ShopUrl + "cart/";
            var links = new List<string>();
            var current = new List<string>();
            var length = prefix.Length;
            foreach (var entry in lines.Where(l => l.Shop != null && l.Count > 0).Select(l => $"{l.Shop!.VariantId}:{l.Count.ToString(CultureInfo.InvariantCulture)}"))
            {
                // the comma that separates it from the one before
                var added = entry.Length + (current.Count > 0 ? 1 : 0);
                if (current.Count > 0 && length + added > maxLength)
                {
                    links.Add(prefix + string.Join(",", current));
                    current.Clear();
                    length = prefix.Length;
                    added = entry.Length;
                }
                current.Add(entry);
                length += added;
            }
            if (current.Count > 0)
            {
                links.Add(prefix + string.Join(",", current));
            }
            return links;
        }

        // The order as plain text, one badge a line, for copying: the shop's own name for each badge, or Topo's name when the shop has none
        public static string AsText(IEnumerable<BadgeOrderLine> lines)
        {
            var text = new StringBuilder();
            foreach (var line in lines.Where(l => l.Count > 0))
            {
                text.Append(line.Count.ToString(CultureInfo.InvariantCulture)).Append(" x ");
                text.Append(line.Shop != null ? $"{line.Shop.Product}: {line.Shop.Option}" : line.Name + " (not in the shop)");
                text.Append('\n');
            }
            return text.ToString();
        }
    }
}
