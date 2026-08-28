# Calendar-first redesign — design

Date: 2026-08-28
Status: approved for planning

## Problem

The app generates time entries from a date span and posts them, but it has no
idea what is already registered in 7Pace. The user therefore has to keep the
7Pace web UI open beside it to see the real picture, and a span that overlaps
already-logged days produces duplicate time. The single-`DataGrid` window also
shows only what the app intends to write, never what exists.

## Goals

1. Show the user's actual registered time, per day, inside the app.
2. Never propose hours that would duplicate time already logged.
3. Keep bulk registration of many days first-class — it is the reason the app
   exists.
4. Replace the grid with a month calendar as the primary surface.

## Non-goals

- Editing or deleting existing worklogs (read-only awareness only). `PATCH` and
  `DELETE /workLogs/{id}` stay unused.
- Resolving work item titles for items outside the user's configured list.
  Unknown items are shown by ID.
- Multi-user or team views. `/workLogs/all` is not used.
- Offline mode. Without a successful fetch the app does not register.

## Decisions

| Question | Decision |
| --- | --- |
| Primary surface | Month calendar, replacing the grid |
| Existing time | Read-only: fetched and displayed, never modified |
| Partially logged day | Top up to the daily target |
| Day selection | Drag on the calendar, plus week and bulk shortcuts |
| Work item choice | Side panel for the whole selection, with splits |
| Detail level | Hours per day plus work item IDs |
| Daily target | A setting, applied to every workday |
| Failed fetch | Explicit `Unknown` state that blocks registration |

## Architecture

Approach: a calendar UI over a pure Core planner. All merge and top-up logic is
WPF-free and unit-tested, matching how `TimeEntryGenerator` is tested today.

### Core (`PaceDesktop.Core`)

**`ExistingWorkLog`** — record: `Id` (string), `Date` (DateOnly), `Hours`
(double), `WorkItemId` (int), `Comment` (string?).

**`IWorkLogReader`** — `Task<IReadOnlyList<ExistingWorkLog>> GetWorkLogsAsync(DateOnly from, DateOnly to, CancellationToken ct)`.
Implemented by `PaceApiClient` alongside the existing `IWorkLogClient`.

**`WorkSchedule`** — expected hours for a date:

- 0 on Saturday and Sunday.
- 0 on a holiday.
- `DailyHours - 3` on the workday immediately before a holiday, floored at 0
  (`HitZeroFloor`), preserving the current rule.
- `DailyHours` otherwise.

Constructed from `DailyHours` and the holiday set. This absorbs the rules
currently inside `TimeEntryGenerator`.

**`DayPlan`** — record: `Date`, `Expected`, `Logged`, `Existing`
(`IReadOnlyList<ExistingWorkLog>`), `Status`, `HitZeroFloor`.

`DayStatus` is `NonWorking | Empty | Partial | Complete | Over | Unknown`:

- `NonWorking` when `Expected == 0`. This wins over `Unknown`: the schedule is
  known locally, so a weekend stays a weekend even when the fetch failed.
- `Unknown` when the fetch for the period failed and `Expected > 0`
- `Empty` when `Logged == 0`
- `Over` when `Logged > Expected`
- `Complete` when `Logged >= Expected`
- `Partial` otherwise

**`MonthPlan`** — pure merge of a date range, a `WorkSchedule` and a worklog
list into `DayPlan` per date. A separate factory produces an all-`Unknown`
plan when the fetch failed. Also exposes period totals (`ExpectedTotal`,
`LoggedTotal`, `MissingTotal`) for the status bar.

**`FillSpec`** — `IReadOnlyList<FillLine>` where `FillLine` is
`(int WorkItemId, double Hours)`, plus the target hours the lines must sum to.

**`FillPlanner`** — pure: `(IReadOnlySet<DateOnly> selection, MonthPlan plan, FillSpec spec) -> IReadOnlyList<TimeEntry>`.

Per selected date:

- `NonWorking` or `Unknown` → no entries.
- `remaining = Expected - Logged`; if `remaining <= 0` → no entries (the day is
  skipped, reported as such in the summary).
- Otherwise the fill lines are scaled by `remaining / spec.Target` and emitted
  as one `TimeEntry` per line. Scaling keeps the split's proportions on a
  partially logged day: a 6/2 split topping up 5 h emits 3.75 h and 1.25 h.
  `remaining` differs from `spec.Target` for two independent reasons — the day
  is partially logged, or the day's `Expected` is itself shorter than the target
  (a pre-holiday day). Both are handled by the same scaling.
