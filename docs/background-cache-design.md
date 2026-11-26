# Data cache: design

Status: proposal, nothing built. Written 2026-11-22 from measurements of the live app (Scout Unit, 39 Scouts). Revised 2026-11-26: after discussion with the maintainer, Topo **caches as you go** instead of reading everything ahead.

## Goal

Most pages wait on Terrain for tens of seconds, every time they are opened. Instead, Topo keeps what a page reads, for the day, and shows the page from that copy the next time it (or another page that needs the same data) is opened. Each page has a **Refresh data** button that re-reads only the data that page needs.

Nothing is read ahead. The first visit to a page takes as long as it does today; later visits are quick.

Decisions:

1. **Cache as you go.** Only what the pages you open ask for is read and kept. There is no background queue that reads data ahead (agreed 2026-11-26, replacing the earlier plan to read as much as possible in the background, longest first).
2. **Lifetimes:** recent data is kept for 1 day; events that ended more than 4 months ago are kept **until a Refresh data button forces an update**; the cache is cleared on logout (confirmed 2026-11-26).
3. **Out-of-date data** is shown straight away and refreshed behind it. The page updates once the new data has loaded, with a note saying it is updating to more recent data (2026-11-26).
4. **The background checks stay** as background tasks: the Camper and Walkabout awards check and the Years of Service check work out what is waiting to be handed over, which is separate from this cache (2026-11-26).
5. Pages with a time period load **6 months back and 6 months forward** by default, and load more from the page on request.
6. A page's Refresh data button refreshes only the data that page needs.
7. The cache is stored in **IndexedDB** (2026-11-26).
8. The cache is **on by default and configurable** in Settings (2026-11-26).
9. **Reports** made from cached data are fine, except while a page is still updating and showing the old data (2026-11-26): the report buttons wait until the update has finished.

## What things cost (measured)

Scout Unit, 39 Scouts, wall time. Rough: Terrain's speed varies from run to run, and event reads in particular range from 0.09s to 0.85s each.

| Page | Time | Mostly |
|---|---|---|
| Logbook | 30s | 457 entry reads (six at a time) after 39 profile reads |
| Wallchart | 24s | 39 one-at-a-time totals (13s), unit metrics (5s), 9 record reads in parallel |
| Unit History (all time) | 20s | 14 event reads (0.85s each), 5 record reads |
| Section report (one unit, a quarter) | 20s | records, events |
| Additional Awards | 14s | 39 one-at-a-time totals (12s) |
| Program Builder, Continue from Terrain | 11.3s | 79 event reads |
| Patrol Planner | 10.8s | 54 event reads |
| Attendance (a quarter) | 9s | 40 event reads |
| Program Ideas (all time) | 8.1s | two event lists, 3.6s each |
| Approvals | 5.8s | two submissions reads (3.2s slowest) |
| Ready to Submit | 4.4s | 6 record reads in parallel (3.7s slowest) |
| Program (4 months) | 2.6s | 15 event reads |
| Personal Progress, one Scout | 2.3s | 9 record reads |
| Milestone 1.9s, Sign In Sheet 1.4s, OAS 1.3s, SIA 1.3s, SIA Progress ~1s, Members 0.4s | | |

Events by age (Scout calendar, event details read six at a time):

| Window | Time | Events |
|---|---|---|
| 6 months back to 6 months forward (default) | 4-6s | 35-45 |
| 4-12 months back | 14.9s | 129 |
| 1-2 years back | 16.5s | 169 |
| 2-3 years back | 14.7s | 151 |
| 3-4 years back | 12.9s | 132 |
| 4-5 years back | 9.5s | 93 |
| 5-6 years back | 6.2s | 50 |
| 6-7 years back | 3.3s | 12 |

The whole history is about 780 events and 85s for one unit calendar. About 78s of that is older than 4 months. Other units' calendars were not measured.

## What is cached: reads, not pages

Pages share the same reads, so the cache is keyed by the read. One read can serve several pages.

