# Data cache

Topo keeps what a page reads from Terrain, in the browser, so the page (and others that need the same records) opens quickly next time. Nothing is
read ahead. It is on by default and can be turned off on the Settings page.

## What a page does

Every page that reads from Terrain reads through the cache.

- A read made in the last day is used as it is.
- A page that shows what it reads (Members, Wallchart, Milestone, Ready to Submit, SIA Progress, Scouting History, a Scout's progress, Program,
  Sign In Sheet, Program Ideas and so on) shows what is held straight away. Anything older than a day, and every
  list of events, is then read from Terrain behind the page, with a note saying it is updating to more recent data. The report buttons wait until
  that has finished.
- The Patrol Planner, which is edited on the page, and the pages that make a report when asked (Logbook, Additional Awards, SIA, OAS, Unit
  History, Section Report) read old data again before using it. Making the history or section report from what is held takes about as long as
  reading it, so showing the held one first would only double the wait.
- Pages say when their kept data was read, with a Refresh data button (or their existing refresh button).
- Not read again for 30 days, an entry is dropped. Logging out removes everything.

## What is always read from Terrain

- Which calendars are selected, and the list of them: Topo needs what is selected now to put it back after a read.
- Signing in and your profile.

## Events

- A list of events is kept, but only so that it can show at once: it is read from Terrain every time it is shown, switching the chosen calendars
  on for the read and putting the selection back. If Terrain does not answer, the kept list stays.
- An event's details are kept. When its entry in the list is new or has changed, they are read again. A change the list does not show (who came,
  for example) is picked up after a day, or with Refresh data.
- Events that ended more than four months ago are kept until Refresh data is pressed.

## Writing to Terrain

Every request goes through `CacheInvalidationHandler`, which drops what a write may have made wrong before the writer carries on
(`Model/Cache/WriteEffects.cs`). A write it does not know drops everything, so a new kind of write is safe; add a rule for it to keep more.

## In the code

- A page asks for caching by reading inside a `CacheScope` (`Held`, `Fresh` or `Reread`). Reads outside a scope go straight to Terrain.
- `PageLoad` is the show-then-refresh flow for a page that shows data; `ReportRefresh` serves the pages that read only for a report.
- `DataCache` holds the rules; `IndexedDbCacheStore` and `wwwroot/js/dataCache.js` keep the entries in IndexedDB.
- Terrain allows one assumed Scout profile and one calendar selection at a time. `TerrainLocks` holds both, within a tab and across tabs.
