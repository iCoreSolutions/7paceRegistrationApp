# 7PaceDesktop — Design Spec

Date: 2026-07-03

## Purpose

A Windows desktop app that lets a user quickly bulk-register work hours into
7Pace Timetracker (an Azure DevOps time-tracking extension), with minimal
interaction. It replaces a one-off PowerShell script (`Log-7PaceTime.ps1`)
that posted CSV rows to the 7Pace REST API.

Core behavior the user wants:
- Pick a date range and a "hours per day" value; the app generates one
  work-log entry per weekday in that range.
- Weekends and Swedish public holidays are skipped automatically.
- The workday immediately before a public holiday is automatically
  shortened by 3 hours (floored at 0).
- Multiple people at iCore may use this app; each person's work items,
  organization, and API token are their own and must be independently
  configurable — nothing about iCore's specific work items is hardcoded.

## Non-goals

- No duplicate-detection against time already logged in 7Pace. The user is
  expected to review the generated preview before submitting.
- No support for non-Windows platforms.
- No multi-tenant / shared-config features — this is a single-user local app,
  each installation manages its own settings.

## Tech stack

- **.NET 10**, WPF.
- MVVM via CommunityToolkit.Mvvm.
- Local per-user state stored under `%AppData%\7PaceDesktop\`:
  - `workitems.json` — list of configured work items.
  - `settings.json` — organization name, last-used daily-hours value,
    cached holiday data.
  - 7Pace API token stored in **Windows Credential Manager** (never written
    to a plain file).

## Data models

```csharp
record WorkItem(int Id, string Name, bool IsFavorite);
record Holiday(DateOnly Date, string Name);
record TimeEntry(DateOnly Date, double Hours, int WorkItemId);
```

## Components

- **First-run setup wizard** — blocks the main window until complete.
  Collects: 7Pace organization name, 7Pace API token, and at least one
  work item (Id + display name, marked as favorite). The app ships with
  **no pre-seeded work items** — every installation starts empty so that
  one person's work item IDs never leak into another's setup.
- **MainWindow** — date range picker, "hours per day" numeric input
  (defaults to last-used value), "Generate" button, preview grid,
  "Simulate" checkbox (dry-run, mirrors the old script's `-WhatIf`),
  "Register" button.
- **Work item management view** — reachable at any time after setup.
  Add/edit/remove work items, toggle which one is the favorite (used as
  the default for newly generated rows).
- **Settings dialog** — reachable at any time after setup. Update
  organization name and/or API token (re-triggers the same validation as
  first-run setup).

### Services

- `SwedishHolidayService` — fetches public holidays from the Nager.Date
  API (`https://date.nager.at/api/v3/publicholidays/{year}/SE`), caches the
  result per year in `settings.json`. On fetch failure, falls back to the
  last successfully cached year; if no cache exists, warns the user inline
  and treats the range as having no holidays (non-blocking).
- `TimeEntryGenerator` — pure business logic, no I/O. Given a date range,
  daily hours, holiday set, and default work item ID, produces the list of
  `TimeEntry` rows:
  - Skip Saturday/Sunday.
  - Skip dates present in the holiday set.
  - If `date + 1 day` is in the holiday set, subtract 3 hours from that
    day's entry, floored at 0. Flag the row (via a bool) when it hits the
    floor so the UI can highlight it.
- `PaceApiClient` — wraps `POST https://{org}.timetracker.7pace.com/api/rest/workLogs`
  for a single `TimeEntry`, given the organization name and token. Same
  request shape as the existing PowerShell script's `Invoke-RestMethod`
  call, translated to `HttpClient`. **Not yet verified against a live
  7Pace instance's Swagger/help page** — this should be confirmed during
  implementation before relying on this in production (see Open
  Questions).
- `CredentialStore` — thin wrapper around Windows Credential Manager
  (via the `CredentialManagement` NuGet package or direct P/Invoke) to
  save/load the 7Pace API token, keyed per organization name.
- `WorkItemStore` — reads/writes `workitems.json`.

## Data flow

1. App start → check for `settings.json` + a stored token + at least one
   work item. If any are missing, show the first-run wizard; it must
   complete successfully before the main window opens.
2. MainWindow loads: favorite work item as default, last-used daily-hours
   value.
3. User picks a date range + hours/day → clicks **Generate**.
4. `TimeEntryGenerator` produces the row list as described above.
5. Preview `DataGrid` shows: date, hours (editable), work item (dropdown
   populated from `WorkItemStore`, defaulting to the favorite), a
   per-row "remove" action, and a running total of hours. Rows that hit
   the 0-hour floor are visually flagged.
6. User optionally checks **Simulate** to dry-run (skips the actual POST,
   shows what would be sent) or clicks **Register** to submit.
7. On submit, each row is POSTed via `PaceApiClient` (mild concurrency,
   e.g. max 4 in flight) with a per-row status column (`Pending` →
   `OK` / `Fel: <message>`).
8. Failed rows can be resubmitted individually without regenerating the
   whole batch.

## Error handling

- **401/403 from 7Pace** → surface an inline error and open the Settings
  dialog so the user can update their token.
- **Nager.Date unreachable** → fall back to cache; if no cache, warn and
  proceed treating the range as holiday-free (does not block the user
  from logging time).
- **Row hits 0-hour floor** → visually flagged in the preview grid, not
  silently created.
- Network/timeout errors on individual POSTs are shown per-row and are
  independently retryable.

## Testing

- Unit tests (xUnit) for `TimeEntryGenerator`:
  - Plain weekday range with no holidays.
  - Range containing a holiday on a weekday (skipped) with the prior
    weekday correctly shortened by 3 hours.
  - Holiday falling on a Monday (the calendar day before is Sunday,
    which isn't logged anyway — verify no weekday entry is incorrectly
    shortened).
  - Multiple consecutive holidays.
  - Range crossing a year boundary (requires fetching holiday sets for
    both years).
  - Hours reduced below 0 by the shortening rule — verify floor at 0 and
    the flag is set.
- Manual verification before relying on real submissions: run **Simulate**
  against a small real date range, inspect the generated payloads, then
  submit a small real batch to 7Pace and confirm the entries appear
  correctly before broader use.

## Open questions / risks to confirm during implementation

- The exact 7Pace `workLogs` POST payload shape (field names like
  `timestamp`, `length`, `comment`) is carried over from the earlier
  PowerShell script as a best-effort guess and has **not** been verified
  against iCore's actual 7Pace instance API documentation
  (`https://<org>.timetracker.7pace.com/api/rest/help`). This must be
  confirmed (or corrected) early in implementation, ideally before
  building the rest of the app around it. Since the app sends no comment
  text (see Non-goals), confirm whether the `comment` field can be omitted
  entirely or must be sent as an empty string.
