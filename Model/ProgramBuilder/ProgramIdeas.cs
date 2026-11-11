namespace Topo.Model.ProgramBuilder
{
    // Moving ideas about: from the list of ideas onto a night, from one night to another, and back to the list. What moves is the activity, its
    // game, location and challenge area, and the Terrain event it was filled in from; the leader in charge, the times and the duty patrol
    // belong to the night.
    public static class ProgramIdeas
    {
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
            var (activity, game, location, area, eventId) = (source.Activity, source.Game, source.Location, source.ChallengeArea, source.TerrainEventId);
            (source.Activity, source.Game, source.Location, source.ChallengeArea, source.TerrainEventId) = (target.Activity, target.Game, target.Location, target.ChallengeArea, target.TerrainEventId);
            (target.Activity, target.Game, target.Location, target.ChallengeArea, target.TerrainEventId) = (activity, game, location, area, eventId);
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
