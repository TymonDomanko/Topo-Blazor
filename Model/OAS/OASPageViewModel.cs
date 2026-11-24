using System.ComponentModel.DataAnnotations;

namespace Topo.Model.OAS
{
    public class OASPageViewModel
    {
        public Dictionary<string, string> Units { get; set; } = new Dictionary<string, string>();
        [Display(Name = "Unit")]
        [Required(ErrorMessage = "Select unit")]
        public string UnitId { get; set; } = string.Empty;
        public string UnitName { get; set; } = string.Empty;
        // Every stage of every skill, as Terrain lists them
        public List<OASStageListModel> Stages { get; set; } = new List<OASStageListModel>();

        // A skill's name as the stage list has it, OasWorksheetSelection.Core for the three core skills, or blank for none yet
        public string Skill { get; set; } = string.Empty;
        // 1 to 9, or OasWorksheetSelection.OneAboveHighest
        public int Level { get; set; } = 1;
        public bool IncludeBelow { get; set; } = true;
        // The branches ticked at the level, by OasBranchChoice.Key
        public HashSet<string> ChosenBranches { get; set; } = new HashSet<string>();

        [Display(Name = "Hide Completed Members")]
        public bool HideCompletedMembers { get; set; } = true;

        [Display(Name = "Break by Patrol")]
        public bool BreakByPatrol { get; set; } = false;
        public bool FormatLikeTerrain { get; set; } = true;
        public string GroupName { get; set; } = string.Empty;
        public bool UseFullNames { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
    }

}
