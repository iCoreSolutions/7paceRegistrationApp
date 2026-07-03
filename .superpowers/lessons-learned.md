# Lessons learned

- 7Pace Timetracker uses its own API token (created in ADO under Time Tracker > Settings > API Tokens), separate from an Azure DevOps PAT — do not conflate the two.
- The exact 7Pace `workLogs` POST payload shape (`timestamp`, `length` in seconds, `comment`) is a best-effort guess carried over from an earlier PowerShell prototype and is unverified against a live instance's Swagger docs (`https://<org>.timetracker.7pace.com/api/rest/help`) — confirm before relying on it in production.
- Context7 MCP server was not connected in this session, so 7Pace API docs could not be looked up through it; fell back to best-known API patterns instead.
- Because other iCore staff will use this app with different work items, the app must ship with zero hardcoded/pre-seeded work items — each installation's `workitems.json` (under `%AppData%`) starts empty and is filled via a mandatory first-run wizard.
