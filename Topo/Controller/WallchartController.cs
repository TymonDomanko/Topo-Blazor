using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Newtonsoft.Json;
using Topo.Model.Wallchart;
using Topo.Model.ReportGeneration;
using Topo.Services;
using System.Reflection;

namespace Topo.Controller
{
    public class WallchartController : ComponentBase, IDisposable
    {
        [Inject]
        public ILogbookTotalsService _logbookTotals { get; set; }

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
        public bool Loading;
        public string Progress = "";
        // Laid out again whenever an option changes, from what was read for the unit
        public List<WallchartSheet>? Sheets;
        private List<WallchartItemModel>? _items;
        // Reading logbook totals again behind the page; the reports wait until it is done
        public bool Updating;
        public DateTime? TotalsReadUtc;
        // Counts the loads, so one that a later one has overtaken (another unit picked) does not touch the page
        private int _load;
        private CancellationTokenSource? _update;

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

            model.GroupName = _storageService.GroupNameDisplay;
            model.Units = _storageService.Units;
            model.SuppressLastName = _storageService.SuppressLastName;
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

        // Reads the unit's wallchart once; the preview then follows the options. Logbook totals older than a day are shown straight away and then
        // read again behind the page.
        private async Task Load()
        {
            var load = StartLoad();
            Sheets = null;
            _items = null;
            TotalsReadUtc = null;
            model.ErrorMessage = "";
            if (string.IsNullOrEmpty(model.UnitId))
                return;

            Loading = true;
            try
            {
                _storageService.SuppressLastName = model.SuppressLastName;
                var items = await _wallchartService.GetWallchartItems(model.UnitId, step => Report(load, step));
                if (load != _load)
                {
                    return;
                }
                _items = items;
                Recalculate();
                await ReadAge(load);
                Loading = false;
                Progress = "";
                if (await _wallchartService.HasStaleLogbookTotalsAsync(items))
                {
                    await UpdateTotals(load, items, everyScout: false);
                }
            }
            catch (Exception)
            {
                if (load == _load)
                {
                    model.ErrorMessage = "Couldn't read the wallchart from Terrain.";
                }
            }
            finally
            {
                if (load == _load)
                {
                    Loading = false;
                    Progress = "";
                }
            }
        }

        // Reads everything on the page again, leaving what is shown in place until the new figures are in
        internal async Task RefreshClick()
        {
            if (_items == null || Loading || Updating)
            {
                return;
            }
            var load = StartLoad();
            model.ErrorMessage = "";
            Updating = true;
            try
            {
                _storageService.CachedWallchartItems.RemoveAll(c => c.Key == model.UnitId);
                var items = await _wallchartService.GetWallchartItems(model.UnitId, step => Report(load, step));
                if (load != _load)
                {
                    return;
                }
                _items = items;
                Recalculate();
                await UpdateTotals(load, items, everyScout: true);
            }
            catch (Exception)
            {
                if (load == _load)
                {
                    model.ErrorMessage = "Couldn't read the wallchart from Terrain.";
                }
            }
            finally
            {
                if (load == _load)
                {
                    Updating = false;
                    Progress = "";
                }
            }
        }

        private async Task UpdateTotals(int load, List<WallchartItemModel> items, bool everyScout)
        {
            Updating = true;
            StateHasChanged();
            try
            {
                await _wallchartService.UpdateLogbookTotalsAsync(items, everyScout, step => Report(load, step), _update!.Token);
                if (load == _load)
                {
                    Recalculate();
                    await ReadAge(load);
                }
            }
            catch (OperationCanceledException)
            {
                // the page was left or another unit picked
            }
            finally
            {
                if (load == _load)
                {
                    Updating = false;
                    Progress = "";
                }
            }
        }

        private int StartLoad()
        {
            _update?.Cancel();
            _update = new CancellationTokenSource();
            Updating = false;
            Progress = "";
            return ++_load;
        }

        private void Report(int load, string step)
        {
            if (load == _load)
            {
                Progress = step;
                InvokeAsync(StateHasChanged);
            }
        }

        private async Task ReadAge(int load)
        {
            var oldest = await _logbookTotals.OldestReadAsync(_items?.Select(i => i.MemberId) ?? Enumerable.Empty<string>());
            if (load == _load)
            {
                TotalsReadUtc = oldest;
            }
        }

        public void Dispose()
        {
            _load++;
            _update?.Cancel();
        }

        internal void BreakByPatrolChanged() => Recalculate();

        // The items are kept for the unit, so asking again only changes the names to match the setting
        internal async Task SuppressLastNameChanged()
        {
            _storageService.SuppressLastName = model.SuppressLastName;
            if (_items != null)
                _items = await _wallchartService.GetWallchartItems(model.UnitId);
            Recalculate();
        }

        private void Recalculate() => Sheets = _items == null ? null : WallchartLayout.Build(_items, model.BreakByPatrol);

        internal async Task WallchartReportPdfClick() => await Download(OutputType.PDF, "pdf", "application/pdf");

        internal async Task WallchartReportXlsxClick() => await Download(OutputType.Excel, "xlsx", "application/vnd.ms-excel");

        // The report is made from what is on the page, so it is the same as the preview
        private async Task Download(OutputType outputType, string extension, string contentType)
        {
            if (_items == null || _items.Count == 0 || Updating)
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
