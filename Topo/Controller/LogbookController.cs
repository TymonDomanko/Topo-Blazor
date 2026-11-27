using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Newtonsoft.Json;
using System.Runtime.InteropServices;
using Topo.Model.Logbook;
using Topo.Model.Members;
using Topo.Model.ReportGeneration;
using Topo.Model.SIA;
using Topo.Model.Cache;
using Topo.Services;

namespace Topo.Controller
{
    public class LogbookController : ComponentBase, IDisposable
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
        // Reading again behind what is shown, and when the oldest of what is shown was read
        public bool Updating;
        public DateTime? ReadUtc;

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
            ReadUtc = null;
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
            _cancel?.Dispose();
            _cancel = null;
        }

        // Leaving the page stops the reading, so a Scout's profile is not left taken on for the next page. A Scout already being read is
        // finished first.
        public void Dispose() => _cancel?.Cancel();

        // Reads the logbook of each Scout not read yet, one at a time, showing each as soon as it is in. What the data cache holds shows first,
        // and is read again behind the page when it is more than a day old.
        private async Task Load()
        {
            var version = ++_loadVersion;
            var unread = Members(m => !_logbooks.ContainsKey(m.id));
            if (unread.Count == 0)
            {
                return;
            }

            _cancel = new CancellationTokenSource();
            Loading = true;
            _running = ReadAsync(unread, version, false, _cancel.Token);
            await _running;
        }

        // Reads every logbook on the page again, leaving each in place until its new one is in
        internal async Task RefreshClick()
        {
            if (Loading || Updating || model.Members.Count == 0)
            {
                return;
            }

            await StopReading();
            var version = ++_loadVersion;
            ErrorMessage = "";
            _cancel = new CancellationTokenSource();
            Updating = true;
            _running = ReadAsync(Members(m => true), version, true, _cancel.Token);
            await _running;
        }

        private List<KeyValuePair<string, string>> Members(Func<MemberListModel, bool> wanted) =>
            model.Members.Where(wanted).Select(m => new KeyValuePair<string, string>(m.id, $"{m.first_name} {m.last_name}")).ToList();

        private async Task ReadAsync(List<KeyValuePair<string, string>> members, int version, bool again, CancellationToken cancellationToken)
        {
            try
            {
                if (!again)
                {
                    var stale = await ReadPassAsync(members, version, CacheMode.Held, cancellationToken);
                    if (!stale || version != _loadVersion || cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }
                    Loading = false;
                    Updating = true;
                    Progress = "";
                }
                await ReadPassAsync(members, version, CacheMode.Reread, cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception)
            {
                if (version == _loadVersion)
                {
                    ErrorMessage = "Couldn't read the logbooks from Terrain.";
                }
            }
            finally
            {
                if (version == _loadVersion)
                {
                    Loading = false;
                    Updating = false;
                    Progress = "";
                }
            }
        }

        // One time through the Scouts. True when any of what was used is more than a day old.
        private async Task<bool> ReadPassAsync(List<KeyValuePair<string, string>> members, int version, CacheMode mode, CancellationToken cancellationToken)
        {
            var failed = new List<string>();
            using var scope = CacheScope.Begin(mode, behindShownData: Updating);
            await _LogbookService.ReadLogbooksAsync(members,
                logbook => { if (version == _loadVersion) { _logbooks[logbook.MemberId] = logbook; InvokeAsync(StateHasChanged); } },
                step => { if (version == _loadVersion) { Progress = step; InvokeAsync(StateHasChanged); } },
                cancellationToken,
                name => failed.Add(name));
            if (version != _loadVersion)
            {
                return false;
            }
            if (failed.Count > 0)
            {
                ErrorMessage = $"Couldn't read the logbook of {string.Join(", ", failed)}; Terrain would not give it.";
            }
            // when only some were read (leaders added to the list), the others may be older
            if (members.Count == model.Members.Count || ReadUtc == null || scope.OldestUtc < ReadUtc)
            {
                ReadUtc = scope.OldestUtc;
            }
            return scope.UsedStale;
        }

        // Ticking a name puts that person in the report; it is for everyone while none are ticked
        internal void Toggle((string Id, bool Ticked) change)
        {
            var member = model.Members.FirstOrDefault(m => m.id == change.Id);
            if (member != null)
            {
                member.selected = change.Ticked;
            }
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
            using var ownScope = CacheScope.Current == null ? CacheScope.Begin(CacheMode.Fresh) : null;
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
