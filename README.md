# 7Pace Desktop

Bulk-register work hours into 7Pace Timetracker, with the month's already-registered time
visible so days are topped up rather than duplicated.

## Running it

Download or build `7PaceDesktop.exe`, then run it. It serves a local web app on
`127.0.0.1` and opens your browser. Nothing is hosted and nothing leaves your machine
except the calls to 7Pace itself.

On first run it asks for three things:

1. **Organisation** — your Azure DevOps account name, for example `icore v3`. Not the project.
2. **API-token** — from 7Pace: *Settings > Reporting and API*. Stored in Windows Credential
   Manager, never in a file and never sent to the browser.
3. **A work item** — at least one, to report time against.

The server picks a free port automatically. Pin it with `--Port=5111` if you need a
predictable address, for example `7PaceDesktop.exe --Port=5111`.

## Building from source

```bash
dotnet publish src/7PaceDesktop.Server -c Release -o publish
```

The Release build runs `npm ci && npm run build` in `web/` and embeds the result, so the
executable is the whole app. Node 20 or later is required to build; not to run.

## Development

Two shells:

```bash
dotnet run --project src/7PaceDesktop.Server -- --Port=5111
cd web && npm run dev
```

(`ASPNETCORE_URLS` has no effect here — Kestrel's `Listen()` call binds the loopback
address and port explicitly, which overrides it. `--Port` is read from configuration
instead.)

Then open `http://127.0.0.1:5173`. Vite proxies `/api` to the dotnet server.

## Tests

```bash
dotnet test tests/7PaceDesktop.Tests/7PaceDesktop.Tests.csproj
cd web && npm test
```
