namespace Topo.Model.ProgramBuilder
{
    // Moving ideas about: from the list of ideas onto a night, from one night to another, and back to the list. What moves is the activity, its
    // game, location and challenge area, and the Terrain event it was filled in from; the leader in charge, the times and the duty patrol
    // belong to the night.
    public static class ProgramIdeas
    {
        // Offers what the unit's youth have proposed in Terrain as ideas. Each proposal is offered once: one that has been used or taken off is
        // not offered again, and one whose name is already an idea or an activity in the program is left out. How many were added.
        public static int AddProposals(ProgramDraft draft, IEnumerable<Topo.Model.Program.ProposedActivityModel> proposals)
        {
            var taken = draft.TakenProposals.ToHashSet();
            var titles = draft.Ideas.Select(i => i.Title).Concat(draft.NightEdits.Values.Concat(draft.Extras).Select(r => r.Activity))
                .Select(t => t.Trim()).Where(t => t != "").ToHashSet(StringComparer.OrdinalIgnoreCase);
            var areas = new[] { "outdoors", "community", "personal_growth", "creative" };
            var added = 0;
            foreach (var proposal in proposals.OrderBy(p => p.Submitted))
            {
                var key = proposal.OpenKey;
                if (taken.Contains(key))
                    continue;
                // seen now whether or not it is added, so a name that is on the program already does not turn up if that is later cleared
                taken.Add(key);
                draft.TakenProposals.Add(key);
                if (!titles.Add(proposal.Title.Trim()))
                    continue;
                draft.Ideas.Add(new ProgramIdea
                {
                    Title = proposal.Title.Trim(),
                    ChallengeArea = areas.Contains(proposal.ChallengeAreaKey) ? proposal.ChallengeAreaKey : "",
                    ProposedBy = proposal.Patrol == "" ? proposal.ProposedBy : $"{proposal.ProposedBy}, {proposal.Patrol}",
                    ProposalId = key
                });
                added++;
            }
            return added;
        }

        // Put an idea on a night. Whatever was on the night goes back to the ideas. False when the idea or the night is not there.
        public static bool Assign(ProgramDraft draft, string ideaId, DateTime night)
        {
            var idea = draft.Ideas.FirstOrDefault(i => i.Id == ideaId);
            if (idea == null || !IsOpenNight(draft, night))
                return false;

            var row = Edit(draft, night);
            var replaced = Take(row);
            row.Activity = idea.Title;
            row.Game = idea.Game;
            row.Location = idea.Location;
            row.ChallengeArea = idea.ChallengeArea;
            row.TerrainEventId = "";
            row.ScoutMethod = new List<string>();
            draft.Ideas.Remove(idea);
            if (replaced != null)
                draft.Ideas.Add(replaced);
            return true;
        }

        // Move what is on a night to another night; when the other has something the two swap. False when there is nothing to move or a night is
        // not one that is running.
        public static bool Move(ProgramDraft draft, DateTime from, DateTime to)
        {
            if (from.Date == to.Date || !IsOpenNight(draft, from) || !IsOpenNight(draft, to))
                return false;
            var source = Edit(draft, from);
            if (source.Activity.Trim() == "")
                return false;

            var target = Edit(draft, to);
            var (activity, game, location, area, eventId, method) = (source.Activity, source.Game, source.Location, source.ChallengeArea, source.TerrainEventId, source.ScoutMethod);
            (source.Activity, source.Game, source.Location, source.ChallengeArea, source.TerrainEventId, source.ScoutMethod) = (target.Activity, target.Game, target.Location, target.ChallengeArea, target.TerrainEventId, target.ScoutMethod);
            (target.Activity, target.Game, target.Location, target.ChallengeArea, target.TerrainEventId, target.ScoutMethod) = (activity, game, location, area, eventId, method);
            return true;
        }

        // Take what is on a night back to the ideas. False when there was nothing on it.
        public static bool Unassign(ProgramDraft draft, DateTime night)
        {
            if (!draft.NightEdits.TryGetValue(ProgramPlanner.NightKey(night), out var row))
                return false;
            var idea = Take(row);
            if (idea == null)
                return false;
            draft.Ideas.Add(idea);
            return true;
        }

        // The idea that is on a row, cleared from it; null when the row has no activity
        private static ProgramIdea? Take(ProgramRow row)
        {
            if (row.Activity.Trim() == "")
                return null;
            var idea = new ProgramIdea { Title = row.Activity, Game = row.Game, Location = row.Location, ChallengeArea = row.ChallengeArea };
            row.Activity = row.Game = row.Location = row.ChallengeArea = row.TerrainEventId = "";
            row.ScoutMethod = new List<string>();
            return idea;
        }

        private static bool IsOpenNight(ProgramDraft draft, DateTime night) =>
            ProgramPlanner.Nights(draft).Any(n => n.Date.Date == night.Date && !n.Skipped);

        private static ProgramRow Edit(ProgramDraft draft, DateTime night)
        {
            var key = ProgramPlanner.NightKey(night);
            if (!draft.NightEdits.TryGetValue(key, out var row))
                draft.NightEdits[key] = row = new ProgramRow { Date = night.Date };
            return row;
        }
    }
}