- Rounding: each line is rounded to two decimals; any residual from rounding is
  added to the largest line so the day's entries sum exactly to `remaining`.

`TimeEntryGenerator` is deleted; its tests move to `WorkScheduleTests` and
`FillPlannerTests`.

### App (`PaceDesktop.App`)

**`CalendarViewModel`** — owns:

- `VisiblePeriod` (the first and last date of the displayed grid, including the
  leading and trailing days of adjacent months)
- `Days` (`ObservableCollection<DayCellViewModel>`, always 35 or 42 cells)
- `LoadState` (`Loading | Loaded | Failed`) and `LastFetchedAt`
- `Selection` (`ISet<DateOnly>`)
- commands: `PreviousMonth`, `NextMonth`, `Today`, `Refresh`,
  `SelectAllEmpty`, `ClearSelection`, `SelectWeek(int)`, `Register`

**`DayCellViewModel`** — one per grid cell: `Date`, `Expected`, `Logged`,
`Planned`, `Status`, `IsSelected`, `IsOutsideMonth`, `HolidayName`,
`WorkItemLabels`, `SubmitStatus`, `Error`.

**`SelectionPanelViewModel`** — `TargetHours`, `Lines`
(`ObservableCollection<FillLineViewModel>`), `LinesSum`, `IsBalanced`,
`Summary` (empty / partial / skipped day counts and the total), `Simulate`,
`CanRegister`.

`MainViewModel` and `EntryRowViewModel` are removed; `MainWindow.xaml` is
rewritten. `SetupWindow`, `WorkItemsWindow`, `ThemeService` and the theme
dictionaries are unchanged.

### Data flow

1. Period changes, or `Refresh` is invoked → `GetWorkLogsAsync` over the whole
   visible grid range.
2. Success → `MonthPlan` built → cells rendered. Failure → all-`Unknown` plan.
3. Selection changes, or the fill spec changes → `FillPlanner` recomputes; the
   planned hours land on the cells and in the panel summary.
4. `Register` → one `TimeEntry` POST per planned entry, at most four in flight,
   per-day status and retry as today.
5. When the batch finishes → refetch the period. Displayed state always comes
   from 7Pace, never from local optimism.

## API contract

`GET https://{account}.timehub.7pace.com/api/rest/workLogs?api-version=3.2`

Query parameters (documented):
`$fromTimestamp`, `$toTimestamp` (format `2021-11-06T10:28:00`), `$count`
(max 500), `$skip`. Authorization: `Bearer {token}`, the same token used for
`POST`. The endpoint is scoped to the token owner; `/workLogs/all` would be
org-wide and is not used.

Paging: request `$count=500`, increment `$skip` by 500 until a response
returns fewer than 500 rows.

**Unverified.** The response body's field names and nesting are not confirmed
against a live instance — only the endpoint and its query parameters are
documented. The client must be written against a small parsed shape
(`id`, `timeStamp`, `length` in seconds, `workItemId`, `comment`), pinned by a
stubbed-handler test, and confirmed with one live call before release. This is
the same situation the `POST` contract was in, and it was wrong the first time
(`timetracker` vs `timehub`, `timestamp` vs `timeStamp`), so the live check is
mandatory rather than advisory.

**Timezone.** `POST` currently writes `yyyy-MM-ddTHH:mm:ss` at 09:00 with no
offset. Reads group worklogs by the date portion of the returned `timeStamp`
with no timezone conversion, which is symmetric with how the app writes. If the
live check shows the API returns UTC with an offset, this needs revisiting
before release — a day boundary error would misattribute early-morning or
late-evening entries.

## User interface

Window 1240 x 820 by default, minimum 1040 x 680. Existing Light/Dark palettes
and control styles are reused unchanged; new colours are limited to the day
status set below.

### Layout

- **Top bar** (52 px): app name, account name; right side shows the fetch time,
  `Uppdatera`, settings and theme buttons.
- **Month bar** (52 px): `‹ Juni 2026 ›`, `Idag`, a divider, `Alla tomma dagar`,
  `Rensa markering`; right side carries the status legend.
- **Calendar**: a week-number gutter (34 px) plus a 7-column grid, Monday first,
  5 or 6 rows depending on the month.
- **Side panel** (340 px): the selection panel.
- **Status bar** (44 px): period, `83 av 165 h loggade`, a progress bar, and
  `82 h saknas`.

### Day cell

Date number top left; a planned badge (`+8 h`, or `klar` for a skipped day) top
right; `logged / expected h` at the bottom; work item ID chips below that. A
3 px status stripe runs down the left edge.

