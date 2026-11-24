using Topo.Model.AdditionalAwards;

namespace Topo.Model.Approvals
{
    // One pending award request, and whether the signed in leader can act on it. Terrain disables the Award button on a
    // request that is the leader's own, so that is shown but cannot be selected. A request the leader has already
    // actioned is not listed at all.
    public class AwardRequest
    {
        public PendingApprovalItem Item { get; init; } = new();
        public bool CanAward { get; init; }
        public string Reason { get; init; } = string.Empty;
        // A Camper or Walkabout Award Topo worked out from the logbook, which has no award request in Terrain
        public bool IsTopoAward => Item.SubmissionId.StartsWith(PendingApprovalItem.TopoIdPrefix, StringComparison.Ordinal);
    }

    // All of one person's pending awards
    public class AwardPerson
    {
        public string MemberId { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        // "Last First", which the list is in order of
        public string SortName { get; init; } = string.Empty;
        public List<AwardRequest> Awards { get; init; } = new();
    }

    // Every pending award request in a unit, grouped by the person they are for
    public class AwardRequestList
    {
        public List<AwardPerson> People { get; init; } = new();

        public int Total => People.Sum(p => p.Awards.Count);

        // The ids of pending award requests that are not listed because someone has already awarded them (the leader or
        // anyone else) or the leader has already actioned them. After awarding, one that is here is still pending in
        // Terrain with an awarded response recorded.
        public HashSet<string> Hidden { get; init; } = new();

        // Only the people whose member id is in the set (for example those who attended an event), in the same order
        public AwardRequestList OnlyMembers(ISet<string> memberIds) =>
            new() { People = People.Where(p => memberIds.Contains(p.MemberId)).ToList() };

        // The Camper and Walkabout awards to hand over, listed with the pending awards as one more kind of badge.
        // They have no award request in Terrain, so each gets an id that starts with PendingApprovalItem.TopoIdPrefix.
        public AwardRequestList WithAdditionalAwards(IEnumerable<AwardRow> toBePresented)
        {
            var rows = toBePresented.Where(r => r.Status == AwardRowStatus.ToBePresented).ToList();
            if (rows.Count == 0)
            {
                return this;
            }

            var people = People.ToDictionary(p => p.MemberId, p => new AwardPerson { MemberId = p.MemberId, Name = p.Name, SortName = p.SortName, Awards = p.Awards.ToList() });
            foreach (var row in rows)
            {
                if (!people.TryGetValue(row.MemberId, out var person))
                {
                    person = new AwardPerson { MemberId = row.MemberId, Name = row.MemberName, SortName = row.SortName.Length > 0 ? row.SortName : row.MemberName };
                    people[row.MemberId] = person;
                }
                person.Awards.Add(new AwardRequest
                {
                    CanAward = true,
                    Item = new PendingApprovalItem
                    {
                        SubmissionId = PendingApprovalItem.TopoIdPrefix + row.Key,
                        SubmissionType = "award",
                        SubmissionDate = row.Earned ?? DateTime.Today,
                        MemberId = row.MemberId,
                        MemberName = row.MemberName,
                        AchievementId = row.AwardId,
                        AchievementType = "additional_award",
                        AchievementName = row.AwardName
                    }
                });
            }
            foreach (var person in people.Values)
                person.Awards.Sort((a, b) =>
                {
                    var byName = string.Compare(a.Item.AchievementName, b.Item.AchievementName, StringComparison.CurrentCultureIgnoreCase);
                    if (byName != 0)
                    {
                        return byName;
                    }
                    var byDate = a.Item.SubmissionDate.CompareTo(b.Item.SubmissionDate);
                    return byDate != 0 ? byDate : string.CompareOrdinal(a.Item.SubmissionId, b.Item.SubmissionId);
                });
            return new AwardRequestList
            {
                People = people.Values.OrderBy(p => p.SortName, StringComparer.CurrentCultureIgnoreCase).ThenBy(p => p.MemberId, StringComparer.Ordinal).ToList(),
                Hidden = Hidden
            };
        }

