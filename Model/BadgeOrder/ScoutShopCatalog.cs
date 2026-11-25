using System.Globalization;

namespace Topo.Model.BadgeOrder
{
    // One thing the national Scout Shop (scoutshop.com.au) sells: a product and the option that picks the badge, with the number the shop's cart
    // link needs for it
    public class ShopVariant
    {
        public string Handle { get; set; } = "";
        public string Product { get; set; } = "";
        // What picks the badge in the product ("Stage 3", "Scout Milestone 2", "Environment", "Default Title" when there is no choice)
        public string Option { get; set; } = "";
        public long VariantId { get; set; }
        public decimal Price { get; set; }
    }

    // The badges the Scout Shop sells that Topo can order for, as the shop listed them on 25 November 2026. The numbers are the shop's own
    // product variant ids and its RRP prices. If the shop re-creates a product its numbers change: refresh the list below (docs/badge-order.md
    // says how) and anything that no longer matches is shown as "not in the shop" to order by hand, never ordered wrongly.
    public static class ScoutShopCatalog
    {
        public const string ShopUrl = "https://scoutshop.com.au/";

        // handle | product | option name | option=variant id@price; ...
        private const string Data = """
oas-alpine-badge | OAS Alpine Badge | Badge Type | Stage 1=43723150786653@1.2; Stage 2=43723150819421@1.2; Stage 3=43723150852189@1.2; Stage 4=43723150884957@1.2; Stage 5=43723150917725@1.2; Stage 6=43723150950493@1.2; Stage 7=43723150983261@1.2; Stage 8=43723151016029@1.2; Stage 9=43723151048797@1.2
oas-aquatics-badge | OAS Aquatics Badge | Badge Type | Stage 1=43723151081565@1.2; Stage 2=43723151114333@1.2; Stage 3=43723151147101@1.2; Stage 4=43723151179869@1.2; Stage 5=43723151212637@1.2; Stage 6=43723151245405@1.2; Stage 7=43723151278173@1.2; Stage 8=43723151310941@1.2; Stage 9=43723151343709@1.2
oas-boating-badge | OAS Boating Badge | Badge Type | Stage 1=43723151376477@1.2; Stage 2=43723151409245@1.2; Stage 3=43723151442013@1.2; Stage 4=43723151474781@1.2; Stage 5=43723151507549@1.2; Stage 6=43723151540317@1.2; Stage 7=43723151573085@1.2; Stage 8=43723151605853@1.2; Stage 9=43723151638621@1.2
oas-bushcraft-badge | OAS Bushcraft Badge | Badge Type | Stage 1=43723151671389@1.2; Stage 2=43723151704157@1.2; Stage 3=43723151736925@1.2; Stage 4=43723151769693@1.2; Stage 5=43723151802461@1.2; Stage 6=43723151835229@1.2; Stage 7=43723151867997@1.2; Stage 8=43723151900765@1.2; Stage 9=43723151933533@1.2
oas-bushwalking-badge | OAS Bushwalking Badge | Badge Type | Stage 1=43723151966301@1.2; Stage 2=43723151999069@1.2; Stage 3=43723152031837@1.2; Stage 4=43723152064605@1.2; Stage 5=43723152097373@1.2; Stage 6=43723152130141@1.2; Stage 7=43723152162909@1.2; Stage 8=43723152195677@1.2; Stage 9=43723152228445@1.2
oas-camping-badge | OAS Camping Badge | Badge Type | Stage 1=43723152261213@1.2; Stage 2=43723152293981@1.2; Stage 3=43723152326749@1.2; Stage 4=43723152359517@1.2; Stage 5=43723152392285@1.2; Stage 6=43723152425053@1.2; Stage 7=43723152457821@1.2; Stage 8=43723152490589@1.2; Stage 9=43723152523357@1.2
oas-cycling-badge | OAS Cycling Badge | Badge Type | Stage 1=43723152556125@1.2; Stage 2=43723152588893@1.2; Stage 3=43723152621661@1.2; Stage 4=43723152654429@1.2; Stage 5=43723152687197@1.2; Stage 6=43723152719965@1.2; Stage 7=43723152752733@1.2; Stage 8=43723152785501@1.2; Stage 9=43723152818269@1.2
oas-paddling-badge | OAS Paddling Badge | Badge Type | Stage 1=43723152851037@1.2; Stage 2=43723152883805@1.2; Stage 3=43723152916573@1.2; Stage 4=43723152949341@1.2; Stage 5=43723152982109@1.2; Stage 6=43723153014877@1.2; Stage 7=43723153047645@1.2; Stage 8=43723153080413@1.2; Stage 9=43723153113181@1.2
oas-vertical-badge | OAS Vertical Badge | Badge Type | Stage 1=43723153178717@1.2; Stage 2=43723153211485@1.2; Stage 3=43723153244253@1.2; Stage 4=43723153277021@1.2; Stage 5=43723153309789@1.2; Stage 6=43723153342557@1.2; Stage 7=43723153375325@1.2; Stage 8=43723153408093@1.2; Stage 9=43723153440861@1.2
milestone-badges-joey | Milestone Badges - JOEY SCOUT | Badge Type | Joey Milestone 1=43723144822877@1.4; Joey Milestone 2=43723144855645@1.4; Joey Milestone 3=43723144888413@1.4
milestone-badges-52015-p | Milestone Badges - CUB SCOUT | Badge Type | Cub Milestone 1=43723144724573@1.4; Cub Milestone 2=43723144757341@1.4; Cub Milestone 3=43723144790109@1.4
milestone-badges-scout | Milestone Badges - SCOUT | Badge Type | Scout Milestone 1=43723145019485@1.4; Scout Milestone 2=43723145052253@1.4; Scout Milestone 3=43723145085021@1.4
milestone-badges-venturer | Milestone Badges - VENTURER SCOUT | Badge Type | Venturer Milestone 1=43723145117789@1.4; Venturer Milestone 2=43723145150557@1.4; Venturer Milestone 3=43723145183325@1.4
milestone-badges-rover | Milestone Badges - ROVER SCOUT | Badge Type | Rover Milestone 1=43723144921181@1.4; Rover Milestone 2=43723144953949@1.4; Rover Milestone 3=43723144986717@1.4
special-interest-badges-52055-p | Special Interest Area Badges - JOEY SCOUT | Badge Type | Creating A Better World=43723178246237@1.2; Arts & Literature=43723178279005@1.2; Environment=43723178311773@1.2; Growth & Development=43723178344541@1.2; STEM & Innovation=43723178377309@1.2; Adventure & Sport=43723178410077@1.2
special-interest-badges-cub | Special Interest Area Badges - CUB SCOUT | Badge Type | Creating A Better World=43723178049629@1.2; Arts & Literature=43723178082397@1.2; Environment=43723178115165@1.2; Growth & Development=43723178147933@1.2; STEM & Innovation=43723178180701@1.2; Adventure & Sport=43723178213469@1.2
special-interest-badges-scout | Special Interest Area Badges - SCOUT | Badge Type | Creating A Better World=43723178639453@1.2; Arts & Literature=43723178672221@1.2; Environment=43723178704989@1.2; Growth & Development=43723178737757@1.2; STEM & Innovation=43723178770525@1.2; Adventure & Sport=43723178803293@1.2
special-interest-badges-venturer | Special Interest Area Badges - VENTURER SCOUT | Badge Type | Creating A Better World=43723178836061@1.2; Arts & Literature=43723178868829@1.2; Environment=43723178901597@1.2; Growth & Development=43723178934365@1.2; STEM & Innovation=43723178967133@1.2; Adventure & Sport=43723178999901@1.2
special-interest-badges-rover | Special Interest Area Badges - ROVER SCOUT | Badge Type | Creating A Better World=43723178442845@1.2; Arts & Literature=43723178475613@1.2; Environment=43723178508381@1.2; Growth & Development=43723178541149@1.2; STEM & Innovation=43723178573917@1.2; Adventure & Sport=43723178606685@1.2
joey-service | Joey Service Badge | Badge Type | Year 1=43723110613085@1.0; Year 2=43723110645853@1.0; Year 3=43723110678621@1.0
cub-service | Cub Service Badge | Badge Type | Year 1=43723093377117@1.0; Year 2=43723093409885@1.0; Year 3=43723093442653@1.0
scout-service-badge | Scout Service Badge | Badge Type | 1 Year=43723173822557@1.0; 2 Years=43723173855325@1.0; 3 Years=43723173888093@1.0; 4 Years=43723173920861@1.0
venturer-service | Venturer Service | Badge Type | 1 Year=43723200725085@1.0; 2 Years=43723200757853@1.0; 3 Years=43723200790621@1.0; 4 Years=43723200823389@1.0
rover-service-badge | Rover Service Badge | Badge Type | 1 Year=43723166875741@1.0; 2 Years=43723166908509@1.0; 3 Years=43723166941277@1.0; 4 Years=43723166974045@1.0; 5 Years=43723167006813@1.0; 6 Years=43723167039581@1.0; 7 Years=43723167072349@1.0; 8 Years=43723167105117@1.0
camper-award | Camper Award | Badge Type | 10 Nights=43723086004317@1.5; 25 Nights=43723086037085@1.5; 50 Nights=43723086069853@1.5; 75 Nights=43723086102621@1.5; 100 Nights=43723086135389@1.5; 150 Nights=44362034774109@1.5; 200 Nights=44362034806877@1.5; 250 Nights=44362034839645@1.5; 300 Nights=44362034872413@1.5
walkabout-award | Walkabout Award | Badge Type | 10 kms=43723202429021@1.5; 50 kms=43723202461789@1.5; 100 kms=43723202494557@1.5; 150 kms=43723202527325@1.5; 200 kms=43723202560093@1.5; 300 kms=43723202592861@1.5; 400 kms=43723202625629@1.5; 500 kms=43723202658397@1.5; 750 kms=43723202691165@3.0; 1000 kms=43835446788189@3.0
introduction-to-joey-scouts-badge | Introduction to Joey Scouts Badge | Title | Default Title=43723109498973@1.0
introduction-to-cub-scouts-badge | Introduction to Cub Scouts Badge | Title | Default Title=43723109466205@1.0
introduction-to-scouts-badge | Introduction to Scouts Badge | Title | Default Title=43723109564509@1.0
introduction-to-venturer-scouts-badge | Introduction to Venturer Scouts Badge | Title | Default Title=43723109597277@1.0
introduction-to-rover-scouts-badge | Introduction to Rover Scouts Badge | Title | Default Title=43723109531741@1.0
first-aid-badge | First Aid Badge | Title | Default Title=43723099144285@1.4
landcare-badge | Landcare Badge | Title | Default Title=43723115593821@1.4
mental-health-first-aid-badge | Mental Health First Aid Badge | Title | Default Title=43723124506717@1.5
messengers-of-peace-badge | Messengers of Peace | Title | Default Title=43723144265821@2.5
scouts-of-the-world-achievement-badge | Scouts of the World - Achievement Badge | Title | Default Title=43723174019165@4.0
duke-of-edinburgh-award-badges | Duke of Edinburgh Award Badges | Badge Type | Gold=43723096653917@2.0; Silver=43723096686685@2.0; Bronze=43723096719453@2.0
australian-scout-flag-badge | Australian Scout Flag Badge | Title | Default Title=43723074732125@1.2
world-membership-badge | World Membership Badge | Title | Default Title=43723205148765@1.6
""";

