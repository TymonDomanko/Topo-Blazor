namespace Topo.Model.ProgramBuilder
{
    // What kind of entry a row of the program is. Only a section night is given a duty patrol.
    public enum ProgramRowKind
    {
        SectionNight,
        Camp,
        Hike,
        Weekend,
        // An activity away from the section night that is none of the above
        Other
    }

    public class ProgramRow
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public ProgramRowKind Kind { get; set; } = ProgramRowKind.SectionNight;
        public DateTime Date { get; set; }
        // The last day of a camp or hike that runs over several days
        public DateTime? EndDate { get; set; }
        public string Activity { get; set; } = string.Empty;
        public string Game { get; set; } = string.Empty;
        // Blank to use the program's usual location
        public string Location { get; set; } = string.Empty;
        // Blank to use the program's usual leader in charge
        public string Leader { get; set; } = string.Empty;
        // For a section night: a patrol to put on duty in place of the next in the cycle
        public string DutyPatrol { get; set; } = string.Empty;
        // For a section night: no patrol on duty (a leaders' meeting, say), so the cycle carries on without it
        public bool NoDuty { get; set; }
        // For a section night: not running (a public holiday, say), so it is left out
        public bool Skipped { get; set; }
        // The event in Terrain this row was filled in from, so it is not put in Terrain again; blank for one that is only in the program
        public string TerrainEventId { get; set; } = string.Empty;
        // community, outdoors, creative or personal_growth; needed to put the activity in Terrain
        public string ChallengeArea { get; set; } = string.Empty;
        // "HH:mm"; blank to use the program's usual times for this kind of row
        public string StartTime { get; set; } = string.Empty;
        public string EndTime { get; set; } = string.Empty;
        // The parts of the Scout Method this activity uses, by Terrain's names: one, several or none (Terrain is told it only when there is at least one)
        public List<string> ScoutMethod { get; set; } = new List<string>();
        // For a section night: the patrol that ends up on duty, worked out from the cycle and the choices above; never entered
        public string Duty { get; set; } = string.Empty;
    }

    // Something the section might do, kept until it is dragged onto a night
    public class ProgramIdea
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Title { get; set; } = string.Empty;
        public string Game { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string ChallengeArea { get; set; } = string.Empty;
        // For one a youth member proposed in Terrain: who, and which proposal, so it is not offered again once it has been used or taken off
        public string ProposedBy { get; set; } = string.Empty;
        public string ProposalId { get; set; } = string.Empty;
    }

    // A stretch with no section nights, such as the school holidays
    public class ProgramBreak
    {
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public string Label { get; set; } = "Term Break";
    }

    public class ProgramLocation
    {
        public string Name { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
    }

    public class ProgramContact
    {
        public string Name { get; set; } = string.Empty;
        public string Position { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        // For one filled in from the unit's membership: which member, so it is not filled in again once it has been taken off
        public string MemberId { get; set; } = string.Empty;
    }

    // Everything entered for one program, as it is saved on the device
    public class ProgramDraft
    {
        public string Title { get; set; } = string.Empty;
        public string UnitName { get; set; } = string.Empty;
        public string Section { get; set; } = string.Empty;
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        public DayOfWeek MeetingDay { get; set; } = DayOfWeek.Wednesday;
        // Whether the section night and usual times have been found from the unit's calendar, so they are looked for on opening the unit until they are
        public bool UsualFound { get; set; }
        public List<ProgramBreak> Breaks { get; set; } = new List<ProgramBreak>();
        // The patrols that take turns on duty, in order
        public List<string> DutyPatrols { get; set; } = new List<string>();
        // Which of them is on duty the first night
        public string FirstDutyPatrol { get; set; } = string.Empty;
        public string DefaultLocation { get; set; } = string.Empty;
        public string DefaultLeader { get; set; } = string.Empty;
        // The times a row has when it has none of its own: the section night, a camp (it starts on the first day and ends on the last), and
        // anything else
        public string NightStart { get; set; } = "19:00";
        public string NightEnd { get; set; } = "21:00";
        public string CampStart { get; set; } = "18:00";
        public string CampEnd { get; set; } = "10:00";
        public string OtherStart { get; set; } = "09:00";
        public string OtherEnd { get; set; } = "17:00";
        public string DefaultChallengeArea { get; set; } = string.Empty;
        // What has been filled in for a section night, by its date (yyyy-MM-dd), kept when the dates are changed
        public Dictionary<string, ProgramRow> NightEdits { get; set; } = new Dictionary<string, ProgramRow>();
        // The ideas not yet on a night
        public List<ProgramIdea> Ideas { get; set; } = new List<ProgramIdea>();
        // The youth's proposals already offered as ideas, whether still among them, on a night or taken off, so each is offered once
        public List<string> TakenProposals { get; set; } = new List<string>();
        // The leaders already made contacts from the unit's membership, whether or not they are still in the list, so each is added once
        public List<string> TakenLeaders { get; set; } = new List<string>();
        // Everything that is not a section night
        public List<ProgramRow> Extras { get; set; } = new List<ProgramRow>();
        public List<ProgramLocation> Locations { get; set; } = new List<ProgramLocation>();
        public List<ProgramContact> Contacts { get; set; } = new List<ProgramContact>();
        // What the printed program shows: each of these columns and tables can be switched off. The month, date and activity are always shown.
        // off to begin with: the game is not planned for every night, so it is neither asked for nor printed unless it is switched on
        public bool ShowGame { get; set; }
        public bool ShowLocation { get; set; } = true;
        public bool ShowChallengeArea { get; set; }
        public bool ShowDutyPatrol { get; set; } = true;
        public bool ShowLeader { get; set; } = true;
        public bool ShowLocations { get; set; } = true;
        public bool ShowContacts { get; set; } = true;
    }
}
