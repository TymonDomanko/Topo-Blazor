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
        public const string NewMemberGroup = "New member items";
        public const string PeakGroup = "Peak awards";

        private static readonly string[] GroupOrder = { YearsOfServiceGroup, MilestonesGroup, OasGroup, SiaGroup, AdditionalGroup, IntroGroup, NewMemberGroup, PeakGroup };

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

        // What a new member is given when they join, as the Section/Group Report names it. The ones the Scout Shop sells are listed here; the others (the group's
        // tape and scarf, the scarf badge and the region badge) are ordered by hand, so they show as lines that are not in the shop.
        private static readonly Dictionary<string, string> NewMemberProducts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Australian Scouting Badge"] = "australian-scout-flag-badge", ["World Scouting Badge"] = "world-membership-badge"
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
                    var stage = milestone.Name.Split(' ').LastOrDefault() ?? "";
                    Add(lines, MilestonesGroup, $"{sectionName} {milestone.Name}", milestone.Count, Shop(MilestoneProducts, key, $"{sectionName} Milestone {stage}"));
                }
                foreach (var oas in section.Badges.OasStages)
                {
                    Add(lines, OasGroup, $"OAS {oas.Stream}, Stage {oas.Stage}", oas.Count, ScoutShopCatalog.Find($"oas-{oas.Stream.ToLowerInvariant()}-badge", $"Stage {oas.Stage}"));
                }
                foreach (var sia in section.Badges.SiaCategories)
                {
                    var option = sia.Name == "Art & Literature" ? "Arts & Literature" : sia.Name;
                    Add(lines, SiaGroup, $"{sectionName} SIA, {sia.Name}", sia.Count, SiaProducts.TryGetValue(key, out var handle) ? ScoutShopCatalog.FindLoosely(handle, option) : null);
                }
                foreach (var award in section.Badges.AdditionalAwards)
                {
                    var (group, shop) = Additional(award.Id);
                    Add(lines, group, award.Name, award.Count, shop);
                }
                if (section.InvestedInPeriod > 0)
                {
                    var shop = IntroProducts.TryGetValue(key, out var handle) ? ScoutShopCatalog.Find(handle, "Default Title") : null;
                    Add(lines, IntroGroup, $"Introduction to {sectionName}", section.InvestedInPeriod, shop);
                }
                foreach (var item in section.Badges.InvestitureItems)
                {
                    var shop = NewMemberProducts.TryGetValue(item.Name, out var handle) ? ScoutShopCatalog.Find(handle, "Default Title") : null;
                    Add(lines, NewMemberGroup, item.Name, item.Count, shop);
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
