using Topo.Model.Milestone;
using Topo.Model.SiaProgress;

namespace Topo.Services
{
    // What was read from Terrain for a unit; the pages are worked out from it as often as the options change
    public class MilestoneProgressData
    {
        public List<MilestoneProgressRecord> Records { get; set; } = new List<MilestoneProgressRecord>();
        public Dictionary<string, SiaTemplate> Forms { get; set; } = new Dictionary<string, SiaTemplate>();
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
        private readonly Dictionary<string, SiaTemplate> _forms = new Dictionary<string, SiaTemplate>();

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

            // Each answered record is on a version of the review form; the questions are read as they were worded then
            var wanted = records
                .Where(r => string.Equals(r.Section, section, StringComparison.OrdinalIgnoreCase) && r.FormKey != "" && r.Answers.Values.Any(v => !string.IsNullOrWhiteSpace(v)))
                .GroupBy(r => r.FormKey)
                .Select(g => g.First())
                .Where(r => !_forms.ContainsKey(r.FormKey))
                .ToList();
            if (wanted.Count > 0)
            {
                progress?.Invoke("the review forms");
                foreach (var batch in wanted.Chunk(FormsAtOnce))
                {
                    var read = await Task.WhenAll(batch.Select(async r => (key: r.FormKey, form: SiaTemplateParser.Parse(await _terrainAPIService.GetTemplateVersionJsonAsync(r.Template, r.Version)))));
                    cancellationToken.ThrowIfCancellationRequested();
                    // a form that couldn't be read is tried again next time
                    foreach (var (key, form) in read.Where(f => f.form.Questions.Count > 0))
                        _forms[key] = form;
                }
            }

            return new MilestoneProgressData { Records = records, Forms = new Dictionary<string, SiaTemplate>(_forms) };
        }
    }
}
