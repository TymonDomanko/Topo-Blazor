# Data cache

Topo keeps what a page reads from Terrain, in the browser, so the page (and others that need the same records) opens quickly next time. Nothing is
read ahead. It is on by default and can be turned off on the Settings page.

## What a page does

Every page that reads records from Terrain reads them through the cache.

- A read made in the last day is used as it is.
- A page that shows what it reads (Members, Wallchart, Milestone, Milestone Progress, Ready to Submit, SIA Progress, Scouting History, a Scout's
  progress and so on) shows an older read straight away and reads it again behind the page, with a note saying it is updating to more recent data.
  The report buttons wait until that has finished.
- Pending Approvals and Mass awarding act on Terrain: they show what is held, always read it again, and nothing can be actioned until that is in.
  A submission's answers and who attended an event are read from Terrain each time.
- The Patrol Planner, which is edited on the page, and the pages that make something when asked (Logbook, Additional Awards, SIA, OAS Progress,
  Program, Program Builder, Sign In Sheet, Attendance, Unit History, Section Report, Badge Order) read old data again before using it.
- Anything that is about to change Terrain, and the background award checks, read Terrain as it is (`CacheScope.Outside`).
- Pages say when their kept data was read, with a Refresh data button (or their existing refresh button).
- Not read again for 30 days, an entry is dropped. Logging out removes everything.

## What is always read from Terrain

- The list of events, and which calendars are selected (see below). Program Ideas reads nothing else, so it always shows Terrain as it is.
- Signing in and your profile.

## Events

- The list of events is read from Terrain every time, switching the chosen calendars on for the read and putting the selection back.
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
