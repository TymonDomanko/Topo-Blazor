using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Newtonsoft.Json;
using Topo.Model.History;
using Topo.Model.Members;
using Topo.Model.ReportGeneration;
using Topo.Model.Cache;
using Topo.Services;

namespace Topo.Controller
{
    public class ScoutingHistoryController : ComponentBase, IDisposable
    {
        [Inject]
        public StorageService _storageService { get; set; }

        [Inject]
        public IMembersService _membersService { get; set; }

        [Inject]
        public IHistoryService _historyService { get; set; }

        [Inject]
        public IReportService _reportService { get; set; }

        [Inject]
        IJSRuntime JS { get; set; }

        [Inject]
        public NavigationManager NavigationManager { get; set; }

        public ScoutingHistoryPageViewModel model = new ScoutingHistoryPageViewModel();

        // Loading and reading again through the data cache; picking another member overtakes one still loading
        public PageLoad _page = new PageLoad();
        public bool Loading => _page.Loading;
        public string Progress => _page.Progress;

        protected override async Task OnInitializedAsync()
        {
            if (!_storageService.IsAuthenticated)
            {
                NavigationManager.NavigateTo("index");
                return;
            }

            _page.Changed = () => InvokeAsync(StateHasChanged);
            model.GroupName = _storageService.GroupNameDisplay;
            model.Units = _storageService.Units ?? new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(_storageService.UnitId))
            {
                model.UnitId = _storageService.UnitId;
                await PopulateMembers();
            }
        }

        internal async Task UnitChange(ChangeEventArgs e)
        {
            model.UnitId = e.Value?.ToString() ?? "";
            _storageService.UnitId = model.UnitId;
            model.MemberId = "";
            model.History = null;
            model.ErrorMessage = "";
            _page.Cancel();
            await PopulateMembers();
        }

        private async Task PopulateMembers()
        {
            using var scope = CacheScope.Begin(CacheMode.Fresh);
            model.Members = string.IsNullOrEmpty(model.UnitId)
                ? new List<MemberListModel>()
                : (await _membersService.GetMembersAsync(model.UnitId))
                    .OrderBy(m => m.first_name).ThenBy(m => m.last_name).ToList();
        }

        internal async Task MemberChange(ChangeEventArgs e)
        {
            model.MemberId = e.Value?.ToString() ?? "";
            model.History = null;
            model.ErrorMessage = "";
            if (string.IsNullOrEmpty(model.MemberId))
            {
                _page.Cancel();
                return;
            }

            // what the data cache holds shows at once, and is read again behind the page when it is more than a day old
            await _page.LoadAsync(ReadHistory(), Show);
            ShowFailure();
        }

        // Reads the member's history again, leaving what is shown in place until it is in
        internal async Task RefreshClick()
        {
            model.ErrorMessage = "";
            await _page.RefreshAsync(ReadHistory(), Show);
            ShowFailure();
        }

        private Func<Action<string>, CancellationToken, Task<ScoutingHistoryModel>> ReadHistory()
        {
            var memberId = model.MemberId;
            var member = model.Members.FirstOrDefault(m => m.id == memberId);
            return (progress, cancellationToken) =>
                _historyService.GetScoutingHistory(memberId, $"{member?.first_name} {member?.last_name}".Trim(), member?.patrol_duty ?? "", progress);
        }

        private void Show(ScoutingHistoryModel history) => model.History = history;

        private void ShowFailure()
        {
            if (_page.Failed)
            {
                model.ErrorMessage = "Couldn't load that member's history.";
            }
        }

        public void Dispose() => _page.Dispose();

        internal async Task DownloadClick(OutputType outputType)
        {
            if (model.History == null || _page.Updating)
            {
                return;
            }

            // the logo is the section the member is in now
            var section = model.History.Sections.LastOrDefault()?.SectionKey ?? "";
            var report = await _reportService.GetScoutingHistoryReport(
                _storageService.GroupName ?? "Group Name", section, _storageService.UnitName ?? "Unit Name", outputType, JsonConvert.SerializeObject(model.History));
            var isPdf = outputType == OutputType.PDF;
            var fileName = $"ScoutingHistory_{model.History.MemberName.Replace(' ', '_')}" + (isPdf ? ".pdf" : ".xlsx");
            await JS.InvokeVoidAsync("BlazorDownloadFile", fileName, isPdf ? "application/pdf" : "application/vnd.ms-excel", report);
        }
    }
}
