using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Newtonsoft.Json;
using Topo.Model.Cache;
using Topo.Model.Login;
using Topo.Model.Members;
using Topo.Model.ReportGeneration;
using Topo.Services;

namespace Topo.Controller
{

    public class ProgressController : ComponentBase, IDisposable
    {
        [Inject]
        public IMembersService _membersService { get; set; }

        [Inject]
        public StorageService _storageService { get; set; }

        [Inject]
        IJSRuntime JS { get; set; }

        [Inject]
        public IReportService _reportService { get; set; }

        [Inject]
        public NavigationManager NavigationManager { get; set; }

        public MembersPageViewModel model = new MembersPageViewModel();
        // Loading and reading again through the data cache
        public PageLoad _page = new PageLoad();
        public string ErrorMessage = "";

        protected async override Task OnInitializedAsync()
        {
            if (!_storageService.IsAuthenticated)
                NavigationManager.NavigateTo("index");

            _page.Changed = () => InvokeAsync(StateHasChanged);
            model.Units = _storageService.Units;
            model.GroupName = _storageService.GroupNameDisplay;
            if (!string.IsNullOrEmpty(_storageService.UnitId))
            {
                await populateMembers();
            }
            model.UnitId = _storageService.UnitId;
        }

        internal async Task UnitChange(ChangeEventArgs e)
        {
            var unitId = e.Value?.ToString() ?? "";
            if (string.IsNullOrEmpty(unitId))
            {
                model.UnitId = unitId;
                _storageService.UnitId = model.UnitId;
                model.UnitName = _storageService.UnitName;
                model.Members = new List<MemberListModel>();
                _page.Cancel();
                return;
            }
            model.UnitId = unitId;
            _storageService.UnitId = unitId;
            await populateMembers();
        }

        // What the data cache holds shows at once, and is read again behind the page when it is more than a day old
        async Task populateMembers()
        {
            model.Members = new List<MemberListModel>();
            model.UnitName = _storageService.UnitName;
            ErrorMessage = "";
            await _page.LoadAsync(ReadMembers(_storageService.UnitId), Show);
            ShowFailure();
        }

        internal async Task RefreshClick()
        {
            _membersService.ClearMemberCache(_storageService.UnitId);
            ErrorMessage = "";
            await _page.RefreshAsync(ReadMembers(_storageService.UnitId), Show);
            ShowFailure();
        }

        private Func<Action<string>, CancellationToken, Task<List<MemberListModel>>> ReadMembers(string unitId) =>
            (progress, token) => _membersService.GetMembersAsync(unitId);

        private void Show(List<MemberListModel> allMembers) =>
            model.Members = allMembers.Where(m => m.isAdultLeader == 0).OrderBy(m => m.first_name).ThenBy(m => m.last_name).ToList();

        private void ShowFailure()
        {
            if (_page.Failed)
            {
                ErrorMessage = "Couldn't read the members from Terrain.";
            }
        }

        public void Dispose() => _page.Dispose();

    }
}
