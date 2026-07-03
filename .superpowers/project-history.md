# Project history

- 2026-07-03: Started 7PaceDesktop, a .NET 10 WPF app to bulk-register work hours into 7Pace Timetracker via its REST API, built on top of an earlier `Log-7PaceTime.ps1` PowerShell prototype; design spec written and committed covering Swedish-holiday-aware date-range generation (auto-skip weekends/holidays, auto-shorten the day before a holiday by 3 hours), per-user configurable work items with a favorite default, and Windows Credential Manager-backed token storage.
