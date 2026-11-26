namespace Topo.Model.AdditionalAwards
{
    public class AdditionalAwardSpecificationListModel
    {
        public string id { get; set; } = string.Empty;
        public string name { get; set; } = string.Empty;
        public int additionalAwardSortIndex { get; set; } = 0;
        public int additionalAwardValue { get; set; } = 0;
        // The sections (joey, cub, scout, venturer, rover) the award is open to, as Terrain lists them
        public string[] sections { get; set; } = new string[0];
    }
}
