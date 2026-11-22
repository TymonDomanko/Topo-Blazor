# Background data cache: design

Status: proposal, nothing built. Written 2026-11-22 from measurements of the live app (Scout Unit, 39 Scouts).

## Goal

Most pages wait on Terrain for tens of seconds. Instead, Topo reads what pages need in the background while it is open, keeps it for the day, and shows pages from that store. Each page has a **Refresh data** button that re-reads only the data that page needs.

Decisions already made:

1. Do as many Terrain calls as possible in the background and cache them for the day.
2. Pages with a time period load **6 months back and 6 months forward** by default, and load more from the page on request.
3. The background queue runs **longest first**, but when a page is opened whose data is not cached yet, that page's data jumps to the front.
4. Events that ended more than 4 months ago are cached **until a Refresh data button forces an update**.
5. A page's Refresh data button refreshes only the data that page needs.

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

## Queue and priority

One queue, three lanes (profile, unit, calendar), each lane running its jobs in order.

1. **Page first.** When a page is opened and anything it needs is missing or past its lifetime, those reads go to the front of their lane. The page shows what it has and fills in as reads finish (the Logbook page already works this way).
2. **Then longest first.** Everything else is ordered by its measured cost, longest first: Logbook entries, Wallchart reads, Unit History/Section report events, per-Scout totals, event windows, approvals, record reads, then the instant ones.
3. **Stay out of the way.** The background work reuses what the automatic Camper/Walkabout check already does: it only starts in a gap between the leader's own requests (`ActivityMonitor`, `MonitorPacing`), and it hands the Scout profile back at once if a page needs Terrain.
4. **Refresh data** puts only that page's keys at the front and ignores their lifetimes, including old events.

## Storing it

- Entries: key, data, fetched-at time, lifetime class (day or until-refresh), and a schema version so a code change can drop old entries.
- Keyed by group and signed-in user, so two accounts in one browser do not mix.
- Size: the Scout Unit's logbook entries are about 460 entries and one unit's event history about 780 events. That is likely a few MB across a group, which is over the 5 MB limit of local storage (`Blazored.LocalStorage`, what Topo uses now). Proposal: use the browser's IndexedDB through a small wrapper, and ask the browser to keep it (`navigator.storage.persist()`).
- A memory copy in front of the stored copy, as `StorageService` has today.

## What is shown and when it refreshes

- A cached page shows instantly. Each page says when its data was last updated, next to the Refresh data button.
- **Stale while refreshing:** data past its lifetime is still shown straight away, marked stale, and refreshed in the background; the page updates when the new data arrives.
- Topo's own writes (Mass awarding, patrol and program pushes) drop the keys they change.
- Logging out clears the whole cache. It holds young people's names, attendance and logbooks, so it should not stay behind on a shared computer.

## If you do not visit the site

Topo is a web page. **Nothing runs and nothing refreshes while it is closed.** The cache just gets older. Behaviour (1 day and 30 days confirmed 2026-11-22):

| Data | While you are away | First visit afterwards |
|---|---|---|
| Recent and upcoming data (1 day lifetime) | kept in the browser, not deleted | shown at once and marked stale if more than a day old; refreshed in the background; any page you open jumps the queue |
| Same, after 30 days away | dropped on the next visit | read again from scratch; first visit costs the page's full time (the 4-6s to 30s above) |
| Events older than 4 months | kept with no expiry until Refresh data | shown as stored; never refreshed unless forced |
| Whole cache | cleared on logout, or if the browser clears site data | read again from scratch |

The 30 day cap on recent data is there because the cache holds personal data about young people. Events older than 4 months have no cap, as decided, so an old event whose attendance is changed later in Terrain shows the old values until Refresh data is pressed.

If the browser is closed for good, a first visit on a new device or after clearing site data starts from nothing: the default windows take about 5-30s per page, and the full event history is about 85s per unit calendar if someone loads all of it.

## Open questions and risks

- **Two tabs.** Two Topo tabs both running background reads would fight over the one assumed profile and the account's calendar selection. Proposal: one tab at a time holds a lock (a stored heartbeat); other tabs only read the cache.
- **Per-Scout records in parallel.** The code suggests the per-Scout record reads (milestones, OAS, SIA, intro, peak awards) do not need an assumed profile, so several Scouts could be read at once. That has not been tested. If true it takes Personal Progress and Scouting History for a whole unit from a minute or more to a few seconds.
- **Rate limits.** Terrain's limits are not known. The queue starts with 6 requests at once, the same as the reads that exist today, and backs off on errors.
- **Long runs and sign-in.** A background run that outlasts the sign-in token must refresh it (the services already refresh before each call) and stop cleanly if sign-in has expired.
- **Latency varies.** Event reads ranged 0.09s to 0.85s each across runs. Times above are for planning, not promises.
- **Other units.** Only the Scout calendar's events were measured; a group has four.

## Build order

1. Cache store and keys, with the lifetime classes and the stale-while-refreshing rule, in front of one read that is already slow and shared: per-Scout logbook totals (Additional Awards, Wallchart, Logbook).
2. Refresh data button and "last updated" on those three pages.
3. Queue with the three lanes and page-first priority, built on `ActivityMonitor`/`MonitorPacing`.
4. Unit records by type, members, approvals, templates (Wallchart, Ready to Submit, SIA, Milestone, OAS, Approvals).
5. Events with the age rule and 6 month default window, then "load more" (Attendance, Program, Sign In Sheet, Section report, Patrol Planner, Program Builder, Program Ideas, Unit History).
6. Per-Scout records and logbook entries (Personal Progress, Scouting History, Logbook), including testing several Scouts at once.
7. IndexedDB store, `navigator.storage.persist()`, the multi-tab lock, clear on logout.
