using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Newtonsoft.Json;
using Topo.Model.OAS;
using Topo.Model.ReportGeneration;
using Topo.Services;

namespace Topo.Controller
{
    // The OAS Progress page: the worksheets for a skill (or the three core skills) at a level, with the levels below it and the branches
    // at it as the leader wants them
    public class OasProgressController : ComponentBase
    {
        [Inject]
        public StorageService _storageService { get; set; }

        [Inject]
        IJSRuntime JS { get; set; }

        [Inject]
        public IReportService _reportService { get; set; }

        [Inject]
        public NavigationManager NavigationManager { get; set; }

        [Inject]
        public IOASService _oasService { get; set; }

        [Inject]
        public IMembersService _membersService { get; set; }

        public OASPageViewModel model = new OASPageViewModel();
        public bool Loading;
        public string Progress = "";
        // The branches the skills have taken at the chosen level, for the leader to pick from
        public List<OasBranchChoice> BranchChoices = new List<OasBranchChoice>();
        // What the worksheets will be made for, and what is left out because everyone in the unit has it already
        public OasWorksheetSelectionResult Selection = new OasWorksheetSelectionResult();
        private HashSet<string> _everyoneHas = new HashSet<string>();
        // The highest stage anyone in the unit has been awarded in each skill, for "one above the highest"
        private Dictionary<string, int> _highest = new Dictionary<string, int>();
        private int _loadId;
        // The worksheets on the screen, made from what has been read for the stages picked. Each stage is read once for the unit
        // and the options that change what is read, so changing the picks only reads what is new.
        public List<OasWorksheetSheet> Sheets = new List<OasWorksheetSheet>();
        private readonly Dictionary<string, List<OASWorksheetAnswers>> _answers = new Dictionary<string, List<OASWorksheetAnswers>>();
        private List<OASWorksheetAnswers> _shown = new List<OASWorksheetAnswers>();
        private int _viewId;

        // The skills to pick from, in the order Terrain lists them
        public List<string> Skills => model.Stages.Select(s => s.Stream).Distinct().ToList();

        // The levels to pick from
        public IEnumerable<int> Levels => Enumerable.Range(1, Math.Max(OasWorksheetSelection.HighestLevel(model.Stages), 9));

        public bool CanMake => !Loading && !string.IsNullOrEmpty(model.UnitId) && Selection.Included.Count > 0;

        protected override async Task OnInitializedAsync()
        {
            if (!_storageService.IsAuthenticated)
            {
                NavigationManager.NavigateTo("index");
                return;
            }

            model.GroupName = _storageService.GroupNameDisplay;
            model.Units = _storageService.Units;
            model.UseFullNames = _storageService.UseFullNames;
            try
            {
                model.Stages = await _oasService.GetOASStagesList();
            }
            catch (Exception)
            {
                model.ErrorMessage = "Couldn't read the Outdoor Adventure Skills from Terrain.";
            }
            if (!string.IsNullOrEmpty(_storageService.UnitId))
            {
                await UnitChange(_storageService.UnitId);
            }
        }

        internal async Task UnitChange(ChangeEventArgs e) => await UnitChange(e.Value?.ToString() ?? "");

        // The unit's Scouts and what they have been awarded are read so levels everyone already has can be left out
        internal async Task UnitChange(string unitId)
        {
            model.UnitId = unitId;
            _storageService.UnitId = unitId;
            model.UnitName = _storageService.UnitName;
            _everyoneHas = new HashSet<string>();
            _highest = new Dictionary<string, int>();
            _answers.Clear();
            var loadId = ++_loadId;
            if (string.IsNullOrEmpty(unitId))
            {
                Loading = false;
                Progress = "";
                Recalculate();
                await ShowWorksheets();
                return;
            }

            Loading = true;
            Progress = "what the Scouts have been awarded";
            try
            {
                var youth = (await _membersService.GetMembersAsync(unitId)).Where(m => m.isAdultLeader == 0).Select(m => m.id).ToList();
                var records = await _oasService.GetUnitOasRecords(unitId);
                if (loadId == _loadId)
                {
                    _everyoneHas = OasWorksheetSelection.AwardedToEveryone(youth, records.results);
                    _highest = OasWorksheetSelection.HighestAwarded(youth, records.results);
                }
            }
            catch (Exception)
            {
                // the worksheets can still be made; no level is left out
                if (loadId == _loadId)
                {
                    model.ErrorMessage = "Couldn't read what the Scouts have been awarded, so no levels are left out.";
                }
            }
            finally
            {
                if (loadId == _loadId)
                {
                    Loading = false;
                    Progress = "";
                    Recalculate();
                }
            }
            if (loadId == _loadId)
            {
                await ShowWorksheets();
            }
        }

        internal async Task SkillChanged(ChangeEventArgs e)
        {
            model.Skill = e.Value?.ToString() ?? "";
            TickAllBranches();
            Recalculate();
            await ShowWorksheets();
        }

        internal async Task LevelChanged(ChangeEventArgs e)
        {
            model.Level = int.TryParse(e.Value?.ToString(), out var level) ? level : 0;
            TickAllBranches();
            Recalculate();
            await ShowWorksheets();
        }

        internal async Task IncludeBelowChanged()
        {
            Recalculate();
            await ShowWorksheets();
        }

