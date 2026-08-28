# Calendar-first redesign — design

Date: 2026-08-28
Status: approved for planning
Revision: 2 — stack changed from WPF to a locally-run web app (ASP.NET Core + React)

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
5. Deliver a UI that is pleasant to look at and to use, which is what motivated
   moving off WPF.

## Non-goals

- Editing or deleting existing worklogs (read-only awareness only). `PATCH` and
  `DELETE /workLogs/{id}` stay unused.
- Resolving work item titles for items outside the user's configured list.
  Unknown items are shown by ID.
- Multi-user or team views. `/workLogs/all` is not used.
- Hosting. The app runs on the user's own machine and is reached at
  `http://127.0.0.1:<port>`. A hosted deployment would need a different
  credential story and is a separate spec.
- Offline mode. Without a successful fetch the app does not register.
- Cross-platform support. `CredentialStore` is Windows-only, so the server
  targets `net10.0-windows`.

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
| UI stack | React + TypeScript + Tailwind, served by a local ASP.NET Core process |
| Planning arithmetic | Stays in C# `Core`, never duplicated in TypeScript |

### Why a local server rather than a pure browser app

A browser-only app would have to keep the 7Pace API token in browser storage.
That token can write to the user's timesheet, and browser storage is a weaker
place for it than Windows Credential Manager, which the app already uses.
Separately, the 7Pace API is very unlikely to send CORS headers permitting a
`localhost` origin, which would block direct calls from the page. Either reason
alone forces a local process; together they settle it.

The local server therefore holds the token, talks to 7Pace, and serves the UI.
The token is never sent to the browser.

## Architecture

### Projects

| Project | Change | Responsibility |
| --- | --- | --- |
| `src/7PaceDesktop.Core` | Extended | Domain, planning, 7Pace client, holidays, storage, credentials. No UI, no HTTP hosting. |
| `src/7PaceDesktop.Server` | New | ASP.NET Core Minimal API plus static hosting of the built SPA. |
| `web/` | New | Vite + React + TypeScript + Tailwind front end. |
| `src/7PaceDesktop.App` | Deleted | The WPF app, removed once the web UI reaches parity. |
| `tests/7PaceDesktop.Tests` | Extended | Core unit tests plus server endpoint tests. |

`Core` keeps everything already built and verified: `PaceApiClient`,
`SwedishHolidayService`, `CredentialStore`, `SettingsStore`, `WorkItemStore`.
This is the main reason for choosing ASP.NET Core over a JavaScript backend —
the 7Pace client contract was wrong once already and had to be corrected against
a live instance, and rewriting it in another language would put that back at
risk.

### Core additions

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
list into a `DayPlan` per date, plus `TotalsForMonth(year, month)` returning
`PlanTotals(Expected, Logged, Missing)`. A separate factory produces an
all-`Unknown` plan when the fetch failed. The range covers the whole displayed
grid, including the leading and trailing days of adjacent months; totals are
restricted to the month itself.

**`FillSpec`** — `IReadOnlyList<FillLine>` where `FillLine` is
`(int WorkItemId, double Hours)`. `Target` is derived as the sum of the lines
rather than passed alongside them, so the two cannot disagree. The UI validates
that the sum equals the daily target before enabling registration.

**`ITokenSource`** — a seam in the server between the endpoints and Windows
Credential Manager, so endpoint tests read and write an in-memory token instead
of the developer's real credential store.

**`FillPlanner`** — pure:
`Plan(IReadOnlySet<DateOnly> selection, MonthPlan plan, FillSpec spec) -> IReadOnlyList<TimeEntry>`
and `Summarize(...) -> FillSummary(EmptyDays, PartialDays, SkippedDays, TotalHours)`.

Per selected date:

- `NonWorking` or `Unknown` → no entries.
- `remaining = Expected - Logged`; if `remaining <= 0` → no entries (the day is
  skipped, and counted as skipped in the summary).
- Otherwise the fill lines are scaled by `remaining / spec.Target` and emitted
  as one `TimeEntry` per line. Scaling keeps the split's proportions on a
  partially logged day: a 6/2 split topping up 5 h emits 3.75 h and 1.25 h.
  `remaining` differs from `spec.Target` for two independent reasons — the day
  is partially logged, or the day's `Expected` is itself shorter than the target
  (a pre-holiday day). Both are handled by the same scaling.