| Resource | Key | Used by | Lane | Lifetime |
|---|---|---|---|---|
| Unit members | unit | almost every page | unit | 1 day |
| Unit records by type (milestone, OAS, SIA, intro, peak awards, additional awards) | unit + type | Wallchart, Ready to Submit, SIA, Milestone, OAS, Section report, Approvals | unit | 1 day |
| Unit metrics | unit | Wallchart | unit | 1 day |
| Unit approvals (submissions) | unit + status | Approvals | unit | 1 day, and dropped after Topo itself approves or awards |
| Per-Scout logbook totals (nights, km) | member | Additional Awards, Wallchart, Logbook | profile | 1 day |
| Per-Scout logbook entries | member | Logbook, Scouting History | profile | 1 day |
| Per-Scout records (milestones, OAS, SIA, intro, peak awards, all achievements) | member | Personal Progress, Scouting History | member (see Open questions) | 1 day |
| Events, recent or upcoming (ended within the last 4 months, or not yet ended) | calendar + window | Attendance, Program, Sign In Sheet, Section report, Patrol Planner, Program Builder, Program Ideas | calendar | 1 day |
| Events that ended more than 4 months ago | calendar + window | same | calendar | **until Refresh data** |
| Templates, award list, calendars | name | OAS, SIA Progress, Approvals, all | none (tiny) | 7 days, or Refresh data |

Why these lanes:

- **Profile lane**: Terrain keeps only one assumed profile at a time, so logbook reads for different Scouts must go one Scout at a time. This is the biggest cost: about 0.3s per Scout for totals, plus the entries.
- **Unit lane**: unit-wide reads do not need a profile and can run alongside the profile lane (the Wallchart already overlaps them) and in parallel with each other, up to about 6 at once.
- **Calendar lane**: reading events switches the account's selected calendars and puts them back (6 to 10 calendar calls each time), so event reads for different calendars take turns. The event detail reads inside one read can overlap, six at a time.

## Events

- An event is "old" when its end date is more than 4 months in the past when it is read. Old events are stored with no expiry; they are replaced only by Refresh data.
- Recent and upcoming events are stored with the 1 day lifetime.
- An event that was recent when stored does not change class by itself. It is stored as recent, expires after a day, and is stored as old when it is next read after the 4 months have passed.
- The window is kept as a set of month blocks. The default is 6 months back and 6 months forward. "Load more" on a page adds the previous 6 months and reads only the blocks not already stored. Reading older years costs about 15s a year of history for the Scout calendar, one time.
- Refresh data on an events page re-reads the window that page is showing, including its old events (that is what forces them).

## Reading

A page asks the cache for what it needs.

1. **Held and in date:** shown at once, nothing is read.
2. **Held but past its lifetime:** shown at once with the updating note, and re-read behind it. The page updates when the new data arrives.
3. **Not held:** read from Terrain as today, filling the page as reads finish (the Logbook page already works this way), and kept.
4. **Refresh data** re-reads that page's keys whatever their age, including old events.

Nothing else is read. The lanes above still decide which of a page's reads can overlap. The background checks keep working as they do now: they only start in a gap between the leader's own requests (`ActivityMonitor`, `MonitorPacing`) and hand the Scout profile back at once if a page needs Terrain.

## Storing it

- Entries: key, data, fetched-at time, lifetime class (day or until-refresh), and a schema version so a code change can drop old entries.
- Keyed by group and signed-in user, so two accounts in one browser do not mix.
- Size: the Scout Unit's logbook entries are about 460 entries and one unit's event history about 780 events. That is likely a few MB across a group, which is over the 5 MB limit of local storage (`Blazored.LocalStorage`, what Topo uses now). So the cache uses the browser's IndexedDB through a small wrapper, and asks the browser to keep it (`navigator.storage.persist()`).
- A memory copy in front of the stored copy, as `StorageService` has today.

## What is shown and when it refreshes