        internal async Task BranchChanged(OasBranchChoice choice, bool ticked)
        {
            if (ticked)
            {
                model.ChosenBranches.Add(choice.Key);
            }
            else
            {
                model.ChosenBranches.Remove(choice.Key);
            }
            Recalculate();
            await ShowWorksheets();
        }

        // These change what is read (who is listed, how they are named), so the worksheets are read again
        internal async Task ReadOptionChanged() => await ShowWorksheets();

        // These only change how what has been read is laid out
        internal void BreakByPatrolChanged() => Layout();

        private void Layout() => Sheets = OasWorksheetLayout.Build(_shown, model.BreakByPatrol);

        // What was read for a stage depends on the unit and on these options
        private string ReadKey(OASStageListModel stage) => $"{stage.TemplateLink}|{model.HideCompletedMembers}|{model.UseFullNames}";

        // Makes the worksheets on the screen for what is picked, reading only the stages not read yet. A read that has been overtaken by a
        // newer choice stops there, so the screen always follows the latest choice.
        private async Task ShowWorksheets()
        {
            var viewId = ++_viewId;
            if (!CanMake)
            {
                if (string.IsNullOrEmpty(model.UnitId) || Selection.Included.Count == 0)
                {
                    _shown = new List<OASWorksheetAnswers>();
                    Layout();
                }
                return;
            }

            model.ErrorMessage = "";
            Loading = true;
            await InvokeAsync(StateHasChanged);
            try
            {
                var answers = await ReadWorksheets(Selection.Included.ToList(), viewId);
                if (answers == null || viewId != _viewId)
                {
                    return;
                }
                _shown = answers;
                Layout();
            }
            catch (Exception)
            {
                if (viewId == _viewId)
                {
                    model.ErrorMessage = "Couldn't read the worksheets from Terrain.";
                }
            }
            finally
            {
                if (viewId == _viewId)
                {
                    Loading = false;
                    Progress = "";
                }
            }
        }

        // The answers for the stages, from what has been read already and reading the rest. Null if a newer choice (viewId) took over.
        private async Task<List<OASWorksheetAnswers>?> ReadWorksheets(List<OASStageListModel> stages, int? viewId)
        {
            _storageService.UseFullNames = model.UseFullNames;
            var all = new List<OASWorksheetAnswers>();
            for (var i = 0; i < stages.Count; i++)
            {
                var key = ReadKey(stages[i]);
                if (!_answers.TryGetValue(key, out var answers))
                {
                    Progress = $"{stages[i].SelectListItemText} ({i + 1} of {stages.Count})";
                    await InvokeAsync(StateHasChanged);
                    var templateList = await _oasService.GetOASTemplate(stages[i].TemplateLink.Replace("/latest.json", ""));
                    answers = await _oasService.GenerateOASWorksheetAnswers(model.UnitId, stages[i], model.HideCompletedMembers, templateList);
                    if (viewId != null && viewId != _viewId)
                    {
                        return null;
                    }
                    _answers[key] = answers;
                }
                all.AddRange(answers);
            }
            return all;
        }

        // Every branch at the level starts ticked; the leader unticks the ones they do not want
        private void TickAllBranches()
        {
            BranchChoices = OasWorksheetSelection.BranchesAt(model.Stages, OasWorksheetSelection.StreamsFor(model.Skill), model.Level, _highest);
            model.ChosenBranches = BranchChoices.Select(c => c.Key).ToHashSet();
        }

        private void Recalculate()
        {
            var streams = OasWorksheetSelection.StreamsFor(model.Skill);
            BranchChoices = OasWorksheetSelection.BranchesAt(model.Stages, streams, model.Level, _highest);
            Selection = OasWorksheetSelection.Select(model.Stages, streams, model.Level, model.IncludeBelow, model.ChosenBranches, _everyoneHas, _highest);
        }

        internal async Task OASWorksheetPdfClick() => await Download(OutputType.PDF, "pdf", "application/pdf");

        internal async Task OASWorksheetXlsxClick() => await Download(OutputType.Excel, "xlsx", "application/vnd.ms-excel");

        private async Task Download(OutputType outputType, string extension, string contentType)
        {
            if (!CanMake)
            {
                return;
            }

            model.ErrorMessage = "";
            Loading = true;
            try
            {
                // what is on the screen is made from the same reads, so it is not read twice
                var sortedAnswers = await ReadWorksheets(Selection.Included.ToList(), null) ?? new List<OASWorksheetAnswers>();

                var groupName = _storageService.GroupName ?? "";
                var unitName = _storageService.UnitName ?? "";
                Progress = "the worksheet";
                await InvokeAsync(StateHasChanged);
                var report = await _reportService.GetOASWorksheetReport(groupName, _storageService.Section, unitName, outputType, JsonConvert.SerializeObject(sortedAnswers), model.BreakByPatrol, model.FormatLikeTerrain);
                var fileName = $"OAS_Worksheet_{model.UnitName.Replace(' ', '_')}.{extension}";

                // Send the data to JS to actually download the file
                await JS.InvokeVoidAsync("BlazorDownloadFile", fileName, contentType, report);
            }
            catch (Exception)
            {
                model.ErrorMessage = "Couldn't make the worksheets.";
            }
            finally
            {
                Loading = false;
                Progress = "";
            }
        }
    }
}
