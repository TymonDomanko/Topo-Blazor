# Data cache

Topo keeps what a page reads from Terrain, in the browser, so the page (and others that need the same records) opens quickly next time. Nothing is
read ahead. It is on by default and can be turned off on the Settings page.

## What a page does

- A read made in the last day is used as it is.
- An older one is shown straight away and read again behind the page, with a note saying it is updating to more recent data. The report buttons
  wait until that has finished. The Patrol Planner, which is edited on the page, and the pages that read only when a report is made (Logbook,
  Additional Awards, SIA, OAS) read old data again before using it.
- Pages say when their kept data was read, with a Refresh data button (or their existing refresh button).
- Not read again for 30 days, an entry is dropped. Logging out removes everything.

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