- A cached page shows instantly. Each page says when its data was last updated, next to the Refresh data button.
  - **Data-age line (from the UI review, 2026-11-24):** every page that reads Terrain shows "Data read 9:42am today" (or "yesterday 4:10pm", "3 days ago") with the **Refresh data** button beside it, at the top of the page where the Select Unit box is. Without it, once pages stop reading Terrain on every visit, nobody can tell whether the list in front of them is current.
  - The time shown is the **oldest** of the reads the page used, so the line never overstates how fresh it is. Past its lifetime it reads "Data read 3 days ago. Updating to more recent data…" while the refresh runs, and the page changes to the new data once it has loaded.
  - While a read is in progress the same line shows a count ("Reading 12 of 39 Scouts") instead, so the long pages (Logbook ~30s, Wallchart ~24s) also give progress.
  - The line is one shared component fed by the cache, not something each page writes for itself, so a new page gets it by listing the keys it needs.
- **Stale while refreshing:** data past its lifetime is still shown straight away, with the note that it is updating to more recent data, and re-read behind it; the page updates when the new data arrives. If the re-read fails, the older data stays on screen and the line says when it was read.
- **Reports:** a report is made from the data the page holds. While the page is still updating and showing old data, its report buttons wait for the update to finish, so a report is not printed from data that is about to be replaced.
- **Setting:** the cache is on unless the leader changes it in Settings.
- Topo's own writes (Mass awarding, patrol and program pushes) drop the keys they change.
- Logging out clears the whole cache. It holds young people's names, attendance and logbooks, so it should not stay behind on a shared computer.

## If you do not visit the site

Topo is a web page. **Nothing runs and nothing refreshes while it is closed.** The cache just gets older. Behaviour (1 day and 30 days confirmed 2026-11-22):

| Data | While you are away | First visit afterwards |
|---|---|---|
| Recent and upcoming data (1 day lifetime) | kept in the browser, not deleted | shown at once if more than a day old, with the updating note, and re-read behind it when you open a page that uses it |
| Same, after 30 days away | dropped on the next visit | read again from scratch; first visit costs the page's full time (the 4-6s to 30s above) |
| Events older than 4 months | kept with no expiry until Refresh data | shown as stored; never refreshed unless forced |
| Whole cache | cleared on logout, or if the browser clears site data | read again from scratch |

The 30 day cap on recent data is there because the cache holds personal data about young people. Events older than 4 months have no cap, as decided, so an old event whose attendance is changed later in Terrain shows the old values until Refresh data is pressed.

If the browser is closed for good, a first visit on a new device or after clearing site data starts from nothing: the default windows take about 5-30s per page, and the full event history is about 85s per unit calendar if someone loads all of it.

## How much the pages overlap (measured)

Each of the 20 pages was loaded once on the same unit (39 Scouts) with its default settings, and every Terrain request was recorded. A read counts as the same when the whole URL is the same. Assume/revoke profile calls and the calendar calls are left out of the data figures (see "Calls that can be removed").

| Page | Data reads | Also read by another page |
|---|---|---|
| Members, SIA, Milestone, Ready to Submit, Additional Awards, Unit History | 1-42 | 100% |
| Attendance, Scouting History (one person), Wallchart | 46-55 | 96% |
| Section report | 27 | 93% |
| Patrol Planner | 61 | 92% |
| Program, Sign In Sheet | 16-17 | 88% |
| Personal Progress (one person), Approvals | 5-10 | 60% |
| SIA Progress | 4 | 50% |
| Program Builder (Continue from Terrain) | 86 | 40% |
| OAS | 3 | 33% |
| Logbook | 536 | 16% |
| Program Ideas | 2 | 0% (its two event lists cover all time) |

Across all 20 pages: 995 data reads, of which only 674 are different, so 32% of reads are repeats of a read another page makes. By call time it is 255s down to about 155s (39% less). Logbook is more than half of all reads (457 entry reads that only it and the Scouting History use), so excluding it the repeat share is much higher.

By kind of read (reads across the 20 pages, how many are different, how many pages make it):

| Read | Total | Different | Pages | Call time |
|---|---|---|---|---|
| Event details (by event id) | 230 | 86 | 7 | 110s |
| Logbook entries | 502 | 457 | 2 | 48s |
| Event lists (by window) | 26 | 20 | 8 | 24s |
| Unit OAS records | 5 | 2 | 5 | 14s |
| Per-Scout logbook totals | 118 | 39 | 4 | 10s |
| Unit SIA records | 6 | 1 | 6 | 8s |
| Unit members | 15 | 2 | 14 | 7s |
| Unit milestone records | 5 | 1 | 5 | 7s |
| Everything else (other record types, award list, per-Scout lists) | | | | under 15s |

