# Lessons learned

- 7Pace Timetracker uses its own API token (created in ADO under Time Tracker > Settings > API Tokens), separate from an Azure DevOps PAT — do not conflate the two.
- The exact 7Pace `workLogs` POST payload shape (`timestamp`, `length` in seconds, `comment`) is a best-effort guess carried over from an earlier PowerShell prototype and is unverified against a live instance's Swagger docs (`https://<org>.timetracker.7pace.com/api/rest/help`) — confirm before relying on it in production.
- Context7 MCP server was not connected in this session, so 7Pace API docs could not be looked up through it; fell back to best-known API patterns instead.
- Because other iCore staff will use this app with different work items, the app must ship with zero hardcoded/pre-seeded work items — each installation's `workitems.json` (under `%AppData%`) starts empty and is filled via a mandatory first-run wizard.
- Plan deviation: spec's "open settings dialog automatically on 401" simplified to per-row error + menu access (documented in plan self-review).
- .NET 10 SDK 'dotnet new sln' emits .slnx (XML) not .sln; functionally equivalent, avoid hard-coding .sln extension in CI.
- Namespaces can't start with a digit: 7PaceDesktop.* projects use RootNamespace PaceDesktop.*
- 7Pace workLogs payload shape (Bearer auth, api-version=3.2, length in seconds) is UNVERIFIED against a live instance; pinned by tests, must be confirmed live in Task 10 before real use.
