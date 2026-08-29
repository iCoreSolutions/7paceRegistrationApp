# Task 17 — verify the 7Pace read contract against a live instance

**Status: OPEN. This is a release gate and it needs your API token.**

Everything else in the calendar-first redesign is built, reviewed and green. This one
check could not be done by an agent, because it requires calling your real 7Pace
instance. Until it passes, four facts the app depends on are **assumptions**.

Why this matters rather than being a formality: the **write** contract was written from
the same documentation and was wrong on two counts — the host name (`timetracker` instead
of `timehub`) and a field's casing (`timestamp` instead of `timeStamp`). Both were only
found by calling the real instance. The read contract has had no such contact yet.

## Step 1 — get a real response

Run this in the session with `!` so the output lands in the conversation, or in any
terminal. Replace the month with one you know has logged time. It reads three worklogs.

```powershell
$token = Read-Host -AsSecureString "7Pace API token" | ConvertFrom-SecureString -AsPlainText
curl.exe -s -H "Authorization: Bearer $token" "https://icore.timehub.7pace.com/api/rest/workLogs?api-version=3.2&`$fromTimestamp=2026-06-01T00:00:00&`$toTimestamp=2026-07-01T00:00:00&`$count=3" | ConvertFrom-Json | ConvertTo-Json -Depth 8
```

`icore` is the Azure DevOps **organization**, never a project name. If your token lives
under a different account, change it. Find your exact API URL in 7Pace under
**Settings > Reporting and API**.

**Paste the output with any identifying content trimmed** — comments and work item names
can be redacted freely; the structure is what matters.

## Step 2 — the four assumptions to confirm

| # | Assumption | How it fails if wrong |
| - | --- | --- |
| 1 | The array is at `data.workLogs`, at `data`, or at the root | The parser accepts all three, so any of them passes. A fourth shape needs a new branch in `FindArray`. |
| 2 | Fields are `id`, `timeStamp`, `length`, `workItemId`, `comment` | The lookup is case-insensitive, so casing is safe. A different **name** is not. |
| 3 | `length` is in **seconds** — a 6-hour worklog reads `21600` | If it is minutes, the `/ 3600.0` divisor makes every displayed hour 60× too small. |
| 4 | `timeStamp` is **local**, with no offset or trailing `Z` | The parser takes the first ten characters. A UTC timestamp would misfile an early-morning or late-evening worklog **by a day**. |

Assumption 4 is the one I would bet against. It is also the most damaging, because it
fails quietly: the totals still look plausible, they are just attributed to the wrong day.

## Step 3 — if anything differs, fix it test-first

Add the real (trimmed) body as a new `[Fact]` in `PaceApiClientTests` pinning the actual
shape, watch it fail, then correct `ParseWorkLogs` until it passes. If the timestamp
carries an offset, convert to local time before taking the date rather than slicing the
string, and add a test for a worklog logged at 23:30 local.

## Step 4 — check a month end to end

Run the published executable:

```
dotnet publish src/7PaceDesktop.Server -c Release -o publish
./publish/7PaceDesktop.Server.exe --Port=5111
```

Against the 7Pace web UI, confirm: logged hours per day match, the month total matches,
days you know are full read `Klar`, and partially logged days show the right shortfall.

Then select two days you know are empty, tick `Simulera`, and confirm the proposed hours
are what you expect. Untick it, register **one** day, and confirm in 7Pace that exactly
one worklog appeared with the right hours and work item.

## Also fold into this pass — manual checks no agent could do

- **The first-run wizard has never been walked through by anyone.** You will be the first.
- No horizontal scroll at 1280px wide, in both light and dark.
- With the network to 7Pace blocked, the calendar shows `okänt` and `Registrera` is disabled.
- A cosmetic question left open deliberately: a `Simulera` run still refetches the month,
  so the "Hämtad HH:MM" timestamp updates even though nothing was written. Harmless, but
  judge whether it reads as though something *was* written. If it does, the fix is a
  one-line guard.

## Step 5 — close the gate

Replace the spec's **Unverified.** paragraph with what was actually observed, remove the
matching entries from its **Assumptions** section, and append one line per finding to
`.superpowers/lessons-learned.md` — especially the real envelope, field names, `length`
unit and timestamp timezone, since those are exactly the four that were guessed.
