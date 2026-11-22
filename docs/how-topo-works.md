# How Topo works: a code-level guide

Topo is a web app that reads a leader's data from Scouts Australia's Terrain and turns it into on-screen previews and downloadable PDF and Excel reports. This guide walks through the code in the order a request travels, so you can follow any page from the browser to Terrain and back.

It describes the **`everything` branch** (upstream `develop` plus the open PRs and fork drafts). Where something exists only because of one of those, the text says so. File paths are relative to the repository root.

Contents

1. [The big picture](#1-the-big-picture)
2. [The projects](#2-the-projects)
3. [Starting up](#3-starting-up)
4. [Signing in and keeping state](#4-signing-in-and-keeping-state)
5. [Talking to Terrain](#5-talking-to-terrain)
6. [Anatomy of a page, followed end to end](#6-anatomy-of-a-page-followed-end-to-end)
7. [How a report is made](#7-how-a-report-is-made)
8. [The spinner and background work](#8-the-spinner-and-background-work)
9. [What is cached today](#9-what-is-cached-today)
10. [Tests](#10-tests)
11. [Running, building and deploying](#11-running-building-and-deploying)
12. [Gotchas and Terrain quirks](#12-gotchas-and-terrain-quirks)
13. [Where to look when you want to change something](#13-where-to-look-when-you-want-to-change-something)
14. [A good reading order](#14-a-good-reading-order)

---

## 1. The big picture

Topo has **no server of its own**. The whole app is a Blazor WebAssembly program: the browser downloads it once and runs it. It talks to two things directly:

```
                      ┌────────────────────────────────────────────┐
                      │  Your browser                              │
                      │                                            │
                      │   Topo (Blazor WebAssembly, C#)            │
                      │   pages ─► controllers ─► services         │
                      └───────┬──────────────────────────┬─────────┘
          sign in + data      │                          │  only for downloads
   (JSON over HTTPS, your     │                          │  (JSON in, base 64 file out)
    own Terrain login)        ▼                          ▼
        ┌───────────────────────────────┐     ┌─────────────────────────────┐
        │ Terrain (Scouts Australia)    │     │ AWS Lambda "report function"│
        │ members / achievements /      │     │ builds the Excel workbook,  │
        │ events / templates / metrics  │     │ turns it into PDF if asked  │
        │ + Cognito for sign in         │     │ (Syncfusion XlsIO)          │
        └───────────────────────────────┘     └─────────────────────────────┘
```

Three consequences shape most of the code:

- **Everything Topo knows comes from Terrain**, read live with the leader's own sign-in. There is no Topo database. The only things stored are in the browser's local storage (the saved session, a few settings).
- **Reports are the one thing that leaves the browser** for somewhere else. The page sends the Lambda the data it is already showing, and gets a finished file back. That is why the report's data shapes are shared classes (the `Model` project, below).
- **Because the browser calls Terrain directly**, anything slow is slow in front of the user, and anything Terrain limits (one assumed profile at a time, calendar switching) limits Topo. A lot of the service code is about reading Terrain efficiently.

## 2. The projects

The solution is `Topo-Blazor.sln`. Shared settings are in `Directory.Build.props` (`net10.0`, nullable and implicit usings on) and package versions are in `Directory.Packages.props`.

| Project | What it is | Who uses it |
|---|---|---|
| `Topo/` | The Blazor WebAssembly app: pages, controllers, services that call Terrain, the menu, the layout. This is the thing that runs in the browser. | Users |
| `Model/` | Plain classes and pure logic: the shapes of Terrain's answers, the page view models, the report data, and the rules and layouts that don't need Terrain. | **Both** the app and the Lambda (so both agree on the data) and the tests |
| `Services/` | `ReportService`, about 4,300 lines that build every report as an Excel workbook with Syncfusion XlsIO. | The Lambda |
| `TopoReportFunction/` | The AWS Lambda entry point. Receives a report request, calls `Services/ReportService`, returns a file. | AWS |
| `TopoReportServer/` | A local web server that serves the Topo pages **and** stands in for the Lambda, so everything runs on one machine ([docs/report-server.md](report-server.md)). | Developers |
| `TopoReportFunctionTestUnix/` (and an older twin, `TopoReportFunctionTest/`) | MSTest tests, about 540. They reference `TopoReportFunction`, so they reach `Model` and the report code but **not** the Blazor `Topo` project. | Developers, CI |

The dependency direction is: `Topo` → `Model`, and `TopoReportFunction` → `Model` + `Services`. `Topo` and the Lambda never reference each other; they only share `Model`.

**Why `Model` matters.** Two jobs land there on purpose:

1. *The contract between the page and the report.* The page serialises (for example) a `List<MilestoneSummaryListModel>` to JSON and the Lambda deserialises the same class. Change the class and both sides change together.
2. *Testability.* Because the tests can't reach the Blazor project, anything worth testing (what colour a cell is, how awards are ordered, which members count as youth) is written as a pure function or class in `Model`, for example `Model/Milestone/MilestoneLayout.cs` and `Model/Approvals/ApprovalsFilter.cs`.

`Model` is organised by feature, one folder each (`Model/Milestone`, `Model/Wallchart`, `Model/Logbook`, ...). Inside a feature folder you will usually find: Terrain's answer classes (`Get...ResultModel.cs`), a page view model (`...PageViewModel.cs`), the report or row data (`...ListModel.cs`), and a `...Layout.cs` that turns data into what the preview shows.

## 3. Starting up

### `Topo/wwwroot/index.html`

The page the browser first loads. It pulls in Bootstrap, jQuery and select2 (used by some multi-select boxes), the Syncfusion styles and scripts, and `_framework/blazor.webassembly.js`, which starts the .NET runtime. It also holds two small JavaScript functions the C# code calls:

- `BlazorDownloadFile(filename, contentType, content)` makes a file in memory and clicks a hidden link, which is how a finished report becomes a download.
- A drag-and-drop helper for the Program Builder rows.

While it loads, the user sees the "Please wait while Topo loads" text inside `<div id="app">`.

### `Topo/Program.cs`

This builds the app. The important lines:

```csharp
builder.RootComponents.Add<App>("#app");                 // App.razor is the root
builder.Services.AddSingleton<StorageService>();         // the one shared state object
builder.Services.AddScoped<ITerrainAPIService, TerrainAPIService>();
builder.Services.AddScoped<IMembersService, MembersService>();
// ... one AddScoped line per feature service (Logbook, Wallchart, Milestone, ...)
builder.Services.AddScoped<IReportService, ReportService>();
```

- **Dependency injection** is how the pieces find each other. A class asks for an interface (`IMilestoneService`) and Blazor hands it the registered implementation. Services are `Scoped`, which in WebAssembly means one instance for the whole life of the page. `StorageService` is a `Singleton` because it is the shared state.
- **The HttpClient** is registered twice. The later registration wins: it wraps a `DisplaySpinnerAutomaticallyHttpMessageHandler`, so *every* request made through it drives the spinner (section 8).
- **Logging** is replaced by `TopoLogger` (`Topo/TopoLogging.cs`), which writes to an in-memory queue when logging is switched on in the Logging page, so a leader can download a log to send for support.
- **The Syncfusion licence** is registered here. In a `DEBUG` build it is empty (so a trial banner shows); in a `Release` build the text `__SYNCFUSION-KEY__` is replaced by the real key during CI (section 11). The key is never in the repository.
- The culture is set to `en-AU` so dates and numbers format the Australian way.

### `Topo/App.razor`

The root component, with one subtle job. Before it shows the `Router`, it calls `LoginService.TryRestoreSessionAsync()` and shows the loading figures meanwhile. Every page checks `StorageService.IsAuthenticated` the moment it initialises, so if the router rendered first, a "Remember me" user would be bounced to the login page for one frame before their session restored. The router only appears once that check has finished.

### `Topo/Shared/MainLayout.razor`

The frame around every page: the sidebar (`NavMenu`), the `Spinner`, and `@Body`, where the current page is drawn. In this build it also starts the background check (`_autoAwards.Start()` after first render) and tells the `ActivityMonitor` whenever the user navigates, so background work knows when you are busy.

### The menu: `Topo/Shared/NavMenuItems.cs`

The menu is data, not markup. `NavMenuItems.Groups` is a list of groups, each a list of `NavItem(title, href, icon, audience)`. Each item has an `Audience` (`SignedOut`, `Youth`, `Leader`) and `NavMenu.razor` shows only the items for the current user. To add a page to the menu you add one line there.

## 4. Signing in and keeping state

### Signing in

`Topo/Pages/Login.razor` → `Topo/Controller/LoginController.cs` → `LoginService` → `TerrainAPIService`:

1. The user enters a branch (their state), a member number and a password.
2. `TerrainAPIService.LoginAsync` sends an `InitiateAuth` call to **AWS Cognito**, Terrain's sign-in service, with username `"{branch}-{memberNumber}"`. Terrain has four app client ids; Topo tries each in turn until one accepts the sign-in and remembers which (`StorageService.ClientId`).
3. Cognito answers with tokens: an **IdToken** (what Terrain's APIs check), an access token, a refresh token and an expiry. They are kept in `StorageService.AuthenticationResult`, with `TokenExpiry` set a minute early.
4. `LoginService.CompleteLoginSetupAsync` then calls Terrain for the user and their **profiles** (every group, unit and role the person holds) and works out:
   - which group and unit(s) they can see (`StorageService.Groups`, `Units`);
   - whether they are a leader or a youth. A profile with the group role `group-leader`, a `support-leader-group-*` role, or the unit role `unit-council` counts as a leader (all pages); otherwise they are a youth member (only the personal pages). `StorageService.IsYouthMember` records it.

"Remember me" saves the session (`ClientId`, tokens, expiry) in the browser's local storage under the key `TopoSession`. On the next visit `TryRestoreSessionAsync` loads it, forces a token refresh, and runs `CompleteLoginSetupAsync`. If the refresh token is dead it quietly falls back to "not signed in".

### `Topo/Services/StorageService.cs`: the shared state

A singleton that every service and page can read. It holds:

- **Who is signed in**: tokens, `IsAuthenticated`, `IsYouthMember`, the profiles.
- **What they chose**: the group (`GroupId`, `GroupName`) and unit (`UnitId`, `UnitName`, `Section`). `Units` and `UnitSections` are worked out from the profiles each time you ask. `UnitId` is remembered between pages, which is why most pages open with your unit already chosen.
- **A few in-memory caches** (members per unit, wallchart results, OAS templates, the award list; section 9).
- **UI flags**: icon-only menu, full names, logging on.
- The app **version string** (`Version`), shown in the menu and written into logs.

`StorageService.OnChange` is an event the layout listens to, so changing sign-in state redraws the menu.

## 5. Talking to Terrain

All Terrain access is in one class: `Topo/Services/TerrainAPIService.cs` (about 900 lines, behind `ITerrainAPIService`).

### The shape of every call

```csharp
public async Task<GetMembersResultModel?> GetMembersAsync(string selectedUnitId)
{
    await RefreshTokenAsync();                                   // 1. a fresh token
    string requestUri = $"{membersAddress}units/{selectedUnitId}/members";
    var result = await SendRequest(HttpMethod.Get, requestUri);  // 2. the HTTP call
    return DeserializeObject<GetMembersResultModel>(result);     // 3. JSON into a class
}
```

1. **`RefreshTokenAsync()`** checks `TokenExpiry` and, if the token has expired, swaps the refresh token for a new one with Cognito. Every method starts with it, so a long session never fails on an old token.
2. **`SendRequest`** builds the request, adds the header `authorization: <IdToken>` (just the token, no "Bearer"), sends it with the shared `HttpClient`, and logs the request and response.
3. **`DeserializeObject<T>`** turns the JSON into one of the classes in `Model` using Newtonsoft.Json. Terrain uses `snake_case` field names, so those model classes have properties like `member_id` and `date_awarded` that match exactly.

### Where the data lives

Terrain is several services, each on its own host (the `...Address` fields at the top of the class):

| Host | What it holds | Examples |
|---|---|---|
| `members.terrain.scouts.com.au` | People, units, profiles, patrols | a unit's members, profiles, assume a profile |
| `achievements.terrain.scouts.com.au` | Badges, progress, logbooks, approvals | milestones, OAS, SIA, a Scout's logbook |
| `events.terrain.scouts.com.au` | The calendar and events | events in a date range, one event in full, calendars |
| `templates.terrain.scouts.com.au` | Static definitions | the OAS trees, SIA templates, the list of additional awards |
| `metrics.terrain.scouts.com.au` | Group Life figures | per-member totals |
| Cognito (AWS) | Sign in | login, refresh |

### Unit-wide reads versus per-person reads

This distinction drives performance more than anything else.

- **Unit-wide**: one call returns a record type for *every* member, e.g. `GetUnitAchievementsOfType<GetMilestoneResultModel>(unitId, "milestone")` calls `achievements/units/{id}/achievements?type=milestone`. The result includes a `member_id` on each record, so the caller groups it with `ToLookup(r => r.member_id)`. These need no special setup and are fast per call.
- **Per-person**: one call per Scout, e.g. a Scout's logbook. A leader can't read another member's logbook directly; they have to **assume that member's profile** first (`AssumeProfile(memberId)`), read, then give it back (`RevokeAssumedProfiles()`). Terrain keeps **only one assumed profile at a time**, so these reads are strictly one Scout after another. This is the slow part of the Logbook, Wallchart and Additional Awards pages.

### Reading several things at once

- `Model/ReadsAtOnce.cs` runs a list of reads about six at a time and returns the answers **in the order given**, whatever order they finish in. Used for things like each logbook entry or each event.
- `Model/ReadOnce.cs` (this build) wraps it so an item already read for this page load is not read again.
- `ProgramService.LockCalendarsAsync` is a `SemaphoreSlim` lock. Reading events means switching the account's *selected calendars* in Terrain and putting them back, which is global account state, so only one such read may run at a time.

### Errors

`SendRequest` **catches every exception and returns an empty string**, and `DeserializeObject` falls back to an empty object. So a failed call usually shows up as "no data" rather than an exception. Pages that need to tell the user something went wrong wrap their work in `try/catch` and show a message such as "Couldn't read the milestones from Terrain." (Keep this in mind when something just looks empty: check the log, section 12.)

## 6. Anatomy of a page, followed end to end

Every feature follows the same pattern. The Milestone page is a clean example, so here it is from the click to the screen.

```
Topo/Pages/Milestone.razor                 the markup (@page "/milestone")
   @inherits Controller.MilestoneController
Topo/Controller/MilestoneController.cs     the page's code: state + event handlers
Topo/Services/MilestoneService.cs          reads Terrain and shapes the data
Model/Milestone/                           the data classes and the pure layout rules
Topo/Shared/MilestonePreview.razor         draws the preview from the layout
```

### The page is its own code-behind

`Milestone.razor` starts with `@page "/milestone"` (its address) and `@inherits Controller.MilestoneController`. That makes the Razor markup a subclass of `MilestoneController`, a `ComponentBase` in `Topo/Controller/`. So `@model.UnitId`, `UnitChange` and `Load` in the markup are simply members of the controller class. (It is the same idea as code-behind files; Topo just puts them in a `Controller` folder.) There is one controller per page.

### 1. The page opens: `OnInitializedAsync`

```csharp
protected override async Task OnInitializedAsync()
{
    if (!_storageService.IsAuthenticated) { NavigationManager.NavigateTo("index"); return; }
    model.UnitId = _storageService.UnitId;        // your remembered unit
    model.Units = _storageService.Units;          // for the drop-down
    if (!string.IsNullOrEmpty(model.UnitId))
        await Load();
}
```

The controller gets its services with `[Inject]` properties (`_storageService`, `_milestoneService`, `_reportService`, ...). Every page begins by bouncing the user to sign-in if they aren't authenticated.

### 2. `Load()` reads the unit once

```csharp
_summaries = await _milestoneService.GetMilestoneSummaries(model.UnitId, step => { Progress = step; InvokeAsync(StateHasChanged); });
Recalculate();
```

The second argument is a **progress callback**: the service calls it as it works ("Amy (3 of 39)") and the controller stores the text and asks Blazor to redraw. That is where the "Reading Amy (3 of 39)..." line comes from.

### 3. The service does the real work: `MilestoneService.GetMilestoneSummaries`

1. Gets the unit's members from `IMembersService` (cached per unit) and drops adult leaders (`isAdultLeader == 0` keeps the youth).
2. Makes **one** Terrain call for the whole unit's milestone records, then groups them by member.
3. Loops over the youth and works out each person's current milestone and how far through it they are, producing a `MilestoneSummaryListModel` each.

The service returns plain model objects. It never touches the screen.

### 4. A layout class shapes it for display: `MilestoneLayout.Build`

`Recalculate()` calls `MilestoneLayout.Build(_summaries, model.BreakByPatrol)` in `Model/Milestone/`. This is **pure logic**: which rows go in which table, the colour of each cell, the averages, the patrol grouping. Because it is in `Model` it is tested directly (`MilestoneLayout.cs` in the tests).

### 5. The preview draws it: `MilestonePreview.razor`

A small component in `Topo/Shared/` that takes the layout (`Groups`) as a parameter and renders the tables. Options like "Break by Patrol" just call `Recalculate()` again; **no new read from Terrain**, because the data for the unit is already held in `_summaries`.

### 6. Download: the page sends what it shows

```csharp
var report = await _reportService.GetMilestoneReport(groupName, section, unitName,
                 outputType, JsonConvert.SerializeObject(_summaries), model.BreakByPatrol);
await JS.InvokeVoidAsync("BlazorDownloadFile", fileName, contentType, report);
```

The report is made from the same `_summaries` the preview came from, so they always match. Section 7 follows that call.

### The reusable pieces on a page

- `Topo/Shared/PageStatus.razor`: the "Reading ..." line and the red error text, the same on every page (in this build).
- `Topo/Shared/ReportButtons.razor`: the "X (pdf)" and "X (xlsx)" buttons.
- The unit picker, "Break by Patrol" and "Use full names" boxes are repeated in each page's markup.
- Grids use Syncfusion's `SfGrid` (for example the Approvals page).

## 7. How a report is made

```
Controller.Download()
   └─► ReportService (browser)  Topo/Services/ReportService.cs
         builds a ReportGenerationRequest { ReportType, names, OutputType, ReportData = JSON }
         PUT ──────────────────────────────────────────────►  Lambda  (or TopoReportServer)
                                                               TopoReportFunction/Function.cs
                                                                 FunctionHandler:
                                                                   switch (request.ReportType)
                                                                   deserialise ReportData
                                                                   Services/ReportService.GenerateXxxWorkbook(...)
                                                                      (Syncfusion XlsIO builds an Excel workbook)
                                                                   OutputType PDF?  ── XlsIORenderer ─► PDF
                                                                   else             ── SaveAs ───────► .xlsx
                                                                   Convert.ToBase64String(bytes)
         ◄──────────────────────── base 64 text ──────────────────────────────────────────────┘
   Convert.FromBase64String ─► byte[] ─► JS BlazorDownloadFile ─► the file downloads
```

Step by step:

1. **The request.** `Topo/Services/ReportService.cs` has one method per report (`GetMilestoneReport`, `GetWallchartReport`, ...). Each fills in a `ReportGenerationRequest` (`Model/ReportGeneration/ReportGenerationRequest.cs`): a `ReportType` enum value, the group/section/unit names, the `OutputType` (`PDF` or `Excel`), a few flags (`BreakByPatrol`, ...), and **`ReportData`**: the page's data as a JSON string. `CallReportGeneratorFunction` sends it as an HTTP `PUT`.
2. **Where it goes.** The address is `ReportService.ReportFunctionUrl`: a hard-coded AWS Lambda function URL (a different one for `DEBUG` and `Release` builds), unless the setting `ReportFunctionUrl` overrides it. `wwwroot/appsettings.json` is empty on purpose; when the pages are served by `TopoReportServer`, that server answers `GET /appsettings.json` with its own address, which is how a local copy of Topo sends reports to the local server and not to AWS.
3. **The Lambda.** `TopoReportFunction/Function.cs`, `FunctionHandler`, registers the Syncfusion licence from the `SyncfusionLicense` environment variable, deserialises the request, and runs a `switch` on `ReportType`. Each case calls a small `GenerateXxxWorkbook` method that deserialises `ReportData` into the right model type and calls the matching method on `Services/ReportService.cs`.
4. **The workbook.** `Services/ReportService.cs` builds an **Excel workbook** cell by cell with Syncfusion XlsIO: columns, merged headings, fills, borders, widths, page setup. Every report, including the PDFs, starts as a workbook.
5. **PDF or Excel.** If the request wants a PDF, `XlsIORenderer.ConvertToPDF` converts the workbook; otherwise the workbook is saved as `.xlsx`. The bytes are returned **base 64 encoded** as plain text (the Lambda returns a string, not a binary body).
6. **Back in the browser.** `CallReportGeneratorFunction` decodes the text to bytes, the controller hands them to `BlazorDownloadFile`, and the browser saves the file.

Details worth knowing:

- Dates are sent as text on purpose (`DateParseHandling.None`) so they aren't shifted by time zone, and the request carries `CurrentUtcOffset` for the few places that convert.
- If the Lambda fails it returns an empty string, so the browser's decode produces nothing and the page shows "Couldn't build the report."
- **Changing a report's layout means changing `Services/ReportService.cs` and redeploying the Lambda**: users get new page code from GitHub Pages immediately, but the reports only change when the Lambda is updated.

To add a new report: add a `ReportType` value; add the data class in `Model`; add a `GetXxxReport` method to `Topo/Services/ReportService.cs` and its interface; add a `case` and `GenerateXxxWorkbook` in `Function.cs`; build the workbook in `Services/ReportService.cs`; call it from your controller as in section 6.

## 8. The spinner and background work

### The spinner

`Topo/DisplaySpinnerAutomaticallyHttpMessageHandler.cs` sits in the `HttpClient` pipeline. For each request it calls `SpinnerService.Show()` before and `Hide()` after. `SpinnerService` counts requests in flight and only fires its `OnShow`/`OnHide` events when the count goes from 0 to 1 and back, with a 400 ms delay before hiding so a page that makes many calls in a row keeps one steady spinner. `Topo/Shared/Spinner.razor` listens to those events and shows the loading figures.

In this build a request can opt out of the spinner (`SpinnerService.BeQuiet`) when a page shows its own progress line, and background work never shows one.

### Background work (this build only)

These pieces come from the Camper/Walkabout awards work and are the base the cache design ([background-cache-design.md](background-cache-design.md)) builds on:

- `Model/AdditionalAwards/ActivityMonitor.cs` records when the leader last did something (navigated, or a page request ran) and whether background work is holding a Scout profile.
- `Model/AdditionalAwards/CheckPacing.cs` (`MonitorPacing`) makes background work wait for a quiet moment before each step and stop at once if a page needs Terrain. A page's own request always goes first.
- `Topo/Services/AutoAwardService.cs` runs a check about once a day while Topo is open and only in those quiet gaps. It stores its settings under the local-storage key `TopoAutoAwards:{groupId}` and its results under `TopoAwardScan:{groupId}`.
- `Topo/Services/AdditionalAwardCheckService.cs` reads each Scout's logbook, one at a time, and works out Camper and Walkabout awards Terrain has no record of. The Approvals page lists them.
- `Topo/Pages/Settings.razor` is where it is switched on or off.

Nothing runs while the browser tab is closed. There is no server to run it.

## 9. What is cached today

There is no general cache yet. What exists is a handful of separate ones:

| What | Where | Lifetime |
|---|---|---|
| Members per unit | `StorageService.CachedMembers`, filled by `MembersService.GetMembersAsync` | the session; cleared by logout or the Patrol Planner's refresh (`ClearMemberCache`) |
| Wallchart results | `StorageService.CachedWallchartItems` | the session |
| OAS templates and stages, the award list | `StorageService` lists | the session |
| The sign-in | local storage `TopoSession` | until logout |
| Gender notes per unit (Terrain has no gender field) | local storage `{unitId}_MemberGenders` | permanent |
| Awards check settings and results | local storage `TopoAutoAwards:` and `TopoAwardScan:` | permanent |
| Events already read for the look-back | `AttendanceService` dictionaries | the session |
| The calendars the user had switched on | `ProgramService`, so they can be put back | while a read runs |

"The session" means until the page is reloaded. Anything in a C# field is gone on refresh; only local storage survives. The proposed general cache is in [background-cache-design.md](background-cache-design.md) (and upstream issue #306).

## 10. Tests

- Framework: **MSTest**, in `TopoReportFunctionTestUnix/` (`TopoReportFunctionTest/` is an older project of the same kind). About 540 tests; run them with `dotnet test TopoReportFunctionTestUnix`.
- They can reach `Model` and the report-building code, but **not** the Blazor project. That is the reason logic is pushed into `Model`: if it were in a controller it could not be tested.
- Two kinds of test: **rules** (Model classes called directly: layouts, filters, ordering, date rules) and **report tests** that build a workbook through `Services/ReportService` and check cells. The report tests call `SetEnvironment.SetSyncfusionLicense()`, which sets the `SyncfusionLicense` environment variable (to blank in the tests, so they run without a key); they check cell contents, not the look of a finished file.
- A test file is named after the thing it covers (`MilestoneLayout.cs`, `Wallchart.cs`, `ReadsAtOnceTests.cs`).

## 11. Running, building and deploying

### Run it locally

- **The app alone:** `dotnet run --project Topo/Topo.csproj --urls http://localhost:5280`. Reports will go to the hard-coded AWS Lambda.
- **App plus reports on one machine:** `dotnet run --project TopoReportServer`. This serves the pages and answers report requests by calling the real `FunctionHandler`, with no AWS. See [docs/report-server.md](report-server.md). Set the `SyncfusionLicense` environment variable so reports have no trial watermark. A `Release` build is needed for the pages to have the licence key (a `Debug` build registers an empty key).

### How it reaches users

`.github/workflows/main.yml` runs on every push to `main`:

1. install .NET 10;
2. replace `__SYNCFUSION-KEY__` in `Topo/Program.cs` with the secret key;
3. `dotnet publish` the Blazor app in `Release`;
4. change the `<base href>` in `index.html` to `/Topo-Blazor/` (GitHub Pages serves it from a sub-folder) and copy it to `404.html` so deep links work;
5. push the result to the `gh-pages` branch, which is the live site.

Releases are cut as "develop into main" pull requests titled `Version YYYY.MM.DD`; `StorageService.Version` holds the number shown in the menu.

The **Lambda is deployed to AWS separately**; there is no workflow for it in this repository. The pages and the Lambda are two different deployments, so a change that touches both (a new report, a changed column) needs both released.

## 12. Gotchas and Terrain quirks

- **One assumed profile at a time.** Assuming a Scout replaces the previous one (tested), and a logbook cannot be read with nothing assumed (403). So per-Scout reads are one at a time, and the profile must be revoked afterwards.
- **Calendars are global account state.** Reading events switches the account's selected calendars and restores them. Hence the lock in `ProgramService`.
- **Empty can mean failed.** `SendRequest` swallows errors (section 5). If a page is empty, look at the log. Turn logging on in the Logging page (menu → Settings), reproduce the problem, and download the log.
- **Youth roles are youth.** Unit Leader, Patrol Leader and Assistant Patrol Leader are held by youth members, not adults. Adults are the members with `isAdultLeader == 1`; use that to split youth from adults (see `.claude/CLAUDE.md`).
- **Terrain's JSON is `snake_case`**, so model properties in `Model` copy it (`member_id`, `date_awarded`). Don't "fix" the names or deserialising breaks.
- **Dates are local days.** Terrain gives some dates as UTC timestamps; `Model/AwardDate.cs` and `Model/ReportDate.cs` hold the rules for turning them into the day a leader expects.
- **`Debug` and `Release` differ.** The Lambda address and the Syncfusion licence both change with the build type.
- **Nothing secret is in the repo.** The licence key comes from CI, the Lambda addresses are function URLs, and the four Cognito client ids in `TerrainAPIService` are app ids that Terrain's own web app uses (they identify the app, not a user).

## 13. Where to look when you want to change something

| You want to... | Start here |
|---|---|
| Add or reorder a menu entry | `Topo/Shared/NavMenuItems.cs` |
| Change what a page shows | `Topo/Pages/<Page>.razor`, then `Topo/Shared/<Page>Preview.razor` |
| Change what a page does on a click | `Topo/Controller/<Page>Controller.cs` |
| Change what is read from Terrain for a page | `Topo/Services/<Feature>Service.cs` |
| Add a new Terrain call | `ITerrainAPIService` and `TerrainAPIService.cs`, plus the answer class in `Model/<Feature>/` |
| Change how rows are grouped or coloured in the preview | `Model/<Feature>/<Feature>Layout.cs` |
| Change a downloaded report | `Services/ReportService.cs` (then redeploy the Lambda); the data class in `Model/<Feature>/` |
| Add a report type | `ReportType`, `Topo/Services/ReportService.cs`, `Function.cs`, `Services/ReportService.cs` |
| Change sign-in or who counts as a leader | `Topo/Services/LoginService.cs`, `CompleteLoginSetupAsync` |
| Change who sees a page | `NavAudience` in `NavMenuItems.cs` and the page's `IsAuthenticated`/`IsYouthMember` checks |
| Change the spinner | `DisplaySpinnerAutomaticallyHttpMessageHandler.cs`, `SpinnerService.cs` |
| Change what is stored in the browser | `LoginService` (session), `MembersService` (genders), `AutoAwardService`, `AdditionalAwardCheckService` |
| Run reports locally | `TopoReportServer/` and `docs/report-server.md` |

## 14. A good reading order

If you want to read the code, this order builds understanding with the least confusion:

1. `Topo/Program.cs`, then `Topo/App.razor` and `Topo/Shared/MainLayout.razor` (how the app starts and what surrounds a page).
2. `Topo/Services/StorageService.cs` and `LoginService.cs` (what the app knows about you).
3. `Topo/Services/TerrainAPIService.cs`: read `GetMembersAsync`, `RefreshTokenAsync`, `SendRequest` and `GetUnitAchievementsOfType`, and skim the rest as a catalogue.
4. One simple feature end to end: `Topo/Pages/Milestone.razor` → `Topo/Controller/MilestoneController.cs` → `Topo/Services/MilestoneService.cs` → `Model/Milestone/MilestoneLayout.cs` → `Topo/Shared/MilestonePreview.razor`.
5. The report path: `Topo/Services/ReportService.cs` (`CallReportGeneratorFunction`) → `TopoReportFunction/Function.cs` (`FunctionHandler`) → one `Generate...Workbook` in `Services/ReportService.cs`.
6. A page that reads per Scout: `Topo/Services/LogbookService.cs` and `Topo/Controller/LogbookController.cs`, to see assume-profile, `ReadsAtOnce` and incremental display.
7. The tests for what you just read, for example `TopoReportFunctionTestUnix/MilestoneLayout.cs`.

Two exercises that make it click:

- Open the browser's developer tools (Network tab), load a page, and watch the calls: the host tells you which Terrain service answered, and the `units/.../achievements?type=...` calls are the unit-wide reads from section 5. Then open the Logbook page and watch the `assume-profiles` / `logbook-metrics` pairs go one Scout at a time.
- Put a breakpoint (or a temporary log line) in `SendRequest` and click through a page to see every Terrain call it makes, in order.
