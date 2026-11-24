using Topo.Model.Members;

namespace Topo.Services
{
    // Who may write the group's store records. The store event can be opened by everyone in the group, so only a record written by an adult
    // leader of one of the signed-in leader's own units counts.
    public static class TrustedLeaders
    {
        public static async Task<HashSet<string>> AdultLeaderIdsAsync(StorageService storageService, IMembersService membersService)
        {
            var ids = new HashSet<string>();
            foreach (var unitId in storageService.Units.Keys)
            {
                foreach (var member in (await membersService.GetMembersAsync(unitId)).Where(m => m.isAdultLeader == 1))
                {
                    ids.Add(member.id);
                }
            }
            return ids;
        }
    }
}
