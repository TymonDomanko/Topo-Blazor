namespace Topo.Model.OAS
{
    public class OASStageListModel
    {
        public string Stream { get; set; } = string.Empty;
        public string StageTitle { get; set; } = string.Empty;
        public string Branch { get; set; } = string.Empty;
        // The branch of the stage this one follows on from. For the first stage of a branch that is the branch it forked from (Life Saving
        // for Swift Water Rescue); for the stage after one of its own branch it is that same branch, and the first stage of a skill has none.
        public string ParentBranch { get; set; } = string.Empty;
        public int Stage { get; set; }
        public string TemplateLink { get; set; } = string.Empty;
        public string SelectListItemText { get; set; } = string.Empty;
    }
}