- Rounding: each line is rounded to two decimals; any residual is added to the
  largest line so the day's entries sum exactly to `remaining`.

`TimeEntryGenerator` is deleted with the WPF project.

### Server

`net10.0-windows`, ASP.NET Core Minimal API. Bound to `127.0.0.1` only. Serves
the built SPA from its own static web assets, and exposes:

| Endpoint | Purpose |
| --- | --- |
| `GET /api/config` | `{ configured, organization, dailyHours, theme, hasToken }`. Never returns the token. |
| `PUT /api/config` | Accepts `{ organization, token?, dailyHours, theme }`. A supplied token is written to Credential Manager; an omitted one leaves the stored token alone. |
| `GET /api/workitems` | The configured work items. |
| `PUT /api/workitems` | Replaces them, enforcing exactly one favourite and at least one item. |
| `GET /api/month?year=&month=` | The month payload: grid range, a day array, totals, `fetchedAt`, `loadState`, and any holiday warning. |
| `POST /api/register` | `{ dates[], lines[{workItemId, hours}], simulate }` → per-day results. |

The month endpoint owns the whole read path: holidays, `WorkSchedule`,
`GetWorkLogsAsync`, `MonthPlan`. A fetch failure returns `loadState: "failed"`
with an all-`Unknown` day array and an HTTP 200, because a failed fetch is a
displayable state rather than a server error.

The register endpoint builds the `FillSpec`, calls `FillPlanner.Plan`, and posts
each entry with at most four requests in flight, returning a per-day result of
`ok | failed` with the message. The client refetches the month afterwards.

**Planning arithmetic is never duplicated in TypeScript.** The front end
computes only `max(0, expected - logged)` per selected day and sums it for the
preview — both trivially equal to what the server will use. The split and
rounding rules live only in `FillPlanner`, so what is previewed as a day total
always matches what is posted. Consequently the panel shows the per-line
breakdown only for days at full target; a partially logged day shows its day
total.

### Security

The app binds to `127.0.0.1`, so nothing off the machine can reach it. To stop a
web page open in the same browser from posting to the local API, every mutating
endpoint requires the header `X-Pace-Client: 1` and no CORS policy is
configured. A custom header forces a preflight, and with no CORS policy the
browser refuses it, so cross-origin calls cannot reach the handlers. The SPA is
same-origin and sends the header on every request.

The token is written to Windows Credential Manager under the existing
`7PaceDesktop:{org}` key and is never included in any response body.

### Data flow

1. The month view mounts or the period changes → `GET /api/month`.
2. Success → day cells render from the payload. Failure → `loadState: "failed"`,
   all days `Unknown`, registration disabled.
3. Selection changes, or the fill spec changes → the preview total recomputes
   client-side from the day payload.
4. `Registrera` → `POST /api/register` → the server plans and posts.
5. On completion → the client refetches the month. Displayed state always comes
   from 7Pace, never from local optimism.

## API contract with 7Pace

`GET https://{account}.timehub.7pace.com/api/rest/workLogs?api-version=3.2`

Query parameters (documented):
`$fromTimestamp`, `$toTimestamp` (format `2021-11-06T10:28:00`), `$count`
(max 500), `$skip`. Authorization: `Bearer {token}`, the same token used for
`POST`. The endpoint is scoped to the token owner; `/workLogs/all` would be
org-wide and is not used.

Paging: request `$count=500`, increment `$skip` by 500 until a response returns
fewer than 500 rows.

**Unverified.** The response body's field names and nesting are not confirmed
against a live instance — only the endpoint and its query parameters are
documented. The client parses a tolerant shape (`id`, `timeStamp`, `length` in
seconds, `workItemId`, `comment`, matched case-insensitively, accepting a
`{data:{workLogs:[]}}`, `{data:[]}` or bare-array envelope), pinned by
stubbed-handler tests, and confirmed with one live call before release. The
`POST` contract was wrong on both host and field casing the first time, so the
live check is mandatory rather than advisory.