        private static readonly Lazy<List<ShopVariant>> Variants = new Lazy<List<ShopVariant>>(Parse);

        public static IReadOnlyList<ShopVariant> All => Variants.Value;

        // The variant for a product and option, or null when the shop has no such badge in the list
        public static ShopVariant? Find(string handle, string option) =>
            All.FirstOrDefault(v => v.Handle == handle && string.Equals(v.Option, option, StringComparison.OrdinalIgnoreCase));

        // The variant for a product and an option written some other way: ignoring case, spacing and "&" against "and"
        public static ShopVariant? FindLoosely(string handle, string option)
        {
            var wanted = Normalise(option);
            return All.FirstOrDefault(v => v.Handle == handle && Normalise(v.Option) == wanted);
        }

        private static string Normalise(string text) =>
            new string(text.ToLowerInvariant().Replace("&", "and").Where(char.IsLetterOrDigit).ToArray());

        private static List<ShopVariant> Parse()
        {
            var list = new List<ShopVariant>();
            foreach (var line in Data.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var parts = line.Split(" | ");
                if (parts.Length != 4)
                {
                    continue;
                }
                foreach (var entry in parts[3].Split("; "))
                {
                    var equals = entry.LastIndexOf('=');
                    var at = entry.LastIndexOf('@');
                    if (equals < 0 || at < equals || !long.TryParse(entry.AsSpan(equals + 1, at - equals - 1), NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                        || !decimal.TryParse(entry.AsSpan(at + 1), NumberStyles.Number, CultureInfo.InvariantCulture, out var price))
                    {
                        continue;
                    }
                    list.Add(new ShopVariant { Handle = parts[0], Product = parts[1], Option = entry.Substring(0, equals), VariantId = id, Price = price });
                }
            }
            return list;
        }
    }
}
