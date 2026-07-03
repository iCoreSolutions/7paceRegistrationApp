# Project history

- 2026-07-03: Started 7PaceDesktop, a .NET 10 WPF app to bulk-register work hours into 7Pace Timetracker via its REST API, built on top of an earlier `Log-7PaceTime.ps1` PowerShell prototype; design spec written and committed covering Swedish-holiday-aware date-range generation (auto-skip weekends/holidays, auto-shorten the day before a holiday by 3 hours), per-user configurable work items with a favorite default, and Windows Credential Manager-backed token storage.
- 2026-07-03: Wrote full TDD implementation plan (10 tasks) at docs/superpowers/plans/2026-07-03-7pace-desktop-app.md.
- 2026-07-03: Started SDD execution of 7PaceDesktop; Task 1 scaffolded the 3-project .NET 10 solution (Core/App/Tests).
- 2026-07-03: Task 2 built domain models (WorkItem/Holiday/TimeEntry) and TimeEntryGenerator with the pre-holiday 3h-shortening rule; 6 unit tests green.
