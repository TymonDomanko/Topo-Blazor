namespace Topo.Model.SIA
{
    public class SIAProjectListModel
    {
        public string memberId { get; set; } = string.Empty;
        public string memberName { get; set; } = string.Empty;
        public string memberPatrol { get; set; } = string.Empty;
        public string area { get; set; } = string.Empty;
        public string projectName { get; set; } = string.Empty;
        public string status { get; set; } = string.Empty;
        public DateTime statusUpdated { get; set; }
        public string achievement_id { get; set; } = string.Empty;

        // The same project under another name, for when the names are shown in full or shortened
        public SIAProjectListModel WithName(string name)
        {
            var copy = (SIAProjectListModel)MemberwiseClone();
            copy.memberName = name;
            return copy;
        }
    }
}
