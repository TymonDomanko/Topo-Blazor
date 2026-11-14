using Topo.Model.Members;
using Topo.Model.ReadyToSubmit;

namespace Topo.Services
{
    // What was read from Terrain for a unit; the report is worked out from it as often as the options change
    public class ReadyToSubmitData
    {
        public List<ReadyMemberInput> Members { get; set; } = new List<ReadyMemberInput>();
        public ReadyTemplates Templates { get; set; } = new ReadyTemplates();
    }

    public interface IReadyToSubmitService
    {
        public Task<ReadyToSubmitData> LoadAsync(string unitId, List<MemberListModel> youth, string section, Action<string>? progress = null, CancellationToken cancellationToken = default);
    }

    public class ReadyToSubmitService : IReadyToSubmitService
    {
        private static readonly string[] Types =
        {
            ReadyToSubmitBuilder.Type_Milestone, ReadyToSubmitBuilder.Type_Oas, ReadyToSubmitBuilder.Type_Sia,
            "adventurous_journey", "course_reflection", "personal_reflection"
        };

        // How many templates are asked for at the same time
        private const int TemplatesAtOnce = 6;

        private readonly ITerrainAPIService _terrainAPIService;
        // What a template asks does not change from member to member, so it is read once
        private readonly Dictionary<string, List<FormQuestion>> _formTemplates = new Dictionary<string, List<FormQuestion>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<FormQuestion>> _oasTemplates = new Dictionary<string, List<FormQuestion>>(StringComparer.OrdinalIgnoreCase);

        public ReadyToSubmitService(ITerrainAPIService terrainAPIService)
        {
            _terrainAPIService = terrainAPIService;
        }

        public async Task<ReadyToSubmitData> LoadAsync(string unitId, List<MemberListModel> youth, string section, Action<string>? progress = null, CancellationToken cancellationToken = default)
        {
            var data = new ReadyToSubmitData();
            var ordered = ReadyToSubmitBuilder.InPatrolOrder(youth);

            // One read of each type gives every member's records, so the unit isn't read once per Scout. A unit with no
            // youth has nothing to read.
            var byMember = Enumerable.Empty<AchievementRecord>().ToLookup(r => r.MemberId);
            if (ordered.Count > 0)
            {
                progress?.Invoke("the unit's records");
                var responses = await Task.WhenAll(Types.Select(type => _terrainAPIService.GetUnitAchievementsJsonAsync(unitId, type)));
                cancellationToken.ThrowIfCancellationRequested();
                byMember = responses.SelectMany(ReadyParsers.ParseAchievements).ToLookup(r => r.MemberId);
            }
            foreach (var member in ordered)
            {
                data.Members.Add(new ReadyMemberInput
                {
                    MemberId = member.id,
                    MemberName = $"{member.first_name} {member.last_name}".Trim(),
                    FirstName = member.first_name ?? "",
                    LastName = member.last_name ?? "",
                    Patrol = member.patrol_name ?? "",
                    Records = byMember[member.id].ToList()
                });
            }

            // only the templates of what could be waiting are read
            var records = data.Members.SelectMany(m => m.Records).ToList();
            var formPaths = records.Where(r => ReadyToSubmitBuilder.NeedsFormTemplate(r, section)).Select(ReadyToSubmitBuilder.FormTemplatePath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var oasPaths = records.Where(r => ReadyToSubmitBuilder.NeedsOasTemplate(r, section)).Select(r => r.Template).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            // There can be dozens of templates and each is a separate request, so those not already known are read a few at a
            // time instead of one after another. A template that couldn't be read is tried again next time.
            var missing = formPaths.Where(p => !_formTemplates.ContainsKey(p)).Select(p => (path: p, isForm: true))
                .Concat(oasPaths.Where(p => !_oasTemplates.ContainsKey(p)).Select(p => (path: p, isForm: false)))
                .ToList();
            for (var i = 0; i < missing.Count; i += TemplatesAtOnce)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Invoke($"Checking what the forms and stages ask ({Math.Min(i + TemplatesAtOnce, missing.Count)} of {missing.Count})");
                var read = await Task.WhenAll(missing.Skip(i).Take(TemplatesAtOnce)
                    .Select(async t => (t.path, t.isForm, json: await _terrainAPIService.GetTemplateJsonAsync(t.path))));
                foreach (var (path, isForm, json) in read)
                {
                    if (isForm)
                    {
                        var questions = ReadyParsers.ParseFormQuestions(json);
                        if (questions.Count > 0)
                            _formTemplates[path] = questions;
                    }
                    else
                    {
                        var inputs = ReadyParsers.ParseOasInputs(json);
                        if (inputs.Count > 0)
                            _oasTemplates[path] = inputs;
                    }
                }
            }
            foreach (var path in formPaths)
                if (_formTemplates.TryGetValue(path, out var known))
                    data.Templates.Forms[path] = known;
            foreach (var path in oasPaths)
                if (_oasTemplates.TryGetValue(path, out var known))
                    data.Templates.OasInputs[path] = known;
            return data;
        }
    }
}