## Calls that can be removed

Caching by read removes the repeats above. These need no cache at all, or make it cheaper:

1. **Read an event once, whatever window asks for it.** Event details are keyed by event id, not by window: 230 reads become 86 (63% fewer, about 70s of call time). Patrol Planner alone reads 27 events twice in one page load; Sign In Sheet reads 8 twice and Program 1.
2. **Read per-Scout logbook totals once.** Four pages each read all 39 Scouts' totals (118 reads for 39 different), and each read needs an assume and a revoke around it: 119 assume and 124 revoke calls (about 24s) across the pages. One cached read per Scout per day replaces all of it.
3. **Skip the revoke between Scouts.** The code revokes the assumed profile before assuming the next Scout. Tested 2026-11-22: assuming a second Scout without revoking replaces the first (the first Scout's logbook then answers 403, the second's 200), and a logbook cannot be read with nothing assumed (403). So only one revoke at the end of a pass is needed: about 40 fewer calls per full pass of a unit, and 124 fewer across the 20 measured pages. It also confirms that two profiles cannot be assumed at once.
4. **Read the logbook in two steps.** Collapsed rows need only totals, the entry count and the latest date. The entry list already gives the count and dates; the entry details (457 reads) are only needed when a row is opened. Reading details only when needed takes the first paint of the Logbook page from about 30s to about 15s (an estimate: the per-Scout totals take about 12s and the entry lists about 3s).
5. **Align event list windows to month blocks** so Attendance, Program, Sign In Sheet, Section report and Patrol Planner share lists instead of each asking for its own dates (20 different lists for 26 reads today).
6. **One members read per unit** (15 reads, 14 pages, 2 different) and one read per unit and record type (SIA is read by 6 pages, OAS and milestone by 5).
7. **Read the award list once.** It is a static file read by 5 pages.

Across all 20 pages that is 1289 requests today, 294 of them housekeeping (119 assume, 124 revoke, 51 calendars). A cold load of every page with the cache would be about 674 data reads plus about 80 profile switches plus the calendar calls: roughly 35-40% fewer requests. This is for one unit on one day with default settings, and the calendar and profile figures are estimates.

## Open questions and risks

- **Two tabs.** Two Topo tabs reading at once (a page in one, a background check in the other) would fight over the one assumed profile and the account's calendar selection. Proposal: one tab at a time holds a lock (a stored heartbeat) for profile and calendar reads.
- **Per-Scout records in parallel.** The code suggests the per-Scout record reads (milestones, OAS, SIA, intro, peak awards) do not need an assumed profile, so several Scouts could be read at once. That has not been tested. If true it takes Personal Progress and Scouting History for a whole unit from a minute or more to a few seconds.
- **Rate limits.** Terrain's limits are not known. Caching as you go asks no more of Terrain than the pages do today (and less on repeat visits); a page still reads at most 6 requests at once.
- **Long reads and sign-in.** A refresh that outlasts the sign-in token must refresh it (the services already refresh before each call) and stop cleanly if sign-in has expired.
- **Latency varies.** Event reads ranged 0.09s to 0.85s each across runs. Times above are for planning, not promises.
- **Other units.** Only the Scout calendar's events were measured; a group has four.

## Build order

1. Cache store and keys, with the lifetime classes and the show-then-refresh rule, in front of one read that is already slow and shared: per-Scout logbook totals (Additional Awards, Wallchart, Logbook).
2. The data-age line and Refresh data button on those three pages.
3. Unit records by type, members, approvals, templates (Wallchart, Ready to Submit, SIA, Milestone, OAS, Approvals).
4. Events with the age rule and 6 month default window, then "load more" (Attendance, Program, Sign In Sheet, Section report, Patrol Planner, Program Builder, Program Ideas, Unit History).
5. Per-Scout records and logbook entries (Personal Progress, Scouting History, Logbook), including testing several Scouts at once.
6. IndexedDB store, `navigator.storage.persist()`, the multi-tab lock, clear on logout.