**Timezone.** `POST` writes `yyyy-MM-ddTHH:mm:ss` at 09:00 with no offset. Reads
group worklogs by the date portion of the returned `timeStamp` with no timezone
conversion, which is symmetric with how the app writes. If the live check shows
the API returns UTC with an offset, this needs revisiting before release — a day
boundary error would misattribute early-morning or late-evening entries.

Bounds are exclusive on both ends per the documentation, so the client sends
`from` at 00:00 and `to + 1 day` at 00:00, then filters the result to the
requested range client-side. Boundary semantics therefore cannot change what the
caller sees.

## User interface

React + TypeScript, Tailwind for styling, Vite for the build. Design tokens are
carried over from the WPF palette so the look is continuous with the app people
already use, expressed as CSS custom properties with a light and a dark set:

| Token | Light | Dark |
| --- | --- | --- |
| background | `#F3F3F3` | `#1F1F1F` |
| surface | `#FFFFFF` | `#2B2B2B` |
| foreground | `#1A1A1A` | `#F5F5F5` |
| subtle foreground | `#605E5C` | `#C8C8C8` |
| border | `#D6D6D6` | `#3D3D3D` |
| accent | `#0067C0` | `#60CDFF` |
| accent foreground | `#FFFFFF` | `#00243D` |
| row alt | `#F7F7F7` | `#262626` |
| status: complete | `#107C10` | `#6CCB5F` |
| status: partial | `#C77700` | `#FCE100` |
| status: empty | `#B9B9B9` | `#6A6A6A` |
| status: over | `#7C5DBF` | `#B4A0FF` |
| status: unknown | `#605E5C` | `#C8C8C8` |

Theme follows the system preference by default via `prefers-color-scheme`, with
an explicit light or dark override persisted in settings, matching the current
three-way choice.

### Layout

Reference mockups: the design canvas published on 2026-08-28 (artboards
`Månadsvy`, `Dagens tillstånd`, `Markeringspanelen`).

- **Top bar**: app name, account name; right side shows the fetch time,
  `Uppdatera`, settings and theme controls.
- **Month bar**: `‹ Juni 2026 ›`, `Idag`, a divider, `Alla tomma dagar`,
  `Rensa markering`; right side carries the status legend.
- **Calendar**: a week-number gutter plus a seven-column grid, Monday first,
  five or six rows depending on the month.
- **Side panel**: the selection panel.
- **Status bar**: period, `83 av 165 h loggade`, a progress bar, and
  `82 h saknas`.

### Day cell

Date number top left; a planned badge (`+8 h`, or `klar` for a skipped day) top
right; `logged / expected h` at the bottom; work item ID chips below that. A
status stripe runs down the left edge, coloured per the table above.
`NonWorking` days use the row-alt surface with no stripe and carry the holiday
name where there is one. `Unknown` days show `?` on a hatched background.
Selected cells take an accent ring with an accent tint.

### Selection

- Drag across cells to select a contiguous run; Ctrl-click toggles one day.
- Click a week number to select that week.
- `Alla tomma dagar` selects every workday in the visible month whose status is
  `Empty`.
- `Rensa markering` clears the selection.
- Keyboard: arrows move focus, Space toggles the focused day, Shift-arrow
  extends, Ctrl-A selects the month's workdays.
- Selecting `NonWorking` days is allowed but they contribute nothing, which
  keeps drag behaviour predictable across weekends.

### Side panel

Header with the day count and date range. `Mål per dag` shows the target from
settings with a note that holidays and pre-holiday days are shortened
automatically. `Fördelning per dag` lists fill lines, each a work item picker
plus an hours box plus a remove button, with `Lägg till work item` and a sum
indicator that must reach the target before registering. `Fylls upp till målet`
summarises empty days, partial days, skipped days and the total. The footer has
the `Simulera` checkbox and the primary `Registrera N h` button.

When the month's `loadState` is `failed` the panel shows a blocking banner
explaining that registered time could not be fetched and that registering could
double-log, the totals read `—`, and `Registrera` is disabled.

### Setup and settings

The WPF first-run wizard, work item manager and settings dialog become web
views with the same rules: the app is unusable until an organization, a token
and at least one work item exist; exactly one work item is the favourite; the
last work item cannot be removed. No work items ship pre-seeded.

### Accessibility

