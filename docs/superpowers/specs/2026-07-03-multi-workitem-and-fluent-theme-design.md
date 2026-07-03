# 7PaceDesktop — Multi-Work-Item Days + Fluent Theme Design

Date: 2026-07-03
Status: Approved (design). Follows the initial app spec
(`2026-07-03-7pace-desktop-app-design.md`).

## Purpose

Two enhancements to the existing 7PaceDesktop app:

1. **Multiple work items per day** — let the user split a single day's time
   across more than one work item (e.g. Monday = 6h Product Development +
   2h Admin & internal). Most days stay a single work item; splitting is an
   occasional, per-day action.
2. **Modern Fluent look with a theme toggle** — restyle the app to a modern
   WinUI 3 / Visual-Studio-2026 appearance, following the Windows theme by
   default with an in-app System/Light/Dark toggle.

## Testing

Per the user's explicit direction, **no automated tests are added** for this
work. The existing 35-test suite must continue to pass (no regressions);
verification of the new features is by build + manual smoke test.

---

## Feature 1: Multiple work items per day

### Behavior

- The base generation flow is unchanged: Generate still produces exactly one
  row per weekday, at the day's target hours (8h normally, or the target
  minus 3h the day before a public holiday), using the favorite work item.
- Each preview row gains a **Split** action (in the actions column alongside
  the existing "Skicka om" / "Ta bort" buttons). Clicking Split inserts a new
  row **for the same date**, starting at **0h**, defaulting to the favorite
  work item. The user then selects the work item and enters its hours.
- Splitting can be repeated to add a third+ line to a day.
- Each row is still submitted as its own 7Pace worklog. A split day therefore
  produces multiple worklogs on that date with different work items — no API
  change is required.
- **Balance guard:** the app remembers each day's originally-generated target
  hours. For any day that has been **split (2 or more rows)**, if that day's
  rows do not sum to its target, that day's rows are highlighted with a
  distinct "unbalanced" color (separate from the existing pre-holiday
  highlight). A day with a single row is never flagged, even if its hours are
  edited — a lone edited value is treated as intentional. The bottom total
  remains the grand total across all rows.

### Components / under the hood

- `EntryRowViewModel` gains:
  - an observable `IsDayUnbalanced` flag the grid binds to for the highlight.
  - (The row already has `Date`, `Hours`, `SelectedWorkItem`, `HitZeroFloor`,
    `Status`, `Error`.)
- `MainViewModel` gains:
  - `Dictionary<DateOnly,double> _dayTargets` — populated at Generate time
    with each generated day's target hours.
  - `SplitRowCommand(EntryRowViewModel row)` — inserts a new
    `EntryRowViewModel` for `row.Date` at 0h with the favorite work item,
    positioned immediately after the last existing row for that date;
    subscribes it to `PropertyChanged` (for total + balance recalculation);
    then recalculates balance.
  - A `RecalculateBalance()` pass (invoked on split, remove, and any row
    `Hours` change) that, for each date with 2+ rows, sets `IsDayUnbalanced`
    on those rows when their summed hours ≠ the date's target (within a small
    floating-point epsilon), and clears it otherwise. Single-row dates always
    have `IsDayUnbalanced = false`.
  - `RemoveRow` unsubscribes the removed row and re-runs balance + total.
- The existing per-row `Hours` PropertyChanged subscription (added for the
  live total) is extended to also trigger `RecalculateBalance()`.

### Data flow

Generate → rows created, `_dayTargets` filled, balance recalculated (all
single-row, so nothing flagged). User clicks Split on a day → sibling row
added at 0h → that day now has 2 rows summing to less than target → day
highlighted. User sets the split hours so the day sums to target → highlight
clears. Register → every row (including splits) submitted as its own worklog.

### Error handling

- Splitting a row whose date is already at/over target is allowed; the day
  simply shows as unbalanced until the user rebalances — no hard block
  (consistent with the app trusting the reviewed preview).
- Removing rows re-evaluates balance so a day can return to a single,
  unflagged row.

---

## Feature 2: Modern Fluent look + theme toggle

### Behavior

- Adopt the WPF-UI Fluent library (https://github.com/lepoco/wpfui) for a
  WinUI 3 / modern-VS appearance across the three existing windows (Main,
  Setup wizard, Work items) — rounded controls, Fluent surfaces, proper
  light/dark palettes — without changing the window layouts.
- **Default: follow the Windows theme** (light or dark), and live-switch if
  the user changes the Windows theme while the app is open
  (`SystemThemeWatcher`).
- **In-app toggle** with three states — **System (default) / Light / Dark** —
  reachable from the main window (menu item or small segmented control). The
  choice persists in `settings.json` and is re-applied on startup.

### Components / under the hood

- Add the `WPF-UI` NuGet package to `7PaceDesktop.App`.
- `App.xaml` merges WPF-UI's `ThemesDictionary` and `ControlsDictionary`.
- A small `ThemeService` (App project) applies a `ThemePreference`: when
  `System`, enables `SystemThemeWatcher` and applies the current system
  theme; when `Light`/`Dark`, applies that theme explicitly and stops
  watching. Called at startup and whenever the toggle changes.
- `AppSettings` gains `ThemePreference Theme { get; set; } = ThemePreference.System;`
  where `ThemePreference` is an enum `{ System, Light, Dark }`. Persisted and
  loaded via the existing `SettingsStore` (JSON).
- The main window optionally uses WPF-UI's `FluentWindow` for the modern
  title bar / Mica chrome; the Setup and Work-items windows adopt the Fluent
  styles via the merged dictionaries.

### Scope (YAGNI)

- No custom brand colors and no bespoke control templates — the library's
  stock Fluent theme only, plus the System/Light/Dark toggle.
- No layout redesign of the windows; only restyling and the theme control.

---

## Out of scope

- Automated tests for the new features (per user direction).
- Editing a day's target hours directly.
- Changing the base one-row-per-day generation.
- Custom theming/branding beyond stock Fluent light/dark.
