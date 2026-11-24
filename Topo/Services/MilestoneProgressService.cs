using Topo.Model.Milestone;

namespace Topo.Services
{
    // What was read from Terrain for a unit; the pages are worked out from it as often as the options change
    public class MilestoneProgressData
    {
        public List<MilestoneProgressRecord> Records { get; set; } = new List<MilestoneProgressRecord>();
        public Dictionary<string, MilestoneForm> Forms { get; set; } = new Dictionary<string, MilestoneForm>();
    }

    public interface IMilestoneProgressService
    {
        public Task<MilestoneProgressData> LoadAsync(string unitId, string section, Action<string>? progress = null, CancellationToken cancellationToken = default);
    }

    public class MilestoneProgressService : IMilestoneProgressService
    {
        // How many review forms are asked for at the same time
        private const int FormsAtOnce = 4;

        private readonly ITerrainAPIService _terrainAPIService;
        // A form never changes once published, so each version of it is read once
        private readonly Dictionary<string, MilestoneForm> _forms = new Dictionary<string, MilestoneForm>();

        public MilestoneProgressService(ITerrainAPIService terrainAPIService)
        {
            _terrainAPIService = terrainAPIService;
        }

        public async Task<MilestoneProgressData> LoadAsync(string unitId, string section, Action<string>? progress = null, CancellationToken cancellationToken = default)
        {
            // One read gives every Scout's milestones, with the events that counted and the answers they saved on the review
            progress?.Invoke("the unit's milestones");
            var records = MilestoneProgressParser.Parse(await _terrainAPIService.GetUnitAchievementsJsonAsync(unitId, "milestone"));
            cancellationToken.ThrowIfCancellationRequested();

            // Each answered record is on a version of the review form; the questions are read as they were worded then. Each milestone's current
            // form is read as well, for a Scout who has written nothing yet.
            var reads = new List<(string key, Func<Task<string>> read)>();
            foreach (var r in records
                .Where(r => string.Equals(r.Section, section, StringComparison.OrdinalIgnoreCase) && r.FormKey != "" && r.Answers.Values.Any(v => !string.IsNullOrWhiteSpace(v)))
                .GroupBy(r => r.FormKey).Select(g => g.First()).Where(r => !_forms.ContainsKey(r.FormKey)))
            {
                var template = r.Template;
                var version = r.Version;
                reads.Add((r.FormKey, () => _terrainAPIService.GetTemplateVersionJsonAsync(template, version)));
            }
            foreach (var stage in MilestoneProgressBuilder.Milestones)
            {
                var key = MilestoneProgressBuilder.LatestKey(section, stage);
                var path = $"milestone/{section.ToLowerInvariant()}/review/{stage}";
                if (!_forms.ContainsKey(key))
                    reads.Add((key, () => _terrainAPIService.GetTemplateJsonAsync(path)));
            }
            if (reads.Count > 0)
            {
                progress?.Invoke("the review forms");
                foreach (var batch in reads.Chunk(FormsAtOnce))
                {
                    var read = await Task.WhenAll(batch.Select(async x => (x.key, form: MilestoneFormParser.Parse(await x.read()))));
                    cancellationToken.ThrowIfCancellationRequested();
                    // a form that couldn't be read is tried again next time
                    foreach (var (key, form) in read.Where(f => f.form.Questions.Count > 0))
                        _forms[key] = form;
                }
            }

            return new MilestoneProgressData { Records = records, Forms = new Dictionary<string, MilestoneForm>(_forms) };
        }
    }
}
