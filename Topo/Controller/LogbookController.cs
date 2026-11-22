using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Newtonsoft.Json;
using System.Runtime.InteropServices;
using Topo.Model.Logbook;
using Topo.Model.Members;
using Topo.Model.ReportGeneration;
using Topo.Model.SIA;
using Topo.Services;

namespace Topo.Controller
{
    public class LogbookController : ComponentBase
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
        public ILogbookService _LogbookService { get; set; }

        public LogbookPageViewModel model = new LogbookPageViewModel();
        public bool Loading;
        public string Progress = "";
        public string ErrorMessage = "";
        // The logbooks read for the unit, by member id. Switching "Include Leaders" on reads only the leaders added.
        private readonly Dictionary<string, MemberLogbook> _logbooks = new();
        private CancellationTokenSource? _cancel;
        private Task _running = Task.CompletedTask;
        private int _loadVersion;

        // The Scouts shown, in the order of the member list, as far as they have been read
        public List<MemberLogbook> Logbooks => model.Members.Where(m => _logbooks.ContainsKey(m.id)).Select(m => _logbooks[m.id]).ToList();
        // The people ticked beside their names; with none ticked the report is for everyone
        public HashSet<string> Ticked => model.Members.Where(m => m.selected).Select(m => m.id).ToHashSet();

        protected override async Task OnInitializedAsync()
        {
            if (!_storageService.IsAuthenticated)
                NavigationManager.NavigateTo("index");

            model.GroupName = _storageService.GroupNameDisplay;
            model.Units = _storageService.Units;
            if (!string.IsNullOrEmpty(_storageService.UnitId))
            {
                await UnitChange(_storageService.UnitId);
            }
        }

        internal async Task UnitChange(ChangeEventArgs e)
        {
            var unitId = e.Value?.ToString() ?? "";
            await UnitChange(unitId);
        }

        internal async Task UnitChange(string unitId)
        {
            // Whatever was being read for the last unit stops before this one starts: only one Scout's profile can be taken on at a time
            await StopReading();
            _logbooks.Clear();
            ErrorMessage = "";
            if (string.IsNullOrEmpty(unitId))
            {
                model.UnitId = unitId;
                _storageService.UnitId = model.UnitId;
                model.UnitName = _storageService.UnitName;
                model.Members = new List<MemberListModel>();
                return;
            }
            model.UnitId = unitId;
            _storageService.UnitId = model.UnitId;
            model.UnitName = _storageService.UnitName;
            await PopulateMembers();
            await Load();
        }

        private async Task StopReading()
        {
            _cancel?.Cancel();
            try
            {
                await _running;
            }
            catch (OperationCanceledException)
            {
            }
        }

        // Reads the logbook of each Scout not read yet, one at a time, showing each as soon as it is in
        private async Task Load()
        {
            var version = ++_loadVersion;
            var unread = model.Members.Where(m => !_logbooks.ContainsKey(m.id))
                .Select(m => new KeyValuePair<string, string>(m.id, $"{m.first_name} {m.last_name}")).ToList();
            if (unread.Count == 0)
                return;

            _cancel = new CancellationTokenSource();
            Loading = true;
            _running = ReadAsync(unread, version, _cancel.Token);
            await _running;
        }

        private async Task ReadAsync(List<KeyValuePair<string, string>> unread, int version, CancellationToken cancellationToken)
        {
            try
            {
                await _LogbookService.ReadLogbooksAsync(unread,
                    logbook => { if (version == _loadVersion) { _logbooks[logbook.MemberId] = logbook; InvokeAsync(StateHasChanged); } },
                    step => { if (version == _loadVersion) { Progress = step; InvokeAsync(StateHasChanged); } },
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception)
            {
                if (version == _loadVersion)
                    ErrorMessage = "Couldn't read the logbooks from Terrain.";
            }
            finally
            {
                if (version == _loadVersion)
                {
                    Loading = false;
                    Progress = "";
                }
            }
        }

        // Ticking a name puts that person in the report; it is for everyone while none are ticked
        internal void Toggle((string Id, bool Ticked) change)
        {
            var member = model.Members.FirstOrDefault(m => m.id == change.Id);
            if (member != null)
                member.selected = change.Ticked;
        }

        internal void ClearTicks()
        {
            foreach (var member in model.Members)
                member.selected = false;
        }

        internal async Task IncludeLeadersChange(ChangeEventArgs e)
        {
            var includeLeaders = (bool)e.Value;
            model.IncludeLeaders = includeLeaders;
            if (model.Members.Any())
            {
                await StopReading();
                await PopulateMembers();
                await Load();
            }
        }

        private async Task PopulateMembers()
        {
            var allMembers = await _membersService.GetMembersAsync(model.UnitId);
            if (model.IncludeLeaders)
                model.Members = allMembers.OrderBy(m => m.first_name).ThenBy(m => m.last_name).ToList();
            else
                model.Members = allMembers.Where(m => m.isAdultLeader == 0).OrderBy(m => m.first_name).ThenBy(m => m.last_name).ToList();
        }

        internal async Task LogbookReportPdfClick()
        {
            byte[] report = await LogbookReport(OutputType.PDF);
            var fileName = $"Logbook_{model.UnitName.Replace(' ', '_')}.pdf";

            // Send the data to JS to actually download the file
            await JS.InvokeVoidAsync("BlazorDownloadFile", fileName, "application/pdf", report);
        }

        internal async Task LogbookReportXlsxClick()
        {
            byte[] report = await LogbookReport(OutputType.Excel);
            var fileName = $"Logbook_{model.UnitName.Replace(' ', '_')}.xlsx";

            // Send the data to JS to actually download the file
            await JS.InvokeVoidAsync("BlazorDownloadFile", fileName, "application/vnd.ms-excel", report);
        }

        // The report is made from what is on the page: the ticked people's entries, or everyone's when none are ticked
        internal async Task<byte[]> LogbookReport(OutputType outputType = OutputType.PDF)
        {
            var chosen = ReportScope.Chosen(model.Members).Where(m => _logbooks.ContainsKey(m.id)).Select(m => _logbooks[m.id]);
            var serialisedReportData = JsonConvert.SerializeObject(MemberLogbook.ReportRows(chosen));

            var groupName = _storageService.GroupName ?? "";
            var unitName = _storageService.UnitName ?? "";
            var section = _storageService.Section;

            return await _reportService.GetLogbookReport(groupName, section, unitName, outputType, serialisedReportData);
        }

    }
}