        // People are in alphabetical order by last name then first name, and a person's awards by achievement then by
        // when they were submitted. A request that already has an "awarded" response recorded, or that the leader has
        // already actioned, is left out: Terrain can keep one pending after it has been awarded, and the leader has
        // nothing more to do with it.
        public static AwardRequestList FromPending(IEnumerable<Result> pending, string? currentMemberId)
        {
            var awards = pending
                .Where(r => string.Equals(r.submission.type, "award", StringComparison.OrdinalIgnoreCase))
                .ToList();
            bool AlreadyDealtWith(Result r) =>
                r.submission.actioned_by.Any(a => string.Equals(a.outcome, "awarded", StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrEmpty(currentMemberId) && r.submission.actioned_by.Any(a => a.member_id == currentMemberId));
            var people = awards
                .Where(r => !AlreadyDealtWith(r))
                .GroupBy(r => r.member.id)
                .Select(g =>
                {
                    var first = g.First().member;
                    return new
                    {
                        Last = first.last_name,
                        First = first.first_name,
                        Person = new AwardPerson
                        {
                            MemberId = g.Key,
                            Name = $"{first.first_name} {first.last_name}".Trim(),
                            SortName = $"{first.last_name} {first.first_name}".Trim(),
                            Awards = g.Select(r => ToAwardRequest(r, currentMemberId))
                                .OrderBy(a => a.Item.AchievementName, StringComparer.CurrentCultureIgnoreCase)
                                .ThenBy(a => a.Item.SubmissionDate)
                                .ThenBy(a => a.Item.SubmissionId, StringComparer.Ordinal)
                                .ToList()
                        }
                    };
                })
                .OrderBy(p => p.Last, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(p => p.First, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(p => p.Person.MemberId, StringComparer.Ordinal)
                .Select(p => p.Person)
                .ToList();
            return new AwardRequestList { People = people, Hidden = awards.Where(AlreadyDealtWith).Select(r => r.submission.id).ToHashSet() };
        }

        private static AwardRequest ToAwardRequest(Result result, string? currentMemberId)
        {
            string reason = string.Empty;
            // With no idea who the leader is nothing can be checked, so nothing can be selected
            if (string.IsNullOrEmpty(currentMemberId))
            {
                reason = "Your own member id could not be found";
            }
            else if (result.member.id == currentMemberId)
            {
                reason = "Your own award";
            }
            return new AwardRequest { Item = PendingApprovalQueue.FromResult(result), CanAward = reason.Length == 0, Reason = reason };
        }
    }

    // Which award requests the leader has ticked. A person can be ticked whole or award by award; only requests the leader
    // can act on can be ticked.
    public class AwardSelection
    {
        private readonly AwardRequestList _list;
        private readonly HashSet<string> _selected = new();

        public AwardSelection(AwardRequestList list)
        {
            _list = list;
        }

        public int Count => _selected.Count;

        public bool IsSelected(string submissionId) => _selected.Contains(submissionId);

        public void Toggle(string submissionId)
        {
            if (_selected.Remove(submissionId))
            {
                return;
            }
            if (_list.People.SelectMany(p => p.Awards).Any(a => a.CanAward && a.Item.SubmissionId == submissionId))
            {
                _selected.Add(submissionId);
            }
        }

        // How many of the person's awards that can be awarded are ticked, and how many could be
        public (int Selected, int Selectable) ForPerson(string memberId)
        {
            var awards = Selectable(memberId);
            return (awards.Count(a => _selected.Contains(a.Item.SubmissionId)), awards.Count);
        }

        public bool IsPersonFullySelected(string memberId)
        {
            var (selected, selectable) = ForPerson(memberId);
            return selectable > 0 && selected == selectable;
        }

        // Ticks all of the person's awards, or if they are all ticked already, unticks them
        public void TogglePerson(string memberId)
        {
            var awards = Selectable(memberId);
            if (IsPersonFullySelected(memberId))
            {
                foreach (var award in awards) _selected.Remove(award.Item.SubmissionId);
            }
            else
            {
                foreach (var award in awards) _selected.Add(award.Item.SubmissionId);
            }
        }

        public void SelectAll()
        {
            foreach (var award in _list.People.SelectMany(p => p.Awards).Where(a => a.CanAward))
                _selected.Add(award.Item.SubmissionId);
        }

        public void Clear() => _selected.Clear();

        // The ticked requests, in the page's order (by person, then achievement)
        public List<AwardRequest> SelectedAwards() =>
            _list.People.SelectMany(p => p.Awards).Where(a => _selected.Contains(a.Item.SubmissionId)).ToList();

        private List<AwardRequest> Selectable(string memberId) =>
            _list.People.Where(p => p.MemberId == memberId).SelectMany(p => p.Awards).Where(a => a.CanAward).ToList();
    }
}