Status colours, light theme: `Complete` #107C10, `Partial` #C77700, `Empty`
#B9B9B9, `Over` #7C5DBF, `NonWorking` no stripe on the `AppRowAlt` surface,
`Unknown` #605E5C on a hatched background. Dark-theme equivalents follow the
existing palette's approach (#6CCB5F, #FCE100, #6A6A6A, #B4A0FF). Selected
cells take a 1 px accent border with an accent tint.

### Selection

- Drag across cells to select a contiguous run; Ctrl-click toggles one day.
- Click a week number to select that week.
- `Alla tomma dagar` selects every workday in the visible month whose status is
  `Empty`.
- `Rensa markering` clears the selection.
- Keyboard: arrows move focus, Space toggles the focused day, Shift-arrow
  extends, Ctrl-A selects the month's workdays.
- Selecting `NonWorking` days is allowed but they contribute nothing; this
  keeps drag behaviour predictable across weekends.

### Side panel

Header with the day count and date range. `Mål per dag` shows the target from
settings with a note that holidays and pre-holiday days are shortened
automatically. `Fördelning per dag` lists fill lines, each a work item picker
plus an hours box plus a remove button, with `Lägg till work item` and a sum
indicator that must reach the target before registering. `Fylls upp till målet`
summarises empty days, partial days, skipped days and the total. The footer has
the `Simulera` checkbox and the primary `Registrera N h` button.

When the period is `Unknown` the panel shows a blocking banner explaining that
registered time could not be fetched and that registering could double-log, the
totals read `—`, and `Registrera` is disabled.

## Behaviour rules

1. **Top-up.** A selected day is filled to `Expected - Logged`. A day already at
   or above target is skipped and reported as skipped.
2. **Expected hours** come from `DailyHours` in settings, with the existing
   weekend, holiday and pre-holiday rules applied on top.
3. **Unknown blocks registration.** A failed fetch does not mean zero. Every day
   in the period becomes `Unknown` and registration is disabled until a refresh
   succeeds.
4. **Staleness.** If the data is older than five minutes when `Registrera` is
   pressed, the app refetches first and recomputes the plan. If that refetch
   fails, registration is blocked rather than proceeding on old data.
5. **Refetch after submit.** The period is always refetched once the batch
   completes, so a day that landed despite an error surface corrects itself.

## Error handling

- **Fetch failure**, including a failure on any page of a paged read: the whole
  period becomes `Unknown`. A partial result is never displayed as fact.
- **Per-day submit failure**: the day keeps a `Failed` status and its message;
  other days continue; retry is per day.
- **401**: surfaced on the affected days with a prompt to open settings, as
  today.
- **Holiday service offline**: existing fallback — all weekdays are treated as
  ordinary workdays and a warning banner appears. Combined with real logged
  data this can only propose too much, never duplicate.

## Settings and storage

`AppSettings.LastDailyHours` becomes `AppSettings.DailyHours` (same JSON
default of 8), now a persistent target rather than a remembered last input.
Migration: read the old property name if present, on first load. `HolidayCache`
and `Theme` are unchanged. `workitems.json` is unchanged.

## Testing

Unit tests, no WPF:

- `WorkScheduleTests` — weekends, holidays, the pre-holiday 3 h reduction, the
  zero floor.
- `MonthPlanTests` — status classification for each of the six states, period
  totals, the all-`Unknown` factory.
- `FillPlannerTests` — top-up arithmetic, days at or over target skipped,
  proportional splitting on a partial day, rounding residual placement,
  `NonWorking` and `Unknown` days excluded.
- `PaceApiClientTests` — a stubbed `HttpMessageHandler` pinning the GET URL and
  query parameters, the paging loop across a 500-row boundary, and the parsed
  shape.
- `CalendarViewModelTests` — selection commands, `Unknown` disabling
  registration, staleness refetch, refetch after submit.

Manual verification before release: one live GET against the iCore instance to
confirm the response field names and the timestamp's timezone, and a
`Simulera` run over a month with known logged time.

## Assumptions

- The 7Pace GET response uses the field names listed above. Unverified; see the
  API contract section.
- Worklog timestamps can be grouped by their date portion without timezone
  conversion. Unverified.
- Nager.Date returns Midsommarafton among Swedish holidays, which is what makes
  the day before it shorten. If it does not, the pre-holiday rule simply does
  not fire for that date; no code change is implied.

## Out of scope

Editing and deleting worklogs, work item title resolution, team views, a week
or day zoom level, and comments on worklogs.
