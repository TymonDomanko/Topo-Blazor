using Topo.Model.Program;

namespace Topo.Model.Defaults
{
    // What the pages start with, chosen in Settings and kept in this browser. The pages still let each report be changed as it is made.
    public class UserDefaults
    {
        // First name plus initial (off) or the full name (on) on reports and lists
        public bool UseFullNames { get; set; }
        // How a new sign in sheet starts (full names, the Registered and Paid columns, Participate / Lead, and Time in/out or a tick box)
        public SignInSheetOptions SignIn { get; set; } = new SignInSheetOptions();
    }
}
