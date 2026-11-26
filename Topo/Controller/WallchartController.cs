using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Newtonsoft.Json;
using Topo.Model.Wallchart;
using Topo.Model.ReportGeneration;
using Topo.Model.Cache;
using Topo.Services;
using System.Reflection;

namespace Topo.Controller
{
    public class WallchartController : ComponentBase, IDisposable
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
        public IMembersService _membersService { get; set; }

        [Inject]
        public IWallchartService _wallchartService { get; set; }

        public WallchartPageViewModel model = new WallchartPageViewModel();
        // Loading and reading again through the data cache
        public PageLoad _page = new PageLoad();
        public bool Loading => _page.Loading;
        public string Progress => _page.Progress;
        // Laid out again whenever an option changes, from what was read for the unit
        public List<WallchartSheet>? Sheets;
        private List<WallchartItemModel>? _items;

        protected override async Task OnInitializedAsync()
        {
            if (!_storageService.IsAuthenticated)
            {
                NavigationManager.NavigateTo("index");
                return;
            }

            if (!string.IsNullOrEmpty(_storageService.UnitId))
            {
                model.UnitId = _storageService.UnitId;
                model.UnitName = _storageService.UnitName;
            }

            _page.Changed = () => InvokeAsync(StateHasChanged);
            model.GroupName = _storageService.GroupNameDisplay;
            model.Units = _storageService.Units;
            model.UseFullNames = _storageService.UseFullNames;
            if (!string.IsNullOrEmpty(model.UnitId))
                await Load();
        }

        internal async Task UnitChange(ChangeEventArgs e)
        {
            var unitId = e.Value?.ToString() ?? "";
            _storageService.UnitId = unitId;
            model.UnitId = unitId;
            model.UnitName = _storageService.UnitName;
            await Load();
        }

        // Reads the unit's wallchart; the preview then follows the options. What the data cache holds shows at once, and is read again behind the
        // page when it is more than a day old.
        private async Task Load()
        {
            Sheets = null;
            _items = null;
            model.ErrorMessage = "";
            if (string.IsNullOrEmpty(model.UnitId))
            {
                _page.Cancel();
                return;
            }

            _storageService.UseFullNames = model.UseFullNames;
            await _page.LoadAsync(ReadItems(model.UnitId), Show);
            ShowFailure();
        }

        // Reads everything on the page again, leaving what is shown in place until the new figures are in
        internal async Task RefreshClick()
        {
            model.ErrorMessage = "";
            await _page.RefreshAsync(ReadItems(model.UnitId), Show);
            ShowFailure();
        }

        private Func<Action<string>, CancellationToken, Task<List<WallchartItemModel>>> ReadItems(string unitId) =>
            (progress, token) => _wallchartService.GetWallchartItems(unitId, progress);

        private void Show(List<WallchartItemModel> items)
        {
            _items = items;
            Recalculate();
        }

        private void ShowFailure()
        {
            if (_page.Failed)
            {
                model.ErrorMessage = "Couldn't read the wallchart from Terrain.";
            }
        }

        public void Dispose() => _page.Dispose();

        internal void BreakByPatrolChanged() => Recalculate();

        // Built again from what is held, with the names to match the setting
        internal async Task UseFullNamesChanged()
        {
            _storageService.UseFullNames = model.UseFullNames;
            if (_items != null)
            {
                await _page.LoadAsync(ReadItems(model.UnitId), Show);
                ShowFailure();
            }
        }

        private void Recalculate() => Sheets = _items == null ? null : WallchartLayout.Build(_items, model.BreakByPatrol);

        internal async Task WallchartReportPdfClick() => await Download(OutputType.PDF, "pdf", "application/pdf");

        internal async Task WallchartReportXlsxClick() => await Download(OutputType.Excel, "xlsx", "application/vnd.ms-excel");

        // The report is made from what is on the page, so it is the same as the preview
        private async Task Download(OutputType outputType, string extension, string contentType)
        {
            if (_items == null || _items.Count == 0 || _page.Updating)
            {
                return;
            }

            try
            {
                var groupName = _storageService.GroupName ?? "";
                var unitName = _storageService.UnitName ?? "";
                var report = await _reportService.GetWallchartReport(groupName, _storageService.Section, unitName, outputType, JsonConvert.SerializeObject(_items), model.BreakByPatrol);
                var fileName = $"Wallchart_{model.UnitName.Replace(' ', '_')}.{extension}";

                // Send the data to JS to actually download the file
                await JS.InvokeVoidAsync("BlazorDownloadFile", fileName, contentType, report);
            }
            catch (Exception)
            {
                model.ErrorMessage = "Couldn't build the report.";
            }
        }
    }
}