Day cells are focusable buttons with an accessible name giving the date, logged
and expected hours, and status. The status stripe is never the only carrier of
meaning — every cell also states its hours in text. Focus rings use the accent
colour and are never suppressed.

## Behaviour rules

1. **Top-up.** A selected day is filled to `Expected - Logged`. A day already at
   or above target is skipped and reported as skipped.
2. **Expected hours** come from `DailyHours` in settings, with the weekend,
   holiday and pre-holiday rules applied on top.
3. **Unknown blocks registration.** A failed fetch does not mean zero. Every day
   in the period becomes `Unknown` and registration is disabled until a refresh
   succeeds.
4. **Staleness is impossible by construction.** The register endpoint refetches
   the worklogs for the selected range and rebuilds the plan before posting
   anything, so a client's view of what is already logged is never trusted. If
   that refetch fails, the endpoint returns `409 Conflict` and posts nothing.
   The front end therefore carries no staleness logic at all.
5. **Refetch after submit.** The month is always refetched once the batch
   completes, so a day that landed despite an error surface corrects itself.

## Error handling

- **Fetch failure**, including a failure on any page of a paged read: the whole
  period becomes `Unknown`. A partial result is never displayed as fact.
- **Per-day submit failure**: the day is returned as `failed` with its message;
  other days continue; retry is per day.
- **401**: surfaced on the affected days with a prompt to open settings.
- **Holiday service offline**: existing fallback — all weekdays are treated as
  ordinary workdays and a warning banner appears. Combined with real logged
  data this can only propose too much, never duplicate.
- **Server not reachable from the page**: the SPA shows a plain "the app is not
  running" state rather than an empty calendar.

## Settings and storage

Unchanged on disk: `%AppData%\7PaceDesktop\settings.json` and `workitems.json`,
and the Credential Manager entry.

`AppSettings.LastDailyHours` becomes `AppSettings.DailyHours` (same JSON default
of 8), now a persistent target rather than a remembered last input. Migration
reads the old property name when present on first load.

## Distribution

`dotnet publish` produces a self-contained single-file executable for
`win-x64`. The Vite build output is included as static web assets, so the
executable is the whole app. On launch it binds a free port on `127.0.0.1`,
opens the default browser at that address, and keeps running until closed.

## Testing

C# unit tests, no UI:

- `WorkScheduleTests` — weekends, holidays, the pre-holiday 3 h reduction, the
  zero floor.
- `MonthPlanTests` — status classification for each of the six states, month
  totals restricted to the month, the all-`Unknown` factory.
- `FillPlannerTests` — top-up arithmetic, days at or over target skipped,
  proportional splitting on a partial day, rounding residual placement,
  `NonWorking` and `Unknown` days excluded.
- `PaceApiClientTests` — a stubbed `HttpMessageHandler` pinning the GET URL and
  query parameters, the paging loop across a 500-row boundary, the tolerant
  envelope parsing, and the client-side range filter.

Server endpoint tests via `WebApplicationFactory` with fakes for
`IWorkLogReader` and `IWorkLogClient`:

- the month payload's shape and its `failed` state,
- register planning and per-day results, including `simulate`,
- the `X-Pace-Client` header requirement on mutating endpoints,
- config and work item validation, and that no response ever contains the token.

Front-end tests with Vitest and Testing Library:

- day-cell rendering per status,
- selection reducer behaviour for drag, ctrl-click, week and bulk actions,
- the preview total for a mix of empty, partial and complete days,
- the blocking banner and disabled register button when `loadState` is `failed`.

Manual verification before release: one live GET against the iCore instance to
confirm the response field names and the timestamp's timezone, and a `Simulera`
run over a month with known logged time.

## Assumptions

- The 7Pace GET response uses the field names listed above. Unverified; see the
  API contract section.
- Worklog timestamps can be grouped by their date portion without timezone
  conversion. Unverified.
- Nager.Date returns Midsommarafton among Swedish holidays, which is what makes
  the day before it shorten. If it does not, the pre-holiday rule simply does
  not fire for that date; no code change is implied.
- The 7Pace API does not permit cross-origin browser calls. Not verified, and
  not load-bearing: the token argument alone requires the local server.

## Out of scope

Editing and deleting worklogs, work item title resolution, team views, a week
or day zoom level, comments on worklogs, hosting the app for others, and
cross-platform support.
