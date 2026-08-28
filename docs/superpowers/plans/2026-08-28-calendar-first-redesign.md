# Calendar-First Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the WPF entry grid with a locally-run web app whose month calendar shows the user's real registered time from 7Pace, so bulk registration tops days up to their target instead of duplicating time already logged.

**Architecture:** Three layers. `Core` (C#, no UI) keeps the verified 7Pace client, holidays, storage and credentials, and gains the planning units — `WorkSchedule`, `MonthPlan`, `FillPlanner`. `Server` (ASP.NET Core Minimal API, bound to `127.0.0.1`) holds the token, owns all planning arithmetic, and serves the SPA. `web/` (React + TypeScript + Tailwind) is the UI. The WPF project is deleted once the web UI reaches parity, so the repo stays runnable throughout.

**Tech Stack:** .NET 10 (`net10.0-windows`), ASP.NET Core Minimal API, xunit 2.9.3, React + TypeScript, Vite, Tailwind CSS, Vitest + Testing Library.

**Spec:** `docs/superpowers/specs/2026-08-28-calendar-first-redesign-design.md` (revision 2)

## Global Constraints

- Target framework is `net10.0-windows` for `Core`, `Server` and `Tests`. `CredentialStore` is Windows-only; do not attempt to make it portable.
- Namespaces are `PaceDesktop.Core.*`, `PaceDesktop.Server.*`, `PaceDesktop.Tests`. A namespace cannot start with a digit — the assemblies are named `7PaceDesktop.*` but the root namespaces are not.
- The 7Pace cloud REST base is `https://{account}.timehub.7pace.com/api/rest/...?api-version=3.2`, where `{account}` is the Azure DevOps organization (`icore`), never the project. Auth is `Authorization: Bearer {token}`.
- The GET response body's field names are **unverified**. Implement the tolerant parser exactly as written in Task 1 and confirm it live in Task 17 before release.
- **The 7Pace token never appears in an HTTP response body, log line, or anything the browser can read.** It lives in Windows Credential Manager under `7PaceDesktop:{org}`.
- Planning arithmetic (`WorkSchedule`, `MonthPlan`, `FillPlanner`) exists only in C#. The front end may compute `max(0, expected - logged)` and sums of it, nothing more. Never port the split or rounding rules to TypeScript.
- The server binds `127.0.0.1` only. Every mutating endpoint requires the header `X-Pace-Client: 1`, and no CORS policy is configured.
- No hardcoded or pre-seeded work items anywhere. `workitems.json` ships empty.
- UI copy is Swedish (`Registrera`, `Inställningar`, `Simulera`, `Alla tomma dagar`, `Rensa markering`).
- Design tokens come from the spec's token table, which is lifted from `src/7PaceDesktop.App/Themes/Palette.*.xaml`. Use those exact hex values.
- Floating-point comparisons use `Epsilon = 0.001`.
- Build: `dotnet build 7PaceDesktop.slnx`. C# tests: `dotnet test tests/7PaceDesktop.Tests/7PaceDesktop.Tests.csproj`. Front-end tests: `npm test` in `web/`.
- 35 C# tests are green at the start of this plan. Every task ends green.

## File Structure

**Core (created):**

| File | Responsibility |
| --- | --- |
| `src/7PaceDesktop.Core/Models/ExistingWorkLog.cs` | A worklog already registered in 7Pace |
| `src/7PaceDesktop.Core/Services/IWorkLogReader.cs` | Read side of the 7Pace API |
| `src/7PaceDesktop.Core/Planning/WorkSchedule.cs` | Expected hours for a date |
| `src/7PaceDesktop.Core/Planning/CalendarGrid.cs` | Monday-first grid range and ISO week numbers |
| `src/7PaceDesktop.Core/Planning/MonthPlan.cs` | `DayStatus`, `DayPlan`, range merge, month totals |
| `src/7PaceDesktop.Core/Planning/FillPlanner.cs` | `FillLine`, `FillSpec`, `FillSummary`, planning |

**Server (created):** `src/7PaceDesktop.Server/7PaceDesktop.Server.csproj`, `Program.cs`, `AppServices.cs`, `Contracts.cs`, `Endpoints/ConfigEndpoints.cs`, `Endpoints/MonthEndpoints.cs`, `Endpoints/RegisterEndpoints.cs`, `ClientHeaderFilter.cs`.

**Web (created):** `web/` — `index.html`, `src/main.tsx`, `src/App.tsx`, `src/api.ts`, `src/theme.css`, `src/selection.ts`, `src/components/*`, `src/views/*`, plus `*.test.ts(x)` beside what they test.

**Modified:** `PaceApiClient.cs`, `AppSettings.cs`, `PaceApiClientTests.cs`, `7PaceDesktop.slnx`.

**Deleted at Task 16:** the whole `src/7PaceDesktop.App` project, `TimeEntryGenerator.cs`, `TimeEntryGeneratorTests.cs`, `MainViewModelTests.cs`, `SetupViewModelTests.cs`, `WorkItemsViewModelTests.cs`.

---

### Task 1: Read side of the 7Pace API

**Files:**
- Create: `src/7PaceDesktop.Core/Models/ExistingWorkLog.cs`
- Create: `src/7PaceDesktop.Core/Services/IWorkLogReader.cs`
- Modify: `src/7PaceDesktop.Core/Services/PaceApiClient.cs`
- Test: `tests/7PaceDesktop.Tests/PaceApiClientTests.cs`

**Interfaces:**
- Consumes: `PaceApiClient(HttpClient, string organization, string token)`, `PaceApiException(int statusCode, string message)`, `PaceApiClient.NormalizeAccount(string)` — all already exist.
- Produces:
  - `record ExistingWorkLog(string Id, DateOnly Date, double Hours, int WorkItemId, string? Comment)`
  - `IWorkLogReader.GetWorkLogsAsync(DateOnly from, DateOnly to, CancellationToken ct = default) -> Task<IReadOnlyList<ExistingWorkLog>>`
  - `PaceApiClient.ParseWorkLogs(JsonElement root) -> IReadOnlyList<ExistingWorkLog>` (public static, so the parser can be pinned directly)
  - `PaceApiClient` now implements `IWorkLogClient, IWorkLogReader`.

- [ ] **Step 1: Write the failing tests**

Append to `tests/7PaceDesktop.Tests/PaceApiClientTests.cs`, adding `using System.Text;` at the top.

```csharp
    // A handler that answers a scripted queue of bodies and records every request URI.
    private sealed class SequenceHandler(params string[] bodies) : HttpMessageHandler
    {
        public readonly List<string> Urls = [];
        private int _index;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Urls.Add(request.RequestUri!.ToString());
            var body = _index < bodies.Length ? bodies[_index] : "{\"data\":{\"workLogs\":[]}}";
            _index++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

    private static string PageOf(int count)
    {
        var logs = Enumerable.Range(0, count).Select(i =>
            $"{{\"id\":\"w{i}\",\"timeStamp\":\"2026-06-{1 + (i % 28):00}T09:00:00\",\"length\":3600,\"workItemId\":42}}");
        return "{\"data\":{\"workLogs\":[" + string.Join(",", logs) + "]}}";
    }

    [Fact]
    public async Task GetWorkLogs_SendsExpectedRequest()
    {
        var handler = new SequenceHandler(PageOf(1));
        var client = new PaceApiClient(new HttpClient(handler), "icore", "tok123");

        await client.GetWorkLogsAsync(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));

        var url = handler.Urls.Single();
        Assert.StartsWith("https://icore.timehub.7pace.com/api/rest/workLogs?api-version=3.2", url);
        Assert.Contains("$fromTimestamp=2026-06-01T00:00:00", url);
        // Both bounds are exclusive in the 7Pace API, so the day after the range end is sent.
        Assert.Contains("$toTimestamp=2026-07-01T00:00:00", url);
        Assert.Contains("$count=500", url);
        Assert.Contains("$skip=0", url);
    }

    [Fact]
    public async Task GetWorkLogs_ParsesFields()
    {
        const string body = """
            {"data":{"workLogs":[
              {"id":"abc","timeStamp":"2026-06-03T09:00:00","length":21600,"workItemId":12345,"comment":"hej"}
            ]}}
            """;
        var client = new PaceApiClient(new HttpClient(new SequenceHandler(body)), "icore", "t");

        var logs = await client.GetWorkLogsAsync(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));

        var log = Assert.Single(logs);
        Assert.Equal("abc", log.Id);
        Assert.Equal(new DateOnly(2026, 6, 3), log.Date);
        Assert.Equal(6, log.Hours);          // 21600 seconds
        Assert.Equal(12345, log.WorkItemId);
        Assert.Equal("hej", log.Comment);
    }

    [Fact]
    public async Task GetWorkLogs_PagesUntilShortPage()
    {
        // 500 rows means "there may be more"; the second page is short and ends the loop.
        var handler = new SequenceHandler(PageOf(500), PageOf(3));
        var client = new PaceApiClient(new HttpClient(handler), "icore", "t");

        var logs = await client.GetWorkLogsAsync(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));

        Assert.Equal(503, logs.Count);
        Assert.Equal(2, handler.Urls.Count);
        Assert.Contains("$skip=0", handler.Urls[0]);
        Assert.Contains("$skip=500", handler.Urls[1]);
    }

    [Fact]
    public async Task GetWorkLogs_FiltersToRequestedRange()
    {
        // The API's bounds are exclusive; the client filters client-side so boundary
        // semantics cannot leak a neighbouring day into the result.
        const string body = """
            {"data":{"workLogs":[
              {"id":"a","timeStamp":"2026-05-31T09:00:00","length":3600,"workItemId":1},
              {"id":"b","timeStamp":"2026-06-01T00:00:00","length":3600,"workItemId":1},
              {"id":"c","timeStamp":"2026-06-30T23:30:00","length":3600,"workItemId":1},
              {"id":"d","timeStamp":"2026-07-01T09:00:00","length":3600,"workItemId":1}
            ]}}
            """;
        var client = new PaceApiClient(new HttpClient(new SequenceHandler(body)), "icore", "t");

        var logs = await client.GetWorkLogsAsync(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));

        Assert.Equal(["b", "c"], logs.Select(l => l.Id));
    }

    [Fact]
    public async Task GetWorkLogs_ThrowsOnError()
    {
        var client = new PaceApiClient(new HttpClient(new CapturingHandler(HttpStatusCode.Unauthorized)), "icore", "t");

        var ex = await Assert.ThrowsAsync<PaceApiException>(
            () => client.GetWorkLogsAsync(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30)));
        Assert.Equal(401, ex.StatusCode);
    }

    [Theory]
    // The response envelope is unverified, so the parser accepts the three plausible shapes.
    [InlineData("""{"data":{"workLogs":[{"id":"x","timeStamp":"2026-06-02T09:00:00","length":3600,"workItemId":7}]}}""")]
    [InlineData("""{"data":[{"id":"x","timeStamp":"2026-06-02T09:00:00","length":3600,"workItemId":7}]}""")]
    [InlineData("""[{"id":"x","timeStamp":"2026-06-02T09:00:00","length":3600,"workItemId":7}]""")]
    public void ParseWorkLogs_AcceptsEachEnvelope(string json)
    {
        using var doc = JsonDocument.Parse(json);

        var log = Assert.Single(PaceApiClient.ParseWorkLogs(doc.RootElement));
        Assert.Equal("x", log.Id);
        Assert.Equal(new DateOnly(2026, 6, 2), log.Date);
        Assert.Equal(1, log.Hours);
        Assert.Equal(7, log.WorkItemId);
        Assert.Null(log.Comment);
    }

    [Fact]
    public void ParseWorkLogs_IsCaseInsensitiveAndSkipsUnusableRows()
    {
        const string json = """
            {"Data":{"WorkLogs":[
              {"Id":"x","TimeStamp":"2026-06-02T09:00:00","Length":3600,"WorkItemId":7},
              {"id":"broken","length":3600,"workItemId":7}
            ]}}
            """;
        using var doc = JsonDocument.Parse(json);

        var log = Assert.Single(PaceApiClient.ParseWorkLogs(doc.RootElement));
        Assert.Equal("x", log.Id);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/7PaceDesktop.Tests/7PaceDesktop.Tests.csproj --filter "FullyQualifiedName~PaceApiClientTests"`
Expected: compile errors — `GetWorkLogsAsync` and `ParseWorkLogs` do not exist.

- [ ] **Step 3: Create the model**

`src/7PaceDesktop.Core/Models/ExistingWorkLog.cs`:

```csharp
namespace PaceDesktop.Core.Models;

/// <summary>A worklog that already exists in 7Pace. Read-only: the app never edits or deletes these.</summary>
public sealed record ExistingWorkLog(string Id, DateOnly Date, double Hours, int WorkItemId, string? Comment);
```

- [ ] **Step 4: Create the interface**

`src/7PaceDesktop.Core/Services/IWorkLogReader.cs`:

```csharp
using PaceDesktop.Core.Models;

namespace PaceDesktop.Core.Services;

public interface IWorkLogReader
{
    /// <summary>Worklogs for the token owner, inclusive of both bounds.</summary>
    Task<IReadOnlyList<ExistingWorkLog>> GetWorkLogsAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
}
```

- [ ] **Step 5: Implement the read side**

In `src/7PaceDesktop.Core/Services/PaceApiClient.cs`, add `using System.Text.Json;` and change the declaration to:

```csharp
public sealed partial class PaceApiClient(HttpClient http, string organization, string token)
    : IWorkLogClient, IWorkLogReader
```

Then add these members:

```csharp
    private const int PageSize = 500;

    public async Task<IReadOnlyList<ExistingWorkLog>> GetWorkLogsAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var account = NormalizeAccount(organization);
        var all = new List<ExistingWorkLog>();

        for (var skip = 0; ; skip += PageSize)
        {
            // Both bounds are exclusive in the 7Pace API, so the upper bound is the day after
            // the range end. The result is filtered below regardless, so the exact semantics
            // of the bounds cannot change what the caller sees.
            var url = $"https://{account}.timehub.7pace.com/api/rest/workLogs?api-version=3.2"
                    + $"&$fromTimestamp={from:yyyy-MM-dd}T00:00:00"
                    + $"&$toTimestamp={to.AddDays(1):yyyy-MM-dd}T00:00:00"
                    + $"&$count={PageSize}&$skip={skip}";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(ct);
                throw new PaceApiException((int)response.StatusCode,
                    $"7Pace API error {(int)response.StatusCode}: {error}");
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var page = ParseWorkLogs(doc.RootElement);
            all.AddRange(page);
            if (page.Count < PageSize) break;
        }

        return all.Where(l => l.Date >= from && l.Date <= to).ToList();
    }

    /// <summary>
    /// Reads worklogs out of a response body. The envelope is UNVERIFIED against a live
    /// instance, so all three plausible shapes are accepted: {data:{workLogs:[]}}, {data:[]}
    /// and a bare array, with case-insensitive property lookup. Confirm before release.
    /// </summary>
    public static IReadOnlyList<ExistingWorkLog> ParseWorkLogs(JsonElement root)
    {
        if (FindArray(root) is not { } items) return [];

        var logs = new List<ExistingWorkLog>();
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            if (!TryGet(item, "timeStamp", out var stamp)) continue;
            var text = stamp.GetString();
            if (text is null || text.Length < 10) continue;
            if (!DateOnly.TryParse(text[..10], out var date)) continue;

            var seconds = TryGet(item, "length", out var len) && len.ValueKind == JsonValueKind.Number
                ? len.GetDouble()
                : 0;
            var workItemId = TryGet(item, "workItemId", out var wi) && wi.ValueKind == JsonValueKind.Number
                ? wi.GetInt32()
                : 0;
            var id = TryGet(item, "id", out var idEl)
                ? idEl.ValueKind == JsonValueKind.String ? idEl.GetString()! : idEl.ToString()
                : string.Empty;
            var comment = TryGet(item, "comment", out var c) && c.ValueKind == JsonValueKind.String
                ? c.GetString()
                : null;

            logs.Add(new ExistingWorkLog(id, date, seconds / 3600.0, workItemId, comment));
        }
        return logs;
    }

    private static JsonElement? FindArray(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array) return root;
        if (root.ValueKind != JsonValueKind.Object) return null;
        if (!TryGet(root, "data", out var data)) return null;
        if (data.ValueKind == JsonValueKind.Array) return data;
        if (data.ValueKind == JsonValueKind.Object && TryGet(data, "workLogs", out var logs)
            && logs.ValueKind == JsonValueKind.Array) return logs;
        return null;
    }

    // 7Pace's casing is not guaranteed, so property lookup is case-insensitive.
    private static bool TryGet(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }
        value = default;
        return false;
    }
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/7PaceDesktop.Tests/7PaceDesktop.Tests.csproj --filter "FullyQualifiedName~PaceApiClientTests"`
Expected: PASS.

- [ ] **Step 7: Run the whole suite**

Run: `dotnet test tests/7PaceDesktop.Tests/7PaceDesktop.Tests.csproj`
Expected: PASS, 35 existing tests plus 9 new ones.

- [ ] **Step 8: Commit**

```bash
git add src/7PaceDesktop.Core/Models/ExistingWorkLog.cs src/7PaceDesktop.Core/Services/IWorkLogReader.cs src/7PaceDesktop.Core/Services/PaceApiClient.cs tests/7PaceDesktop.Tests/PaceApiClientTests.cs
git commit -m "feat: read existing worklogs from 7Pace"
```

---

### Task 2: WorkSchedule

**Files:**
- Create: `src/7PaceDesktop.Core/Planning/WorkSchedule.cs`
- Test: `tests/7PaceDesktop.Tests/WorkScheduleTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces:
  - `record ScheduledDay(double Hours, bool HitZeroFloor)`
  - `WorkSchedule(double dailyHours, IReadOnlySet<DateOnly> holidays)` with `double DailyHours { get; }` and `ScheduledDay Expected(DateOnly date)`.

`TimeEntryGenerator` stays for now — the WPF app still references it and both are deleted in Task 16.

- [ ] **Step 1: Write the failing tests**

`tests/7PaceDesktop.Tests/WorkScheduleTests.cs`:

```csharp
using PaceDesktop.Core.Planning;

namespace PaceDesktop.Tests;

public class WorkScheduleTests
{
    private static WorkSchedule Schedule(double daily = 8, params DateOnly[] holidays) =>
        new(daily, new HashSet<DateOnly>(holidays));

    [Fact]
    public void OrdinaryWeekday_IsTheDailyTarget()
    {
        // Mon 2026-06-01
        var day = Schedule().Expected(new DateOnly(2026, 6, 1));

        Assert.Equal(8, day.Hours);
        Assert.False(day.HitZeroFloor);
    }

    [Theory]
    [InlineData(2026, 6, 6)]  // Saturday
    [InlineData(2026, 6, 7)]  // Sunday
    public void Weekend_IsZero(int y, int m, int d)
    {
        Assert.Equal(0, Schedule().Expected(new DateOnly(y, m, d)).Hours);
    }

    [Fact]
    public void Holiday_IsZero()
    {
        var holiday = new DateOnly(2026, 6, 19);

        Assert.Equal(0, Schedule(8, holiday).Expected(holiday).Hours);
    }

    [Fact]
    public void DayBeforeHoliday_IsShortenedByThree()
    {
        // Fri 2026-06-19 is a holiday, so Thu 2026-06-18 is 5h.
        var day = Schedule(8, new DateOnly(2026, 6, 19)).Expected(new DateOnly(2026, 6, 18));

        Assert.Equal(5, day.Hours);
        Assert.False(day.HitZeroFloor);
    }

    [Fact]
    public void DayBeforeHoliday_FloorsAtZero_AndFlagsIt()
    {
        var day = Schedule(2, new DateOnly(2026, 6, 19)).Expected(new DateOnly(2026, 6, 18));

        Assert.Equal(0, day.Hours);
        Assert.True(day.HitZeroFloor);
    }

    [Fact]
    public void HolidayOnMonday_DoesNotShortenTheFridayBefore()
    {
        // The reduction looks only at the next calendar day, so a weekend breaks the chain.
        var day = Schedule(8, new DateOnly(2026, 7, 13)).Expected(new DateOnly(2026, 7, 10));

        Assert.Equal(8, day.Hours);
    }

    [Fact]
    public void Weekend_IsNotShortened_EvenBeforeAHoliday()
    {
        // Sun 2026-06-21 before a Mon holiday stays 0, not -3.
        var day = Schedule(8, new DateOnly(2026, 6, 22)).Expected(new DateOnly(2026, 6, 21));

        Assert.Equal(0, day.Hours);
        Assert.False(day.HitZeroFloor);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/7PaceDesktop.Tests/7PaceDesktop.Tests.csproj --filter "FullyQualifiedName~WorkScheduleTests"`
Expected: compile error — `PaceDesktop.Core.Planning` does not exist.

- [ ] **Step 3: Implement**

`src/7PaceDesktop.Core/Planning/WorkSchedule.cs`:

```csharp
namespace PaceDesktop.Core.Planning;

/// <summary>Expected hours for a date, and whether the pre-holiday reduction hit the zero floor.</summary>
public sealed record ScheduledDay(double Hours, bool HitZeroFloor);

/// <summary>
/// The user's working pattern: a daily target, weekends and Swedish holidays off, and the
/// workday immediately before a holiday shortened by three hours.
/// </summary>
public sealed class WorkSchedule(double dailyHours, IReadOnlySet<DateOnly> holidays)
{
    private const double PreHolidayReduction = 3;

    public double DailyHours { get; } = dailyHours;

    public ScheduledDay Expected(DateOnly date)
    {
        if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return new ScheduledDay(0, false);
        if (holidays.Contains(date)) return new ScheduledDay(0, false);
        if (!holidays.Contains(date.AddDays(1))) return new ScheduledDay(DailyHours, false);

        var hours = DailyHours - PreHolidayReduction;
        return hours <= 0 ? new ScheduledDay(0, true) : new ScheduledDay(hours, false);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/7PaceDesktop.Tests/7PaceDesktop.Tests.csproj --filter "FullyQualifiedName~WorkScheduleTests"`
Expected: PASS, 8 tests.

- [ ] **Step 5: Commit**

```bash
git add src/7PaceDesktop.Core/Planning/WorkSchedule.cs tests/7PaceDesktop.Tests/WorkScheduleTests.cs
git commit -m "feat: WorkSchedule with weekend, holiday and pre-holiday rules"
```

---

### Task 3: CalendarGrid and MonthPlan

**Files:**
- Create: `src/7PaceDesktop.Core/Planning/CalendarGrid.cs`
- Create: `src/7PaceDesktop.Core/Planning/MonthPlan.cs`
- Test: `tests/7PaceDesktop.Tests/MonthPlanTests.cs`

**Interfaces:**
- Consumes: `ExistingWorkLog` (Task 1), `WorkSchedule`, `ScheduledDay` (Task 2).
- Produces:
  - `CalendarGrid.RangeFor(int year, int month) -> (DateOnly From, DateOnly To)`
  - `CalendarGrid.IsoWeek(DateOnly date) -> int`
  - `enum DayStatus { NonWorking, Empty, Partial, Complete, Over, Unknown }`
  - `record DayPlan(DateOnly Date, double Expected, double Logged, IReadOnlyList<ExistingWorkLog> Existing, DayStatus Status, bool HitZeroFloor)` with `double Remaining { get; }`
  - `record PlanTotals(double Expected, double Logged, double Missing)`
  - `MonthPlan.Build(DateOnly from, DateOnly to, WorkSchedule schedule, IReadOnlyList<ExistingWorkLog> logs) -> MonthPlan`
  - `MonthPlan.Unknown(DateOnly from, DateOnly to, WorkSchedule schedule) -> MonthPlan`
  - `MonthPlan.Day(DateOnly date) -> DayPlan?`, `MonthPlan.Days`, `MonthPlan.IsUnknown`, `MonthPlan.TotalsForMonth(int year, int month) -> PlanTotals`
  - `MonthPlan.Epsilon` (`const double`, 0.001)

- [ ] **Step 1: Write the failing tests**

`tests/7PaceDesktop.Tests/MonthPlanTests.cs`:

```csharp
using PaceDesktop.Core.Models;
using PaceDesktop.Core.Planning;

namespace PaceDesktop.Tests;

public class MonthPlanTests
{
    private static readonly WorkSchedule Plain = new(8, new HashSet<DateOnly>());

    private static ExistingWorkLog Log(int day, double hours, int workItemId = 42) =>
        new($"w{day}-{hours}", new DateOnly(2026, 6, day), hours, workItemId, null);

    [Theory]
    // June 2026 starts on a Monday and has 30 days, so the grid is exactly 5 weeks.
    [InlineData(2026, 6, "2026-06-01", "2026-07-05")]
    // August 2026 starts on a Saturday, so the grid needs 6 weeks and starts in July.
    [InlineData(2026, 8, "2026-07-27", "2026-09-06")]
    // February 2027 starts on a Monday with 28 days: exactly 4 weeks.
    [InlineData(2027, 2, "2027-02-01", "2027-02-28")]
    public void RangeFor_CoversWholeWeeksMondayFirst(int year, int month, string from, string to)
    {
        var (actualFrom, actualTo) = CalendarGrid.RangeFor(year, month);

        Assert.Equal(DateOnly.Parse(from), actualFrom);
        Assert.Equal(DateOnly.Parse(to), actualTo);
        Assert.Equal(DayOfWeek.Monday, actualFrom.DayOfWeek);
        Assert.Equal(DayOfWeek.Sunday, actualTo.DayOfWeek);
        Assert.Equal(0, (actualTo.DayNumber - actualFrom.DayNumber + 1) % 7);
    }

    [Fact]
    public void IsoWeek_MatchesTheIsoCalendar()
    {
        Assert.Equal(23, CalendarGrid.IsoWeek(new DateOnly(2026, 6, 1)));
        Assert.Equal(27, CalendarGrid.IsoWeek(new DateOnly(2026, 6, 29)));
    }

    [Fact]
    public void Build_ClassifiesEveryStatus()
    {
        var holidays = new HashSet<DateOnly> { new(2026, 6, 19) };
        var schedule = new WorkSchedule(8, holidays);
        var (from, to) = CalendarGrid.RangeFor(2026, 6);

        var plan = MonthPlan.Build(from, to, schedule,
        [
            Log(3, 6),            // partial
            Log(4, 8),            // complete
            Log(17, 5), Log(17, 4) // 9h on an 8h day -> over
        ]);

        Assert.Equal(DayStatus.Empty, plan.Day(new DateOnly(2026, 6, 5))!.Status);
        Assert.Equal(DayStatus.Partial, plan.Day(new DateOnly(2026, 6, 3))!.Status);
        Assert.Equal(DayStatus.Complete, plan.Day(new DateOnly(2026, 6, 4))!.Status);
        Assert.Equal(DayStatus.Over, plan.Day(new DateOnly(2026, 6, 17))!.Status);
        Assert.Equal(DayStatus.NonWorking, plan.Day(new DateOnly(2026, 6, 6))!.Status);   // Saturday
        Assert.Equal(DayStatus.NonWorking, plan.Day(new DateOnly(2026, 6, 19))!.Status);  // holiday
        Assert.False(plan.IsUnknown);
    }

    [Fact]
    public void Build_SumsAndKeepsTheDaysExistingWorklogs()
    {
        var (from, to) = CalendarGrid.RangeFor(2026, 6);

        var plan = MonthPlan.Build(from, to, Plain, [Log(17, 5, 100), Log(17, 4, 200)]);

        var day = plan.Day(new DateOnly(2026, 6, 17))!;
        Assert.Equal(9, day.Logged);
        Assert.Equal(8, day.Expected);
        Assert.Equal(0, day.Remaining);                       // never negative
        Assert.Equal([100, 200], day.Existing.Select(e => e.WorkItemId));
    }

    [Fact]
    public void Build_PreHolidayDayCarriesItsShortenedTargetAndRemaining()
    {
        var schedule = new WorkSchedule(8, new HashSet<DateOnly> { new(2026, 6, 19) });
        var (from, to) = CalendarGrid.RangeFor(2026, 6);

        var plan = MonthPlan.Build(from, to, schedule, [Log(18, 2)]);

        var day = plan.Day(new DateOnly(2026, 6, 18))!;
        Assert.Equal(5, day.Expected);
        Assert.Equal(3, day.Remaining);
        Assert.Equal(DayStatus.Partial, day.Status);
    }

    [Fact]
    public void Unknown_MarksWorkdaysUnknownButKeepsWeekendsNonWorking()
    {
        var (from, to) = CalendarGrid.RangeFor(2026, 6);

        var plan = MonthPlan.Unknown(from, to, Plain);

        Assert.True(plan.IsUnknown);
        Assert.Equal(DayStatus.Unknown, plan.Day(new DateOnly(2026, 6, 1))!.Status);
        // The schedule is known locally, so a weekend stays a weekend during a failed fetch.
        Assert.Equal(DayStatus.NonWorking, plan.Day(new DateOnly(2026, 6, 6))!.Status);
    }

    [Fact]
    public void TotalsForMonth_IgnoresAdjacentMonthDaysAndNonWorkingDays()
    {
        var schedule = new WorkSchedule(8, new HashSet<DateOnly>());
        var (from, to) = CalendarGrid.RangeFor(2026, 8);   // grid starts 2026-07-27
        var logs = new List<ExistingWorkLog>
        {
            new("july", new DateOnly(2026, 7, 28), 8, 1, null),   // outside August, must not count
            new("aug", new DateOnly(2026, 8, 3), 6, 1, null)
        };

        var totals = MonthPlan.Build(from, to, schedule, logs).TotalsForMonth(2026, 8);

        // August 2026 has 21 weekdays and no holidays: 21 * 8 = 168 expected.
        Assert.Equal(168, totals.Expected);
        Assert.Equal(6, totals.Logged);
        Assert.Equal(162, totals.Missing);
    }

    [Fact]
    public void TotalsForMonth_ReportsNoMissingHoursWhenTheMonthIsUnknown()
    {
        var (from, to) = CalendarGrid.RangeFor(2026, 6);

        var totals = MonthPlan.Unknown(from, to, Plain).TotalsForMonth(2026, 6);

        // 22 weekdays in June 2026 (it starts on a Monday and ends on a Tuesday): 22 * 8 = 176.
        Assert.Equal(176, totals.Expected);
        Assert.Equal(0, totals.Logged);
        Assert.Equal(0, totals.Missing);   // unknown days cannot be reported as missing
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/7PaceDesktop.Tests/7PaceDesktop.Tests.csproj --filter "FullyQualifiedName~MonthPlanTests"`
Expected: compile errors — `CalendarGrid` and `MonthPlan` do not exist.

- [ ] **Step 3: Implement CalendarGrid**

`src/7PaceDesktop.Core/Planning/CalendarGrid.cs`:

```csharp
using System.Globalization;

namespace PaceDesktop.Core.Planning;

/// <summary>Geometry of the displayed month grid: Monday-first whole weeks, and ISO week numbers.</summary>
public static class CalendarGrid
{
    /// <summary>
    /// The inclusive date range of the grid for a month: whole Monday-to-Sunday weeks covering
    /// every day of the month, so the grid includes the leading and trailing days of neighbours.
    /// </summary>
    public static (DateOnly From, DateOnly To) RangeFor(int year, int month)
    {
        var first = new DateOnly(year, month, 1);
        var offset = ((int)first.DayOfWeek + 6) % 7;   // Monday = 0
        var from = first.AddDays(-offset);
        var weeks = (int)Math.Ceiling((offset + DateTime.DaysInMonth(year, month)) / 7.0);
        return (from, from.AddDays(weeks * 7 - 1));
    }

    public static int IsoWeek(DateOnly date) => ISOWeek.GetWeekOfYear(date.ToDateTime(TimeOnly.MinValue));
}
```

- [ ] **Step 4: Implement MonthPlan**

`src/7PaceDesktop.Core/Planning/MonthPlan.cs`:

```csharp
using PaceDesktop.Core.Models;

namespace PaceDesktop.Core.Planning;

public enum DayStatus { NonWorking, Empty, Partial, Complete, Over, Unknown }

public sealed record DayPlan(
    DateOnly Date,
    double Expected,
    double Logged,
    IReadOnlyList<ExistingWorkLog> Existing,
    DayStatus Status,
    bool HitZeroFloor)
{
    /// <summary>Hours still needed to reach the day's target. Never negative.</summary>
    public double Remaining => Math.Max(0, Expected - Logged);
}

public sealed record PlanTotals(double Expected, double Logged, double Missing);

/// <summary>
/// The merge of a working schedule with the worklogs actually registered in 7Pace, over the
/// whole displayed grid range. Pure: no I/O, no UI.
/// </summary>
public sealed class MonthPlan
{
    public const double Epsilon = 0.001;

    private readonly Dictionary<DateOnly, DayPlan> _byDate;

    public IReadOnlyList<DayPlan> Days { get; }
    public bool IsUnknown { get; }

    private MonthPlan(List<DayPlan> days, bool isUnknown)
    {
        Days = days;
        IsUnknown = isUnknown;
        _byDate = days.ToDictionary(d => d.Date);
    }

    public DayPlan? Day(DateOnly date) => _byDate.GetValueOrDefault(date);

    public static MonthPlan Build(DateOnly from, DateOnly to, WorkSchedule schedule,
        IReadOnlyList<ExistingWorkLog> logs)
    {
        var grouped = logs.GroupBy(l => l.Date).ToDictionary(g => g.Key, g => (IReadOnlyList<ExistingWorkLog>)g.ToList());
        var days = new List<DayPlan>();

        for (var date = from; date <= to; date = date.AddDays(1))
        {
            var scheduled = schedule.Expected(date);
            IReadOnlyList<ExistingWorkLog> existing = grouped.GetValueOrDefault(date) ?? [];
            var logged = existing.Sum(e => e.Hours);
            days.Add(new DayPlan(date, scheduled.Hours, logged, existing,
                Classify(scheduled.Hours, logged, unknown: false), scheduled.HitZeroFloor));
        }

        return new MonthPlan(days, false);
    }

    /// <summary>
    /// A plan for a period whose worklogs could not be fetched. Working days are Unknown rather
    /// than empty, because treating them as empty would double-log real time.
    /// </summary>
    public static MonthPlan Unknown(DateOnly from, DateOnly to, WorkSchedule schedule)
    {
        var days = new List<DayPlan>();
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            var scheduled = schedule.Expected(date);
            days.Add(new DayPlan(date, scheduled.Hours, 0, [],
                Classify(scheduled.Hours, 0, unknown: true), scheduled.HitZeroFloor));
        }
        return new MonthPlan(days, true);
    }

    private static DayStatus Classify(double expected, double logged, bool unknown)
    {
        // NonWorking wins over Unknown: the schedule is known locally even when the fetch failed.
        if (expected <= Epsilon) return DayStatus.NonWorking;
        if (unknown) return DayStatus.Unknown;
        if (logged <= Epsilon) return DayStatus.Empty;
        if (logged > expected + Epsilon) return DayStatus.Over;
        if (logged >= expected - Epsilon) return DayStatus.Complete;
        return DayStatus.Partial;
    }

    /// <summary>Totals for one calendar month, excluding the grid's neighbouring-month days.</summary>
    public PlanTotals TotalsForMonth(int year, int month)
    {
        var days = Days
            .Where(d => d.Date.Year == year && d.Date.Month == month && d.Status != DayStatus.NonWorking)
            .ToList();

        return new PlanTotals(
            days.Sum(d => d.Expected),
            days.Sum(d => d.Logged),
            days.Where(d => d.Status != DayStatus.Unknown).Sum(d => d.Remaining));
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/7PaceDesktop.Tests/7PaceDesktop.Tests.csproj --filter "FullyQualifiedName~MonthPlanTests"`
Expected: PASS, 11 tests.

- [ ] **Step 6: Commit**

```bash
git add src/7PaceDesktop.Core/Planning/CalendarGrid.cs src/7PaceDesktop.Core/Planning/MonthPlan.cs tests/7PaceDesktop.Tests/MonthPlanTests.cs
git commit -m "feat: MonthPlan merging schedule with registered time"
```

---

### Task 4: FillPlanner

**Files:**
- Create: `src/7PaceDesktop.Core/Planning/FillPlanner.cs`
- Test: `tests/7PaceDesktop.Tests/FillPlannerTests.cs`

**Interfaces:**
- Consumes: `MonthPlan`, `DayPlan`, `DayStatus`, `CalendarGrid` (Task 3), `WorkSchedule` (Task 2), `TimeEntry` (already exists: `record TimeEntry(DateOnly Date, double Hours, int WorkItemId, bool HitZeroFloor = false)`).
- Produces:
  - `record FillLine(int WorkItemId, double Hours)`
  - `record FillSpec(IReadOnlyList<FillLine> Lines)` with `double Target { get; }` = the sum of the lines
  - `record FillSummary(int EmptyDays, int PartialDays, int SkippedDays, double TotalHours)`
  - `FillPlanner.Plan(IReadOnlySet<DateOnly> selection, MonthPlan plan, FillSpec spec) -> IReadOnlyList<TimeEntry>`
  - `FillPlanner.Summarize(IReadOnlySet<DateOnly> selection, MonthPlan plan, FillSpec spec) -> FillSummary`

- [ ] **Step 1: Write the failing tests**

`tests/7PaceDesktop.Tests/FillPlannerTests.cs`:

```csharp
using PaceDesktop.Core.Models;
using PaceDesktop.Core.Planning;

namespace PaceDesktop.Tests;

public class FillPlannerTests
{
    private const int Sprint = 12345;
    private const int Support = 12401;

    private static MonthPlan JunePlan(params ExistingWorkLog[] logs)
    {
        var schedule = new WorkSchedule(8, new HashSet<DateOnly> { new(2026, 6, 19) });
        var (from, to) = CalendarGrid.RangeFor(2026, 6);
        return MonthPlan.Build(from, to, schedule, logs);
    }

    private static ExistingWorkLog Log(int day, double hours) =>
        new($"w{day}", new DateOnly(2026, 6, day), hours, Sprint, null);

    private static IReadOnlySet<DateOnly> Days(params int[] days) =>
        new HashSet<DateOnly>(days.Select(d => new DateOnly(2026, 6, d)));

    private static FillSpec Single(double hours = 8) => new([new FillLine(Sprint, hours)]);

    [Fact]
    public void EmptyDay_IsFilledToTheTarget()
    {
        var entries = FillPlanner.Plan(Days(22), JunePlan(), Single());

        var entry = Assert.Single(entries);
        Assert.Equal(new DateOnly(2026, 6, 22), entry.Date);
        Assert.Equal(8, entry.Hours);
        Assert.Equal(Sprint, entry.WorkItemId);
    }

    [Fact]
    public void PartialDay_IsToppedUpByTheShortfallOnly()
    {
        var entries = FillPlanner.Plan(Days(24), JunePlan(Log(24, 3)), Single());

        Assert.Equal(5, Assert.Single(entries).Hours);
    }

    [Fact]
    public void CompleteAndOverDays_ProduceNothing()
    {
        var plan = JunePlan(Log(25, 8), Log(17, 9));

        Assert.Empty(FillPlanner.Plan(Days(25, 17), plan, Single()));
    }

    [Fact]
    public void NonWorkingAndUnknownDays_ProduceNothing()
    {
        // 6 June is a Saturday, 19 June is a holiday.
        Assert.Empty(FillPlanner.Plan(Days(6, 19), JunePlan(), Single()));

        var schedule = new WorkSchedule(8, new HashSet<DateOnly>());
        var (from, to) = CalendarGrid.RangeFor(2026, 6);
        Assert.Empty(FillPlanner.Plan(Days(22), MonthPlan.Unknown(from, to, schedule), Single()));
    }

    [Fact]
    public void PreHolidayDay_IsFilledToItsShortenedTarget()
    {
        // 18 June expects 5h because 19 June is a holiday; the spec target is still 8.
        var entries = FillPlanner.Plan(Days(18), JunePlan(), Single());

        var entry = Assert.Single(entries);
        Assert.Equal(5, entry.Hours);
        Assert.False(entry.HitZeroFloor);
    }

    [Fact]
    public void SplitLines_AreEmittedPerWorkItemOnAFullDay()
    {
        var spec = new FillSpec([new FillLine(Sprint, 6), new FillLine(Support, 2)]);

        var entries = FillPlanner.Plan(Days(22), JunePlan(), spec);

        Assert.Equal(2, entries.Count);
        Assert.Equal(6, entries.Single(e => e.WorkItemId == Sprint).Hours);
        Assert.Equal(2, entries.Single(e => e.WorkItemId == Support).Hours);
    }

    [Fact]
    public void SplitLines_ScaleProportionallyOnAPartialDay()
    {
        // 3h already logged, 5h remaining, split 6/2 -> 3.75 / 1.25.
        var spec = new FillSpec([new FillLine(Sprint, 6), new FillLine(Support, 2)]);

        var entries = FillPlanner.Plan(Days(24), JunePlan(Log(24, 3)), spec);

        Assert.Equal(3.75, entries.Single(e => e.WorkItemId == Sprint).Hours);
        Assert.Equal(1.25, entries.Single(e => e.WorkItemId == Support).Hours);
        Assert.Equal(5, entries.Sum(e => e.Hours));
    }

    [Fact]
    public void RoundingResidual_LandsOnTheLargestLineSoTheDaySumsExactly()
    {
        // 1h already logged leaves 7h; a three-way even split does not divide cleanly.
        var spec = new FillSpec([
            new FillLine(Sprint, 3),
            new FillLine(Support, 3),
            new FillLine(999, 3)
        ]);

        var entries = FillPlanner.Plan(Days(24), JunePlan(Log(24, 1)), spec);

        Assert.Equal(3, entries.Count);
        Assert.Equal(7, Math.Round(entries.Sum(e => e.Hours), 10));
        Assert.All(entries, e => Assert.True(e.Hours > 0));
    }

    [Fact]
    public void ZeroTargetSpec_ProducesNothing()
    {
        var spec = new FillSpec([new FillLine(Sprint, 0)]);

        Assert.Empty(FillPlanner.Plan(Days(22), JunePlan(), spec));
    }

    [Fact]
    public void ManyDays_AreAllPlanned_OrderedByDate()
    {
        var entries = FillPlanner.Plan(Days(26, 22, 23), JunePlan(), Single());

        Assert.Equal(3, entries.Count);
        Assert.Equal([22, 23, 26], entries.Select(e => e.Date.Day));
        Assert.Equal(24, entries.Sum(e => e.Hours));
    }

    [Fact]
    public void Summarize_CountsEachDayKindAndTotalsTheHours()
    {
        // 22, 23, 26 empty; 24 partial with 3h logged; 25 already complete.
        var plan = JunePlan(Log(24, 3), Log(25, 8));

        var summary = FillPlanner.Summarize(Days(22, 23, 24, 25, 26), plan, Single());

        Assert.Equal(3, summary.EmptyDays);
        Assert.Equal(1, summary.PartialDays);
        Assert.Equal(1, summary.SkippedDays);
        Assert.Equal(29, summary.TotalHours);
    }

    [Fact]
    public void Summarize_TotalMatchesWhatPlanWouldPost()
    {
        var plan = JunePlan(Log(24, 3), Log(25, 8));
        var spec = new FillSpec([new FillLine(Sprint, 6), new FillLine(Support, 2)]);
        var selection = Days(22, 23, 24, 25, 26);

        var summary = FillPlanner.Summarize(selection, plan, spec);
        var planned = FillPlanner.Plan(selection, plan, spec);

        Assert.Equal(summary.TotalHours, Math.Round(planned.Sum(e => e.Hours), 2));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/7PaceDesktop.Tests/7PaceDesktop.Tests.csproj --filter "FullyQualifiedName~FillPlannerTests"`
Expected: compile errors — `FillPlanner`, `FillSpec`, `FillLine` do not exist.

- [ ] **Step 3: Implement**

`src/7PaceDesktop.Core/Planning/FillPlanner.cs`:

```csharp
using PaceDesktop.Core.Models;

namespace PaceDesktop.Core.Planning;

/// <summary>One work item and the hours it takes out of a full day's target.</summary>
public sealed record FillLine(int WorkItemId, double Hours);

/// <summary>How a full day's hours are split across work items.</summary>
public sealed record FillSpec(IReadOnlyList<FillLine> Lines)
{
    /// <summary>The full-day total the lines describe. The UI requires this to equal the daily target.</summary>
    public double Target => Lines.Sum(l => l.Hours);
}

public sealed record FillSummary(int EmptyDays, int PartialDays, int SkippedDays, double TotalHours);

/// <summary>
/// Turns a set of selected dates plus a fill spec into the entries to post. This is the only
/// place the split and rounding rules exist — never reimplement them in the front end.
/// </summary>
public static class FillPlanner
{
    private const double Epsilon = MonthPlan.Epsilon;

    public static IReadOnlyList<TimeEntry> Plan(
        IReadOnlySet<DateOnly> selection, MonthPlan plan, FillSpec spec)
    {
        var entries = new List<TimeEntry>();
        if (spec.Target <= Epsilon) return entries;

        foreach (var date in selection.OrderBy(d => d))
        {
            if (!TryRemaining(plan, date, out var day, out var remaining)) continue;

            var scale = remaining / spec.Target;
            var hours = spec.Lines.Select(l => Math.Round(l.Hours * scale, 2)).ToArray();

            // Put the rounding residual on the largest line so the day sums to exactly `remaining`.
            var residual = remaining - hours.Sum();
            if (Math.Abs(residual) > Epsilon / 2)
            {
                var largest = 0;
                for (var i = 1; i < hours.Length; i++)
                    if (hours[i] > hours[largest]) largest = i;
                hours[largest] = Math.Round(hours[largest] + residual, 2);
            }

            for (var i = 0; i < spec.Lines.Count; i++)
                if (hours[i] > Epsilon)
                    entries.Add(new TimeEntry(date, hours[i], spec.Lines[i].WorkItemId, day.HitZeroFloor));
        }

        return entries;
    }

    public static FillSummary Summarize(
        IReadOnlySet<DateOnly> selection, MonthPlan plan, FillSpec spec)
    {
        int empty = 0, partial = 0, skipped = 0;
        double total = 0;

        foreach (var date in selection)
        {
            if (plan.Day(date) is not { } day) continue;
            if (day.Status is DayStatus.NonWorking or DayStatus.Unknown) continue;

            if (day.Remaining <= Epsilon) { skipped++; continue; }

            if (day.Status == DayStatus.Empty) empty++; else partial++;
            total += day.Remaining;
        }

        return new FillSummary(empty, partial, skipped, Math.Round(total, 2));
    }

    private static bool TryRemaining(MonthPlan plan, DateOnly date, out DayPlan day, out double remaining)
    {
        day = default!;
        remaining = 0;

        if (plan.Day(date) is not { } found) return false;
        if (found.Status is DayStatus.NonWorking or DayStatus.Unknown) return false;
        if (found.Remaining <= Epsilon) return false;

        day = found;
        remaining = found.Remaining;
        return true;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/7PaceDesktop.Tests/7PaceDesktop.Tests.csproj --filter "FullyQualifiedName~FillPlannerTests"`
Expected: PASS, 12 tests.

- [ ] **Step 5: Commit**

```bash
git add src/7PaceDesktop.Core/Planning/FillPlanner.cs tests/7PaceDesktop.Tests/FillPlannerTests.cs
git commit -m "feat: FillPlanner tops days up to target without duplicating logged time"
```

---

### Task 5: DailyHours setting and migration

**Files:**
- Modify: `src/7PaceDesktop.Core/Storage/AppSettings.cs`
- Modify: `src/7PaceDesktop.App/ViewModels/MainViewModel.cs:41` and `:79-81` (keep the WPF app compiling)
- Test: `tests/7PaceDesktop.Tests/StorageTests.cs`

**Interfaces:**
- Consumes: `AppSettings`, `SettingsStore` (both exist).
- Produces: `AppSettings.DailyHours` (double, default 8). `LastDailyHours` remains only as a read-time migration shim and is never written.

- [ ] **Step 1: Write the failing tests**

Append to `tests/7PaceDesktop.Tests/StorageTests.cs`:

```csharp
    [Fact]
    public void Settings_DefaultDailyHoursIsEight()
    {
        Assert.Equal(8, new AppSettings().DailyHours);
    }

    [Fact]
    public void Settings_MigratesLastDailyHoursFromAnOlderFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "7pace-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "settings.json"),
            """{"OrganizationName":"icore","LastDailyHours":6,"Theme":"Dark"}""");

        var settings = new SettingsStore(dir).Load();

        Assert.Equal(6, settings.DailyHours);
        Assert.Equal("icore", settings.OrganizationName);
        Assert.Equal(ThemePreference.Dark, settings.Theme);

        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Settings_DoesNotWriteTheLegacyProperty()
    {
        var dir = Path.Combine(Path.GetTempPath(), "7pace-tests", Guid.NewGuid().ToString("N"));
        var store = new SettingsStore(dir);

        store.Save(new AppSettings { OrganizationName = "icore", DailyHours = 7 });
        var json = File.ReadAllText(Path.Combine(dir, "settings.json"));

        Assert.Contains("\"DailyHours\": 7", json);
        Assert.DoesNotContain("LastDailyHours", json);

        Directory.Delete(dir, recursive: true);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/7PaceDesktop.Tests/7PaceDesktop.Tests.csproj --filter "FullyQualifiedName~StorageTests"`
Expected: compile error — `AppSettings.DailyHours` does not exist.

- [ ] **Step 3: Implement the setting and the shim**

Replace the `LastDailyHours` property in `src/7PaceDesktop.Core/Storage/AppSettings.cs` with:

```csharp
    /// <summary>The user's daily target, applied to every workday. Persisted, not a remembered input.</summary>
    public double DailyHours { get; set; } = 8;

    /// <summary>
    /// Migration shim for settings files written before DailyHours existed. Read-only: the getter
    /// returns null so the property is never written back, and the setter forwards to DailyHours.
    /// </summary>
    [JsonPropertyName("LastDailyHours")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? LastDailyHours
    {
        get => null;
        set { if (value is { } hours && hours > 0) DailyHours = hours; }
    }
```

- [ ] **Step 4: Keep the WPF app compiling**

In `src/7PaceDesktop.App/ViewModels/MainViewModel.cs`, replace the constructor line
`HoursPerDay = settingsStore.Load().LastDailyHours;` with:

```csharp
        HoursPerDay = settingsStore.Load().DailyHours;
```

and in `GenerateAsync`, replace `settings.LastDailyHours = HoursPerDay;` with:

```csharp
        settings.DailyHours = HoursPerDay;
```

- [ ] **Step 5: Run the whole suite**

Run: `dotnet test tests/7PaceDesktop.Tests/7PaceDesktop.Tests.csproj`
Expected: PASS. The three new storage tests are green and nothing regressed.

- [ ] **Step 6: Commit**

```bash
git add src/7PaceDesktop.Core/Storage/AppSettings.cs src/7PaceDesktop.App/ViewModels/MainViewModel.cs tests/7PaceDesktop.Tests/StorageTests.cs
git commit -m "feat: DailyHours setting with migration from LastDailyHours"
```

---

### Task 6: Server project, security filter and static hosting

**Files:**
- Create: `src/7PaceDesktop.Server/7PaceDesktop.Server.csproj`, `Program.cs`, `PaceClientFactory.cs`, `ClientHeaderFilter.cs`, `Endpoints/Stubs.cs`
- Modify: `7PaceDesktop.slnx`, `tests/7PaceDesktop.Tests/7PaceDesktop.Tests.csproj`
- Test: `tests/7PaceDesktop.Tests/ServerFixture.cs`, `tests/7PaceDesktop.Tests/ServerSmokeTests.cs`

**Interfaces:**
- Consumes: `SettingsStore`, `WorkItemStore`, `CredentialStore`, `AppPaths`, `SwedishHolidayService`, `PaceApiClient`, `IWorkLogClient`, `IWorkLogReader`.
- Produces:
  - `interface IPaceClientFactory { IWorkLogReader CreateReader(); IWorkLogClient CreateClient(); }`
  - `interface ITokenSource { string? Load(string organization); void Save(string organization, string token); }`
  - `CredentialTokenSource(CredentialStore)`, `PaceClientFactory(HttpClient, SettingsStore, ITokenSource)`
  - `ClientHeaderFilter` with `const string HeaderName = "X-Pace-Client"`
  - `public partial class Program` so `WebApplicationFactory<Program>` can host it
  - `ServerFixture` exposing `Client`, `CreateBareClient()`, `Pace`, `Settings`, `WorkItems`, `DataDir`; `FakePace`; `FakeTokenSource`

- [ ] **Step 1: Create the server project and add it to the solution**

`src/7PaceDesktop.Server/7PaceDesktop.Server.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <RootNamespace>PaceDesktop.Server</RootNamespace>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\7PaceDesktop.Core\7PaceDesktop.Core.csproj" />
  </ItemGroup>

</Project>
```

Run: `dotnet sln 7PaceDesktop.slnx add src/7PaceDesktop.Server/7PaceDesktop.Server.csproj`

- [ ] **Step 2: Wire the test project to the server**

In `tests/7PaceDesktop.Tests/7PaceDesktop.Tests.csproj`, add to the existing `PackageReference` group:

```xml
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.0" />
```

and to the existing `ProjectReference` group:

```xml
    <ProjectReference Include="..\..\src\7PaceDesktop.Server\7PaceDesktop.Server.csproj" />
```

- [ ] **Step 3: Write the test fixture**

`tests/7PaceDesktop.Tests/ServerFixture.cs`:

```csharp
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using PaceDesktop.Core.Models;
using PaceDesktop.Core.Services;
using PaceDesktop.Core.Storage;
using PaceDesktop.Server;

namespace PaceDesktop.Tests;

/// <summary>A fake 7Pace whose reads and writes are scripted by the test.</summary>
public sealed class FakePace : IPaceClientFactory, IWorkLogReader, IWorkLogClient
{
    public List<ExistingWorkLog> Existing { get; } = [];
    public List<TimeEntry> Submitted { get; } = [];
    public Exception? ReadThrows { get; set; }
    public Func<TimeEntry, Exception?>? SubmitThrows { get; set; }
    public int ReadCount;

    public IWorkLogReader CreateReader() => this;
    public IWorkLogClient CreateClient() => this;

    public Task<IReadOnlyList<ExistingWorkLog>> GetWorkLogsAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        Interlocked.Increment(ref ReadCount);
        if (ReadThrows is not null) throw ReadThrows;
        return Task.FromResult<IReadOnlyList<ExistingWorkLog>>(
            Existing.Where(l => l.Date >= from && l.Date <= to).ToList());
    }

    public Task SubmitAsync(TimeEntry entry, CancellationToken ct = default)
    {
        if (SubmitThrows?.Invoke(entry) is { } ex) throw ex;
        lock (Submitted) Submitted.Add(entry);
        return Task.CompletedTask;
    }
}

/// <summary>In-memory token store, so no test touches Windows Credential Manager.</summary>
public sealed class FakeTokenSource : ITokenSource
{
    private readonly Dictionary<string, string> _tokens = [];

    public string? Load(string organization) =>
        _tokens.TryGetValue(organization, out var token) ? token : "test-token";

    public void Save(string organization, string token) => _tokens[organization] = token;
}

/// <summary>
/// Hosts the real server against a per-test data directory and a fake 7Pace, so no test
/// touches the user's real settings, work items or credential store.
/// </summary>
public sealed class ServerFixture : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;

    public string DataDir { get; }
    public FakePace Pace { get; } = new();
    public HttpClient Client { get; }
    public SettingsStore Settings { get; }
    public WorkItemStore WorkItems { get; }

    public ServerFixture()
    {
        DataDir = Path.Combine(Path.GetTempPath(), "7pace-server-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DataDir);
        Settings = new SettingsStore(DataDir);
        WorkItems = new WorkItemStore(DataDir);

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("DataDir", DataDir);
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IPaceClientFactory>(Pace);
                services.AddSingleton<ITokenSource>(new FakeTokenSource());
            });
        });

        Client = _factory.CreateClient();
        Client.DefaultRequestHeaders.Add(ClientHeaderFilter.HeaderName, "1");
    }

    /// <summary>A client without the anti-CSRF header, to prove mutating endpoints reject it.</summary>
    public HttpClient CreateBareClient() => _factory.CreateClient();

    public void Dispose()
    {
        Client.Dispose();
        _factory.Dispose();
        try { Directory.Delete(DataDir, recursive: true); } catch (IOException) { }
    }
}
```

- [ ] **Step 4: Write the failing smoke tests**

`tests/7PaceDesktop.Tests/ServerSmokeTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;

namespace PaceDesktop.Tests;

public class ServerSmokeTests
{
    [Fact]
    public async Task Health_IsReachable()
    {
        using var server = new ServerFixture();

        var response = await server.Client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ok", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task MutatingEndpoint_RejectsRequestsWithoutTheClientHeader()
    {
        // A page on another origin cannot set a custom header without a preflight, and no CORS
        // policy is configured, so this header is what keeps the local API private to the SPA.
        using var server = new ServerFixture();
        using var bare = server.CreateBareClient();

        var response = await bare.PutAsJsonAsync("/api/config",
            new { organization = "icore", token = (string?)null, dailyHours = 8.0, theme = "System" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ReadEndpoint_DoesNotRequireTheClientHeader()
    {
        using var server = new ServerFixture();
        using var bare = server.CreateBareClient();

        Assert.Equal(HttpStatusCode.OK, (await bare.GetAsync("/api/health")).StatusCode);
    }
}
```

- [ ] **Step 5: Run the tests to verify they fail**

Run: `dotnet test tests/7PaceDesktop.Tests/7PaceDesktop.Tests.csproj --filter "FullyQualifiedName~ServerSmokeTests"`
Expected: compile errors — `Program`, `IPaceClientFactory`, `ITokenSource` and `ClientHeaderFilter` do not exist.

- [ ] **Step 6: Implement the client factory and token source**

`src/7PaceDesktop.Server/PaceClientFactory.cs`:

```csharp
using PaceDesktop.Core.Services;
using PaceDesktop.Core.Storage;

namespace PaceDesktop.Server;

/// <summary>
/// Builds 7Pace clients from the CURRENT settings and stored token, so changing the
/// organization or token in the UI takes effect without restarting the server.
/// </summary>
public interface IPaceClientFactory
{
    IWorkLogReader CreateReader();
    IWorkLogClient CreateClient();
}

/// <summary>
/// Reads and writes the 7Pace token. Production uses Windows Credential Manager; tests
/// substitute an in-memory one so no test writes to the developer's real credential store.
/// </summary>
public interface ITokenSource
{
    string? Load(string organization);
    void Save(string organization, string token);
}

public sealed class CredentialTokenSource(CredentialStore credentials) : ITokenSource
{
    public string? Load(string organization) =>
        string.IsNullOrWhiteSpace(organization) ? null : credentials.LoadToken(organization);

    public void Save(string organization, string token) => credentials.SaveToken(organization, token);
}

public sealed class PaceClientFactory(HttpClient http, SettingsStore settings, ITokenSource tokens)
    : IPaceClientFactory
{
    public IWorkLogReader CreateReader() => Build();
    public IWorkLogClient CreateClient() => Build();

    private PaceApiClient Build()
    {
        var current = settings.Load();
        return new PaceApiClient(http, current.OrganizationName,
            tokens.Load(current.OrganizationName) ?? string.Empty);
    }
}
```

Check `CredentialStore`'s method names before compiling; if the save method is not
`SaveToken(string, string)`, use the name it actually has.

- [ ] **Step 7: Implement the security filter**

`src/7PaceDesktop.Server/ClientHeaderFilter.cs`:

```csharp
namespace PaceDesktop.Server;

/// <summary>
/// Requires a custom header on mutating endpoints. Combined with binding to 127.0.0.1 and
/// configuring no CORS policy, this stops a page on another origin from reaching the local
/// API: a custom header forces a preflight, and without a CORS policy the browser refuses it.
/// </summary>
public sealed class ClientHeaderFilter : IEndpointFilter
{
    public const string HeaderName = "X-Pace-Client";

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (context.HttpContext.Request.Headers[HeaderName].ToString() != "1")
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        return await next(context);
    }
}
```

- [ ] **Step 8: Implement the endpoint stubs**

Tasks 7 to 9 replace these one at a time. They exist now so this task compiles alone, and the
config `PUT` carries its filter already so the security test is meaningful.

`src/7PaceDesktop.Server/Endpoints/Stubs.cs`:

```csharp
namespace PaceDesktop.Server;

// Each method here is replaced by a real endpoint group in Tasks 7, 8 and 9.
public static class EndpointStubs
{
    public static void MapConfigEndpoints(this WebApplication app) =>
        app.MapPut("/api/config", () => Results.Ok()).AddEndpointFilter<ClientHeaderFilter>();

    public static void MapMonthEndpoints(this WebApplication app) { }

    public static void MapRegisterEndpoints(this WebApplication app) { }
}
```

- [ ] **Step 9: Implement Program.cs**

`src/7PaceDesktop.Server/Program.cs`:

```csharp
using PaceDesktop.Core.Services;
using PaceDesktop.Core.Storage;
using PaceDesktop.Server;

var builder = WebApplication.CreateBuilder(args);

// Tests point this at a temp directory; production uses %AppData%\7PaceDesktop.
var dataDir = builder.Configuration["DataDir"] ?? AppPaths.DefaultBaseDir;

builder.Services.AddSingleton(new HttpClient());
builder.Services.AddSingleton(new SettingsStore(dataDir));
builder.Services.AddSingleton(new WorkItemStore(dataDir));
builder.Services.AddSingleton<CredentialStore>();
builder.Services.AddSingleton<ITokenSource, CredentialTokenSource>();
builder.Services.AddSingleton(sp => new SwedishHolidayService(
    sp.GetRequiredService<HttpClient>(), sp.GetRequiredService<SettingsStore>()));
builder.Services.AddSingleton<IPaceClientFactory, PaceClientFactory>();

var app = builder.Build();

// No CORS policy is configured on purpose - see ClientHeaderFilter.
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/health", () => Results.Text("ok"));
app.MapConfigEndpoints();
app.MapMonthEndpoints();
app.MapRegisterEndpoints();

app.MapFallbackToFile("index.html");

app.Run();

/// <summary>Named entry point so WebApplicationFactory&lt;Program&gt; can host the app in tests.</summary>
public partial class Program;
```

- [ ] **Step 10: Create wwwroot so static hosting has a root**

Run: `mkdir -p src/7PaceDesktop.Server/wwwroot && touch src/7PaceDesktop.Server/wwwroot/.gitkeep`

The Vite build fills this directory in Task 10.

- [ ] **Step 11: Run the tests to verify they pass**

Run: `dotnet test tests/7PaceDesktop.Tests/7PaceDesktop.Tests.csproj --filter "FullyQualifiedName~ServerSmokeTests"`
Expected: PASS, 3 tests.

- [ ] **Step 12: Run the whole suite**

Run: `dotnet test tests/7PaceDesktop.Tests/7PaceDesktop.Tests.csproj`
Expected: PASS.

- [ ] **Step 13: Commit**

```bash
git add 7PaceDesktop.slnx src/7PaceDesktop.Server tests/7PaceDesktop.Tests
git commit -m "feat: local API server with localhost binding and CSRF header guard"
```

---

### Task 7: Config and work item endpoints

**Files:**
- Create: `src/7PaceDesktop.Server/Contracts.cs`, `src/7PaceDesktop.Server/Endpoints/ConfigEndpoints.cs`
- Modify: `src/7PaceDesktop.Server/Endpoints/Stubs.cs` (drop `MapConfigEndpoints`)
- Test: `tests/7PaceDesktop.Tests/ConfigEndpointTests.cs`

**Interfaces:**
- Consumes: `ServerFixture`, `FakeTokenSource`, `ClientHeaderFilter`, `ITokenSource`, `SettingsStore`, `WorkItemStore`, `AppSettings`, `ThemePreference`, `WorkItem`, `PaceApiClient.NormalizeAccount`.
- Produces:
  - `record ConfigDto(bool Configured, string Organization, double DailyHours, string Theme, bool HasToken)`
  - `record ConfigUpdateDto(string Organization, string? Token, double DailyHours, string Theme)`
  - `record WorkItemDto(int Id, string Name, bool IsFavorite)`
  - `ConfigEndpoints.MapConfigEndpoints(this WebApplication app)` mapping `GET`/`PUT /api/config` and `GET`/`PUT /api/workitems`

- [ ] **Step 1: Write the failing tests**

`tests/7PaceDesktop.Tests/ConfigEndpointTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using PaceDesktop.Core.Models;
using PaceDesktop.Core.Storage;
using PaceDesktop.Server;

namespace PaceDesktop.Tests;

public class ConfigEndpointTests
{
    [Fact]
    public async Task GetConfig_ReportsNotConfiguredOnAFreshInstall()
    {
        using var server = new ServerFixture();

        var config = await server.Client.GetFromJsonAsync<ConfigDto>("/api/config");

        Assert.NotNull(config);
        Assert.False(config.Configured);
        Assert.Equal(string.Empty, config.Organization);
        Assert.Equal(8, config.DailyHours);
        Assert.Equal("System", config.Theme);
    }

    [Fact]
    public async Task GetConfig_NeverReturnsTheToken()
    {
        using var server = new ServerFixture();
        server.Settings.Save(new AppSettings { OrganizationName = "icore", DailyHours = 7 });

        var body = await server.Client.GetStringAsync("/api/config");

        Assert.DoesNotContain("test-token", body);
        Assert.DoesNotContain("\"token\"", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PutConfig_PersistsOrganizationDailyHoursAndTheme()
    {
        using var server = new ServerFixture();

        var response = await server.Client.PutAsJsonAsync("/api/config",
            new ConfigUpdateDto("icore", null, 6, "Dark"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = server.Settings.Load();
        Assert.Equal("icore", saved.OrganizationName);
        Assert.Equal(6, saved.DailyHours);
        Assert.Equal(ThemePreference.Dark, saved.Theme);
    }

    [Fact]
    public async Task PutConfig_NormalisesAPastedUrlToTheAccountLabel()
    {
        using var server = new ServerFixture();

        await server.Client.PutAsJsonAsync("/api/config",
            new ConfigUpdateDto("https://icore.timehub.7pace.com/api", null, 8, "System"));

        Assert.Equal("icore", server.Settings.Load().OrganizationName);
    }

    [Fact]
    public async Task PutConfig_RejectsAnInvalidOrganization()
    {
        using var server = new ServerFixture();

        var response = await server.Client.PutAsJsonAsync("/api/config",
            new ConfigUpdateDto("iCore v3", null, 8, "System"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(25)]
    public async Task PutConfig_RejectsAnImpossibleDailyTarget(double hours)
    {
        using var server = new ServerFixture();

        var response = await server.Client.PutAsJsonAsync("/api/config",
            new ConfigUpdateDto("icore", null, hours, "System"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PutConfig_RejectsAnUnknownTheme()
    {
        using var server = new ServerFixture();

        var response = await server.Client.PutAsJsonAsync("/api/config",
            new ConfigUpdateDto("icore", null, 8, "Neon"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PutWorkItems_RequiresExactlyOneFavourite()
    {
        using var server = new ServerFixture();

        var none = await server.Client.PutAsJsonAsync("/api/workitems",
            new[] { new WorkItemDto(1, "A", false), new WorkItemDto(2, "B", false) });
        var two = await server.Client.PutAsJsonAsync("/api/workitems",
            new[] { new WorkItemDto(1, "A", true), new WorkItemDto(2, "B", true) });

        Assert.Equal(HttpStatusCode.BadRequest, none.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, two.StatusCode);
    }

    [Fact]
    public async Task PutWorkItems_RejectsAnEmptyList()
    {
        using var server = new ServerFixture();

        var response = await server.Client.PutAsJsonAsync("/api/workitems", Array.Empty<WorkItemDto>());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PutWorkItems_RejectsDuplicateIds()
    {
        using var server = new ServerFixture();

        var response = await server.Client.PutAsJsonAsync("/api/workitems",
            new[] { new WorkItemDto(1, "A", true), new WorkItemDto(1, "A again", false) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PutWorkItems_ThenGetWorkItems_RoundTrips()
    {
        using var server = new ServerFixture();

        await server.Client.PutAsJsonAsync("/api/workitems",
            new[] { new WorkItemDto(12345, "Sprintarbete", true), new WorkItemDto(12401, "Support", false) });
        var items = await server.Client.GetFromJsonAsync<List<WorkItemDto>>("/api/workitems");

        Assert.NotNull(items);
        Assert.Equal([12345, 12401], items.Select(i => i.Id));
        Assert.Single(items, i => i.IsFavorite);
        Assert.Equal([12345, 12401], server.WorkItems.Load().Select(i => i.Id));
    }

    [Fact]
    public async Task GetConfig_IsConfiguredOnceOrganizationTokenAndWorkItemsExist()
    {
        using var server = new ServerFixture();
        server.Settings.Save(new AppSettings { OrganizationName = "icore" });
        server.WorkItems.Save([new WorkItem(1, "A", true)]);

        var config = await server.Client.GetFromJsonAsync<ConfigDto>("/api/config");

        Assert.True(config!.Configured);
        Assert.True(config.HasToken);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/7PaceDesktop.Tests/7PaceDesktop.Tests.csproj --filter "FullyQualifiedName~ConfigEndpointTests"`
Expected: compile errors — `ConfigDto`, `ConfigUpdateDto` and `WorkItemDto` do not exist.

- [ ] **Step 3: Write the contracts**

`src/7PaceDesktop.Server/Contracts.cs`:

```csharp
namespace PaceDesktop.Server;

/// <summary>Wire types. None of these ever carries the 7Pace token.</summary>
public sealed record ConfigDto(bool Configured, string Organization, double DailyHours, string Theme, bool HasToken);

public sealed record ConfigUpdateDto(string Organization, string? Token, double DailyHours, string Theme);

public sealed record WorkItemDto(int Id, string Name, bool IsFavorite);
```

- [ ] **Step 4: Implement the endpoints**

`src/7PaceDesktop.Server/Endpoints/ConfigEndpoints.cs`:

```csharp
using PaceDesktop.Core.Models;
using PaceDesktop.Core.Services;
using PaceDesktop.Core.Storage;

namespace PaceDesktop.Server;

public static class ConfigEndpoints
{
    private const double MaxDailyHours = 24;

    public static void MapConfigEndpoints(this WebApplication app)
    {
        app.MapGet("/api/config", (SettingsStore store, WorkItemStore items, ITokenSource tokens) =>
        {
            var settings = store.Load();
            var hasToken = !string.IsNullOrWhiteSpace(settings.OrganizationName)
                           && !string.IsNullOrWhiteSpace(tokens.Load(settings.OrganizationName));

            return Results.Ok(new ConfigDto(
                Configured: hasToken && items.Load().Count > 0,
                Organization: settings.OrganizationName,
                DailyHours: settings.DailyHours,
                Theme: settings.Theme.ToString(),
                HasToken: hasToken));
        });

        app.MapPut("/api/config", (ConfigUpdateDto body, SettingsStore store, ITokenSource tokens) =>
        {
            string account;
            try
            {
                account = PaceApiClient.NormalizeAccount(body.Organization);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }

            if (body.DailyHours is <= 0 or > MaxDailyHours)
                return Results.BadRequest(new { error = "Timmar per dag måste vara mellan 0 och 24." });

            if (!Enum.TryParse<ThemePreference>(body.Theme, ignoreCase: true, out var theme))
                return Results.BadRequest(new { error = $"Okänt tema '{body.Theme}'." });

            var settings = store.Load();
            settings.OrganizationName = account;
            settings.DailyHours = body.DailyHours;
            settings.Theme = theme;
            store.Save(settings);

            // An omitted token means "leave the stored one alone", so the settings view never
            // has to round-trip a secret it is not allowed to read.
            if (!string.IsNullOrWhiteSpace(body.Token)) tokens.Save(account, body.Token);

            return Results.Ok();
        }).AddEndpointFilter<ClientHeaderFilter>();

        app.MapGet("/api/workitems", (WorkItemStore items) =>
            Results.Ok(items.Load().Select(i => new WorkItemDto(i.Id, i.Name, i.IsFavorite))));

        app.MapPut("/api/workitems", (List<WorkItemDto> body, WorkItemStore items) =>
        {
            if (body.Count == 0)
                return Results.BadRequest(new { error = "Minst ett work item krävs." });
            if (body.Count(i => i.IsFavorite) != 1)
                return Results.BadRequest(new { error = "Exakt ett work item måste vara favorit." });
            if (body.Any(i => i.Id <= 0))
                return Results.BadRequest(new { error = "Work item-ID måste vara positivt." });
            if (body.Select(i => i.Id).Distinct().Count() != body.Count)
                return Results.BadRequest(new { error = "Samma work item förekommer flera gånger." });

            items.Save(body.Select(i => new WorkItem(i.Id, i.Name, i.IsFavorite)));
            return Results.Ok();
        }).AddEndpointFilter<ClientHeaderFilter>();
    }
}
```

- [ ] **Step 5: Drop the config stub**

In `src/7PaceDesktop.Server/Endpoints/Stubs.cs`, delete `MapConfigEndpoints`, keeping the month
and register stubs.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/7PaceDesktop.Tests/7PaceDesktop.Tests.csproj --filter "FullyQualifiedName~ConfigEndpointTests"`
Expected: PASS, 13 tests.

- [ ] **Step 7: Run the whole suite**

Run: `dotnet test tests/7PaceDesktop.Tests/7PaceDesktop.Tests.csproj`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/7PaceDesktop.Server tests/7PaceDesktop.Tests
git commit -m "feat: config and work item endpoints with the token kept server-side"
```

---

### Task 8: Month endpoint

**Files:**
- Modify: `src/7PaceDesktop.Core/Services/SwedishHolidayService.cs` (holiday names)
- Modify: `src/7PaceDesktop.Server/Contracts.cs`
- Create: `src/7PaceDesktop.Server/Endpoints/MonthEndpoints.cs`
- Modify: `src/7PaceDesktop.Server/Endpoints/Stubs.cs` (drop `MapMonthEndpoints`)
- Test: `tests/7PaceDesktop.Tests/MonthEndpointTests.cs`

**Interfaces:**
- Consumes: `CalendarGrid.RangeFor`, `CalendarGrid.IsoWeek`, `WorkSchedule`, `MonthPlan.Build`, `MonthPlan.Unknown`, `MonthPlan.TotalsForMonth`, `DayStatus`, `IPaceClientFactory.CreateReader`, `SwedishHolidayService.GetHolidaysAsync`, `SettingsStore`, `WorkItemStore`.
- Produces:
  - `HolidayLookup` gains `IReadOnlyDictionary<DateOnly, string>? Names` as a third, defaulted parameter, so existing construction sites keep compiling.
  - `record ExistingWorkLogDto(string Id, double Hours, int WorkItemId, string? WorkItemName, string? Comment)`
  - `record DayDto(string Date, double Expected, double Logged, double Remaining, string Status, bool HitZeroFloor, int IsoWeek, bool InMonth, string? HolidayName, IReadOnlyList<ExistingWorkLogDto> Existing)`
  - `record TotalsDto(double Expected, double Logged, double Missing)`
  - `record MonthDto(int Year, int Month, string From, string To, string LoadState, string? Error, string? HolidayWarning, DateTimeOffset FetchedAt, double DailyHours, TotalsDto Totals, IReadOnlyList<DayDto> Days)`
  - `MonthEndpoints.MapMonthEndpoints(this WebApplication app)` mapping `GET /api/month?year=&month=`
  - `MonthEndpoints.BuildMonth(...)` is not public; the register endpoint rebuilds its own plan in Task 9.

`LoadState` is the lowercase string `"loaded"` or `"failed"`. `Status` is the camel-case
`DayStatus` name lowercased: `nonWorking`, `empty`, `partial`, `complete`, `over`, `unknown`.

- [ ] **Step 1: Write the failing tests**

`tests/7PaceDesktop.Tests/MonthEndpointTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using PaceDesktop.Core.Models;
using PaceDesktop.Core.Storage;
using PaceDesktop.Server;

namespace PaceDesktop.Tests;

public class MonthEndpointTests
{
    private static ServerFixture Configured(double dailyHours = 8)
    {
        var server = new ServerFixture();
        server.Settings.Save(new AppSettings { OrganizationName = "icore", DailyHours = dailyHours });
        server.WorkItems.Save([new WorkItem(12345, "Sprintarbete", true), new WorkItem(12401, "Support", false)]);
        return server;
    }

    private static ExistingWorkLog Log(int day, double hours, int workItemId = 12345) =>
        new($"w{day}-{workItemId}", new DateOnly(2026, 6, day), hours, workItemId, null);

    private static DayDto Day(MonthDto month, int day) =>
        month.Days.Single(d => d.Date == $"2026-06-{day:00}");

    [Fact]
    public async Task GetMonth_ReturnsTheWholeGridRangeAndDailyTarget()
    {
        using var server = Configured();

        var month = await server.Client.GetFromJsonAsync<MonthDto>("/api/month?year=2026&month=6");

        Assert.NotNull(month);
        Assert.Equal("loaded", month.LoadState);
        Assert.Equal("2026-06-01", month.From);
        Assert.Equal("2026-07-05", month.To);
        Assert.Equal(35, month.Days.Count);
        Assert.Equal(8, month.DailyHours);
    }

    [Fact]
    public async Task GetMonth_ReportsLoggedHoursAndStatusPerDay()
    {
        using var server = Configured();
        server.Pace.Existing.AddRange([Log(3, 6), Log(4, 8), Log(17, 5), Log(17, 4)]);

        var month = await server.Client.GetFromJsonAsync<MonthDto>("/api/month?year=2026&month=6");

        Assert.Equal("partial", Day(month!, 3).Status);
        Assert.Equal(6, Day(month!, 3).Logged);
        Assert.Equal(2, Day(month!, 3).Remaining);
        Assert.Equal("complete", Day(month!, 4).Status);
        Assert.Equal("over", Day(month!, 17).Status);
        Assert.Equal("empty", Day(month!, 5).Status);
        Assert.Equal("nonWorking", Day(month!, 6).Status);   // Saturday
    }

    [Fact]
    public async Task GetMonth_NamesKnownWorkItemsAndLeavesUnknownOnesNull()
    {
        using var server = Configured();
        server.Pace.Existing.AddRange([Log(3, 4, 12345), Log(3, 2, 99999)]);

        var month = await server.Client.GetFromJsonAsync<MonthDto>("/api/month?year=2026&month=6");

        var existing = Day(month!, 3).Existing;
        Assert.Equal("Sprintarbete", existing.Single(e => e.WorkItemId == 12345).WorkItemName);
        Assert.Null(existing.Single(e => e.WorkItemId == 99999).WorkItemName);
    }

    [Fact]
    public async Task GetMonth_MarksAdjacentMonthDaysAndCarriesIsoWeekNumbers()
    {
        using var server = Configured();

        var month = await server.Client.GetFromJsonAsync<MonthDto>("/api/month?year=2026&month=8");

        var july = month!.Days.Single(d => d.Date == "2026-07-27");
        Assert.False(july.InMonth);
        Assert.Equal(31, july.IsoWeek);
        Assert.True(month.Days.Single(d => d.Date == "2026-08-03").InMonth);
    }

    [Fact]
    public async Task GetMonth_TotalsCoverTheMonthOnly()
    {
        using var server = Configured();
        server.Pace.Existing.Add(Log(3, 6));

        var month = await server.Client.GetFromJsonAsync<MonthDto>("/api/month?year=2026&month=6");

        // June 2026 has 22 weekdays and, with no holiday data available in tests, no holidays.
        Assert.Equal(176, month!.Totals.Expected);
        Assert.Equal(6, month.Totals.Logged);
        Assert.Equal(170, month.Totals.Missing);
    }

    [Fact]
    public async Task GetMonth_WhenTheFetchFails_ReturnsFailedWithAllDaysUnknown()
    {
        using var server = Configured();
        server.Pace.ReadThrows = new PaceDesktop.Core.Services.PaceApiException(401, "7Pace API error 401: nope");

        var response = await server.Client.GetAsync("/api/month?year=2026&month=6");
        var month = await response.Content.ReadFromJsonAsync<MonthDto>();

        // A failed fetch is a displayable state, not a server error.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("failed", month!.LoadState);
        Assert.Contains("401", month.Error);
        Assert.Equal("unknown", Day(month, 1).Status);
        Assert.Equal("nonWorking", Day(month, 6).Status);   // the weekend is still known locally
        Assert.Equal(0, month.Totals.Missing);
    }

    [Fact]
    public async Task GetMonth_ShortensTheDayBeforeAHoliday()
    {
        using var server = Configured();
        // Seed the holiday cache so the service does not need the network.
        var settings = server.Settings.Load();
        settings.HolidayCache[2026] = [new Holiday(new DateOnly(2026, 6, 19), "Midsommarafton")];
        settings.HolidayCache[2027] = [];
        server.Settings.Save(settings);

        var month = await server.Client.GetFromJsonAsync<MonthDto>("/api/month?year=2026&month=6");

        Assert.Equal("nonWorking", Day(month!, 19).Status);
        Assert.Equal("Midsommarafton", Day(month!, 19).HolidayName);
        Assert.Equal(5, Day(month!, 18).Expected);
    }

    [Fact]
    public async Task GetMonth_RejectsAnImpossibleMonth()
    {
        using var server = Configured();

        Assert.Equal(HttpStatusCode.BadRequest,
            (await server.Client.GetAsync("/api/month?year=2026&month=13")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await server.Client.GetAsync("/api/month?year=1800&month=1")).StatusCode);
    }

    [Fact]
    public async Task GetMonth_NeverReturnsTheToken()
    {
        using var server = Configured();

        var body = await server.Client.GetStringAsync("/api/month?year=2026&month=6");

        Assert.DoesNotContain("test-token", body);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/7PaceDesktop.Tests/7PaceDesktop.Tests.csproj --filter "FullyQualifiedName~MonthEndpointTests"`
Expected: compile errors — `MonthDto` and `DayDto` do not exist.

- [ ] **Step 3: Carry holiday names out of the holiday service**

In `src/7PaceDesktop.Core/Services/SwedishHolidayService.cs`, change the lookup record to add a
defaulted third parameter so existing construction sites keep compiling:

```csharp
public sealed record HolidayLookup(
    IReadOnlySet<DateOnly> Dates,
    bool IsIncomplete,
    IReadOnlyDictionary<DateOnly, string>? Names = null);
```

In `GetHolidaysAsync`, collect names alongside dates. Add `var names = new Dictionary<DateOnly, string>();`
beside the existing `dates` set, then record each holiday's name in both the cached and the fetched
branch:

```csharp
            if (settings.HolidayCache.TryGetValue(year, out var cached))
            {
                foreach (var h in cached) { dates.Add(h.Date); names[h.Date] = h.Name; }
                continue;
            }
```

and in the fetched branch, after `settings.HolidayCache[year] = holidays;`:

```csharp
                foreach (var h in holidays) { dates.Add(h.Date); names[h.Date] = h.Name; }
```

replacing the existing `foreach (var h in holidays) dates.Add(h.Date);` line. Finally return:

```csharp
        return new HolidayLookup(dates, incomplete, names);
```

- [ ] **Step 4: Add the month contracts**

Append to `src/7PaceDesktop.Server/Contracts.cs`:

```csharp
public sealed record ExistingWorkLogDto(string Id, double Hours, int WorkItemId, string? WorkItemName, string? Comment);

public sealed record DayDto(
    string Date,
    double Expected,
    double Logged,
    double Remaining,
    string Status,
    bool HitZeroFloor,
    int IsoWeek,
    bool InMonth,
    string? HolidayName,
    IReadOnlyList<ExistingWorkLogDto> Existing);

public sealed record TotalsDto(double Expected, double Logged, double Missing);

public sealed record MonthDto(
    int Year,
    int Month,
    string From,
    string To,
    string LoadState,
    string? Error,
    string? HolidayWarning,
    DateTimeOffset FetchedAt,
    double DailyHours,
    TotalsDto Totals,
    IReadOnlyList<DayDto> Days);
```

- [ ] **Step 5: Implement the endpoint**

`src/7PaceDesktop.Server/Endpoints/MonthEndpoints.cs`:

```csharp
using PaceDesktop.Core.Planning;
using PaceDesktop.Core.Services;
using PaceDesktop.Core.Storage;

namespace PaceDesktop.Server;

public static class MonthEndpoints
{
    public static void MapMonthEndpoints(this WebApplication app)
    {
        app.MapGet("/api/month", async (
            int year,
            int month,
            SettingsStore settingsStore,
            WorkItemStore workItemStore,
            SwedishHolidayService holidayService,
            IPaceClientFactory clients,
            TimeProvider time,
            CancellationToken ct) =>
        {
            if (month is < 1 or > 12 || year is < 2000 or > 2100)
                return Results.BadRequest(new { error = "Ogiltig månad." });

            var settings = settingsStore.Load();
            var (from, to) = CalendarGrid.RangeFor(year, month);

            var holidays = await holidayService.GetHolidaysAsync(from.Year, to.Year, ct);
            var schedule = new WorkSchedule(settings.DailyHours, holidays.Dates);

            MonthPlan plan;
            string loadState;
            string? error = null;
            try
            {
                var logs = await clients.CreateReader().GetWorkLogsAsync(from, to, ct);
                plan = MonthPlan.Build(from, to, schedule, logs);
                loadState = "loaded";
            }
            catch (Exception ex)
            {
                // A failed fetch is a state the UI displays, not a 500. Days become Unknown so
                // registration is blocked rather than topping up from an assumed zero.
                plan = MonthPlan.Unknown(from, to, schedule);
                loadState = "failed";
                error = ex.Message;
            }

            var names = workItemStore.Load().ToDictionary(i => i.Id, i => i.Name);
            var days = plan.Days.Select(d => new DayDto(
                Date: d.Date.ToString("yyyy-MM-dd"),
                Expected: d.Expected,
                Logged: Math.Round(d.Logged, 2),
                Remaining: Math.Round(d.Remaining, 2),
                Status: StatusName(d.Status),
                HitZeroFloor: d.HitZeroFloor,
                IsoWeek: CalendarGrid.IsoWeek(d.Date),
                InMonth: d.Date.Year == year && d.Date.Month == month,
                HolidayName: holidays.Names?.GetValueOrDefault(d.Date),
                Existing: d.Existing.Select(e => new ExistingWorkLogDto(
                    e.Id, Math.Round(e.Hours, 2), e.WorkItemId,
                    names.GetValueOrDefault(e.WorkItemId), e.Comment)).ToList()
            )).ToList();

            var totals = plan.TotalsForMonth(year, month);

            return Results.Ok(new MonthDto(
                Year: year,
                Month: month,
                From: from.ToString("yyyy-MM-dd"),
                To: to.ToString("yyyy-MM-dd"),
                LoadState: loadState,
                Error: error,
                HolidayWarning: holidays.IsIncomplete
                    ? "Kunde inte hämta röda dagar — alla vardagar behandlas som vanliga arbetsdagar."
                    : null,
                FetchedAt: time.GetUtcNow(),
                DailyHours: settings.DailyHours,
                Totals: new TotalsDto(
                    Math.Round(totals.Expected, 2),
                    Math.Round(totals.Logged, 2),
                    Math.Round(totals.Missing, 2)),
                Days: days));
        });
    }

    /// <summary>DayStatus as a camel-case wire string, e.g. NonWorking -> "nonWorking".</summary>
    private static string StatusName(DayStatus status) =>
        char.ToLowerInvariant(status.ToString()[0]) + status.ToString()[1..];
}
```

- [ ] **Step 6: Register TimeProvider and drop the month stub**

In `src/7PaceDesktop.Server/Program.cs`, add beside the other registrations:

```csharp
builder.Services.AddSingleton(TimeProvider.System);
```

In `src/7PaceDesktop.Server/Endpoints/Stubs.cs`, delete `MapMonthEndpoints`.

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/7PaceDesktop.Tests/7PaceDesktop.Tests.csproj --filter "FullyQualifiedName~MonthEndpointTests"`
Expected: PASS, 9 tests.

If `GetMonth_TotalsCoverTheMonthOnly` fails because the holiday service reached the network and
found a June holiday, seed empty holiday caches for both grid years in the `Configured` helper the
same way `GetMonth_ShortensTheDayBeforeAHoliday` does, so no test depends on the internet.

- [ ] **Step 8: Run the whole suite**

Run: `dotnet test tests/7PaceDesktop.Tests/7PaceDesktop.Tests.csproj`
Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add src/7PaceDesktop.Core/Services/SwedishHolidayService.cs src/7PaceDesktop.Server tests/7PaceDesktop.Tests
git commit -m "feat: month endpoint merging schedule, holidays and registered time"
```

---

### Task 9: Register endpoint

**Files:**
- Modify: `src/7PaceDesktop.Server/Contracts.cs`
- Create: `src/7PaceDesktop.Server/Endpoints/RegisterEndpoints.cs`
- Delete: `src/7PaceDesktop.Server/Endpoints/Stubs.cs`
- Test: `tests/7PaceDesktop.Tests/RegisterEndpointTests.cs`

**Interfaces:**
- Consumes: `FillPlanner.Plan`, `FillSpec`, `FillLine`, `MonthPlan`, `WorkSchedule`, `CalendarGrid`, `IPaceClientFactory`, `SwedishHolidayService`, `SettingsStore`, `ClientHeaderFilter`.
- Produces:
  - `record FillLineDto(int WorkItemId, double Hours)`
  - `record RegisterRequestDto(IReadOnlyList<string> Dates, IReadOnlyList<FillLineDto> Lines, bool Simulate)`
  - `record DayResultDto(string Date, double Hours, string Status, string? Error)` where `Status` is `"ok"`, `"partial"` or `"failed"`. `Hours` is always the **planned** hours for the day, in both real and simulate runs. `"partial"` means some of the day's work item lines posted and some did not, so the day must not be treated as "nothing landed".
  - `record RegisterResponseDto(int PostedEntries, int FailedEntries, int SkippedDays, double TotalHours, IReadOnlyList<DayResultDto> Days)`
  - `RegisterEndpoints.MapRegisterEndpoints(this WebApplication app)` mapping `POST /api/register`

**Deliberate strengthening of spec rule 4.** The spec has the client refetch when its data is
older than five minutes. The server instead refetches unconditionally before planning and returns
`409 Conflict` if that fetch fails, so no client can ever cause a top-up from stale or assumed-zero
state. The client keeps no staleness logic.

- [ ] **Step 1: Write the failing tests**

`tests/7PaceDesktop.Tests/RegisterEndpointTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using PaceDesktop.Core.Models;
using PaceDesktop.Core.Services;
using PaceDesktop.Core.Storage;
using PaceDesktop.Server;

namespace PaceDesktop.Tests;

public class RegisterEndpointTests
{
    private const int Sprint = 12345;
    private const int Support = 12401;

    private static ServerFixture Configured()
    {
        var server = new ServerFixture();
        server.Settings.Save(new AppSettings
        {
            OrganizationName = "icore",
            DailyHours = 8,
            HolidayCache = { [2026] = [], [2027] = [] }   // keep tests off the network
        });
        server.WorkItems.Save([new WorkItem(Sprint, "Sprintarbete", true), new WorkItem(Support, "Support", false)]);
        return server;
    }

    private static RegisterRequestDto Request(IEnumerable<int> days, bool simulate = false,
        params FillLineDto[] lines) =>
        new(days.Select(d => $"2026-06-{d:00}").ToList(),
            lines.Length > 0 ? lines : [new FillLineDto(Sprint, 8)],
            simulate);

    [Fact]
    public async Task Register_PostsOneEntryPerEmptyDay()
    {
        using var server = Configured();

        var response = await server.Client.PostAsJsonAsync("/api/register", Request([22, 23, 26]));
        var result = await response.Content.ReadFromJsonAsync<RegisterResponseDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, result!.PostedEntries);
        Assert.Equal(0, result.FailedEntries);
        Assert.Equal(24, result.TotalHours);
        Assert.Equal(3, server.Pace.Submitted.Count);
        Assert.All(server.Pace.Submitted, e => Assert.Equal(8, e.Hours));
    }

    [Fact]
    public async Task Register_TopsUpAPartialDayAndSkipsACompleteOne()
    {
        using var server = Configured();
        server.Pace.Existing.Add(new ExistingWorkLog("a", new DateOnly(2026, 6, 24), 3, Sprint, null));
        server.Pace.Existing.Add(new ExistingWorkLog("b", new DateOnly(2026, 6, 25), 8, Sprint, null));

        var result = await (await server.Client.PostAsJsonAsync("/api/register",
            Request([22, 23, 24, 25, 26]))).Content.ReadFromJsonAsync<RegisterResponseDto>();

        Assert.Equal(29, result!.TotalHours);      // 8 + 8 + 5 + 0 + 8
        Assert.Equal(1, result.SkippedDays);
        Assert.Equal(5, server.Pace.Submitted.Single(e => e.Date == new DateOnly(2026, 6, 24)).Hours);
        Assert.DoesNotContain(server.Pace.Submitted, e => e.Date == new DateOnly(2026, 6, 25));
    }

    [Fact]
    public async Task Register_SplitsAcrossWorkItems()
    {
        using var server = Configured();

        await server.Client.PostAsJsonAsync("/api/register",
            Request([22], lines: [new FillLineDto(Sprint, 6), new FillLineDto(Support, 2)]));

        Assert.Equal(6, server.Pace.Submitted.Single(e => e.WorkItemId == Sprint).Hours);
        Assert.Equal(2, server.Pace.Submitted.Single(e => e.WorkItemId == Support).Hours);
    }

    [Fact]
    public async Task Register_SkipsWeekendsAndHolidays()
    {
        using var server = Configured();
        var settings = server.Settings.Load();
        settings.HolidayCache[2026] = [new Holiday(new DateOnly(2026, 6, 19), "Midsommarafton")];
        server.Settings.Save(settings);

        var result = await (await server.Client.PostAsJsonAsync("/api/register",
            Request([6, 7, 19]))).Content.ReadFromJsonAsync<RegisterResponseDto>();

        Assert.Equal(0, result!.PostedEntries);
        Assert.Empty(server.Pace.Submitted);
    }

    [Fact]
    public async Task Register_Simulate_PostsNothingButReportsThePlan()
    {
        using var server = Configured();

        var result = await (await server.Client.PostAsJsonAsync("/api/register",
            Request([22, 23], simulate: true))).Content.ReadFromJsonAsync<RegisterResponseDto>();

        Assert.Equal(2, result!.PostedEntries);
        Assert.Equal(16, result.TotalHours);
        Assert.Empty(server.Pace.Submitted);
        Assert.All(result.Days, d => Assert.Equal("ok", d.Status));
    }

    [Fact]
    public async Task Register_ReportsPerDayFailuresAndKeepsGoing()
    {
        using var server = Configured();
        server.Pace.SubmitThrows = entry => entry.Date == new DateOnly(2026, 6, 23)
            ? new PaceApiException(500, "7Pace API error 500: boom")
            : null;

        var result = await (await server.Client.PostAsJsonAsync("/api/register",
            Request([22, 23, 26]))).Content.ReadFromJsonAsync<RegisterResponseDto>();

        Assert.Equal(2, result!.PostedEntries);
        Assert.Equal(1, result.FailedEntries);
        var failed = result.Days.Single(d => d.Status == "failed");
        Assert.Equal("2026-06-23", failed.Date);
        Assert.Contains("500", failed.Error);
        Assert.Equal(2, server.Pace.Submitted.Count);
    }

    [Fact]
    public async Task Register_RefetchesBeforePlanning()
    {
        using var server = Configured();

        await server.Client.PostAsJsonAsync("/api/register", Request([22]));

        // The server never trusts a client-supplied view of what is already logged.
        Assert.True(server.Pace.ReadCount >= 1);
    }

    [Fact]
    public async Task Register_WhenTheRefetchFails_PostsNothingAndConflicts()
    {
        using var server = Configured();
        server.Pace.ReadThrows = new PaceApiException(503, "7Pace API error 503: down");

        var response = await server.Client.PostAsJsonAsync("/api/register", Request([22, 23]));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty(server.Pace.Submitted);
    }

    [Fact]
    public async Task Register_RejectsAnEmptySelectionOrZeroTarget()
    {
        using var server = Configured();

        var noDays = await server.Client.PostAsJsonAsync("/api/register",
            new RegisterRequestDto([], [new FillLineDto(Sprint, 8)], false));
        var noHours = await server.Client.PostAsJsonAsync("/api/register",
            new RegisterRequestDto(["2026-06-22"], [new FillLineDto(Sprint, 0)], false));

        Assert.Equal(HttpStatusCode.BadRequest, noDays.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noHours.StatusCode);
    }

    [Fact]
    public async Task Register_RejectsMalformedDatesAndSpansOverAMonth()
    {
        using var server = Configured();

        var bad = await server.Client.PostAsJsonAsync("/api/register",
            new RegisterRequestDto(["not-a-date"], [new FillLineDto(Sprint, 8)], false));
        var wide = await server.Client.PostAsJsonAsync("/api/register",
            new RegisterRequestDto(["2026-01-05", "2026-09-05"], [new FillLineDto(Sprint, 8)], false));

        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, wide.StatusCode);
    }

    [Fact]
    public async Task Register_RequiresTheClientHeader()
    {
        using var server = Configured();
        using var bare = server.CreateBareClient();

        var response = await bare.PostAsJsonAsync("/api/register", Request([22]));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(server.Pace.Submitted);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/7PaceDesktop.Tests/7PaceDesktop.Tests.csproj --filter "FullyQualifiedName~RegisterEndpointTests"`
Expected: compile errors — `RegisterRequestDto` and `RegisterResponseDto` do not exist.

- [ ] **Step 3: Add the register contracts**

Append to `src/7PaceDesktop.Server/Contracts.cs`:

```csharp
public sealed record FillLineDto(int WorkItemId, double Hours);

public sealed record RegisterRequestDto(
    IReadOnlyList<string> Dates,
    IReadOnlyList<FillLineDto> Lines,
    bool Simulate);

public sealed record DayResultDto(string Date, double Hours, string Status, string? Error);

public sealed record RegisterResponseDto(
    int PostedEntries,
    int FailedEntries,
    int SkippedDays,
    double TotalHours,
    IReadOnlyList<DayResultDto> Days);
```

- [ ] **Step 4: Implement the endpoint**

`src/7PaceDesktop.Server/Endpoints/RegisterEndpoints.cs`:

```csharp
using System.Collections.Concurrent;
using PaceDesktop.Core.Models;
using PaceDesktop.Core.Planning;
using PaceDesktop.Core.Services;
using PaceDesktop.Core.Storage;

namespace PaceDesktop.Server;

public static class RegisterEndpoints
{
    private const int MaxConcurrentSubmits = 4;
    private const int MaxSpanDays = 62;

    public static void MapRegisterEndpoints(this WebApplication app)
    {
        app.MapPost("/api/register", async (
            RegisterRequestDto body,
            SettingsStore settingsStore,
            SwedishHolidayService holidayService,
            IPaceClientFactory clients,
            CancellationToken ct) =>
        {
            if (body.Dates.Count == 0)
                return Results.BadRequest(new { error = "Inga dagar valda." });
            if (body.Lines.Count == 0 || body.Lines.Sum(l => l.Hours) <= MonthPlan.Epsilon)
                return Results.BadRequest(new { error = "Fördelningen måste summera till mer än noll." });
            if (body.Lines.Any(l => l.WorkItemId <= 0 || l.Hours < 0))
                return Results.BadRequest(new { error = "Ogiltig rad i fördelningen." });

            var dates = new HashSet<DateOnly>();
            foreach (var text in body.Dates)
            {
                if (!DateOnly.TryParse(text, out var date))
                    return Results.BadRequest(new { error = $"Ogiltigt datum '{text}'." });
                dates.Add(date);
            }

            var from = dates.Min();
            var to = dates.Max();
            if (to.DayNumber - from.DayNumber + 1 > MaxSpanDays)
                return Results.BadRequest(new { error = "Markeringen sträcker sig över mer än två månader." });

            var settings = settingsStore.Load();
            var holidays = await holidayService.GetHolidaysAsync(from.Year, to.AddDays(1).Year, ct);
            var schedule = new WorkSchedule(settings.DailyHours, holidays.Dates);

            // Always plan against a fresh read. The client's view of what is already logged is
            // never trusted, so a stale page cannot cause a top-up from an assumed zero.
            IReadOnlyList<ExistingWorkLog> logs;
            try
            {
                logs = await clients.CreateReader().GetWorkLogsAsync(from, to, ct);
            }
            catch (Exception ex)
            {
                return Results.Conflict(new
                {
                    error = "Kunde inte hämta redan registrerad tid, så ingenting registrerades. "
                          + "Försök igen. (" + ex.Message + ")"
                });
            }

            var plan = MonthPlan.Build(from, to, schedule, logs);
            var spec = new FillSpec(body.Lines.Select(l => new FillLine(l.WorkItemId, l.Hours)).ToList());
            var entries = FillPlanner.Plan(dates, plan, spec);
            var summary = FillPlanner.Summarize(dates, plan, spec);

            var errors = new ConcurrentDictionary<DateOnly, string>();
            var posted = 0;
            var failed = 0;

            if (!body.Simulate)
            {
                var client = clients.CreateClient();
                using var gate = new SemaphoreSlim(MaxConcurrentSubmits);
                await Task.WhenAll(entries.Select(async entry =>
                {
                    await gate.WaitAsync(ct);
                    try
                    {
                        await client.SubmitAsync(entry, ct);
                        Interlocked.Increment(ref posted);
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Increment(ref failed);
                        errors.TryAdd(entry.Date, ex.Message);
                    }
                    finally
                    {
                        gate.Release();
                    }
                }));
            }
            else
            {
                posted = entries.Count;
            }

            var days = entries
                .GroupBy(e => e.Date)
                .OrderBy(g => g.Key)
                .Select(g => new DayResultDto(
                    Date: g.Key.ToString("yyyy-MM-dd"),
                    Hours: Math.Round(g.Sum(e => e.Hours), 2),
                    Status: errors.ContainsKey(g.Key) ? "failed" : "ok",
                    Error: errors.GetValueOrDefault(g.Key)))
                .ToList();

            return Results.Ok(new RegisterResponseDto(
                PostedEntries: posted,
                FailedEntries: failed,
                SkippedDays: summary.SkippedDays,
                TotalHours: summary.TotalHours,
                Days: days));
        }).AddEndpointFilter<ClientHeaderFilter>();
    }
}
```

- [ ] **Step 5: Delete the stubs file**

Run: `git rm src/7PaceDesktop.Server/Endpoints/Stubs.cs`

All three endpoint groups are now real.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/7PaceDesktop.Tests/7PaceDesktop.Tests.csproj --filter "FullyQualifiedName~RegisterEndpointTests"`
Expected: PASS, 11 tests.

- [ ] **Step 7: Run the whole suite**

Run: `dotnet test tests/7PaceDesktop.Tests/7PaceDesktop.Tests.csproj`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/7PaceDesktop.Server tests/7PaceDesktop.Tests
git commit -m "feat: register endpoint plans against a fresh read and reports per-day results"
```

---

### Task 10: Web scaffold, design tokens and API client

**Files:**
- Create: `web/package.json`, `web/vite.config.ts`, `web/tsconfig.json`, `web/index.html`, `web/vitest.setup.ts`
- Create: `web/src/main.tsx`, `web/src/theme.css`, `web/src/types.ts`, `web/src/api.ts`, `web/src/App.tsx`
- Create: `web/src/api.test.ts`
- Modify: `.gitignore`
- Modify: `src/7PaceDesktop.Server/7PaceDesktop.Server.csproj`

**Interfaces:**
- Consumes: the server's `/api/health`, `/api/config`, `/api/workitems`, `/api/month`, `/api/register` and their DTO shapes from Tasks 7 to 9.
- Produces:
  - `web/src/types.ts` — `Config`, `WorkItem`, `Month`, `Day`, `DayStatus`, `Totals`, `ExistingLog`, `RegisterRequest`, `RegisterResponse`, `DayResult`
  - `web/src/api.ts` — `api.config()`, `api.saveConfig(body)`, `api.workItems()`, `api.saveWorkItems(items)`, `api.month(year, month)`, `api.register(body)`, and `ApiError` with `status` and `message`
  - CSS custom properties on `:root` and `.dark`, named `--bg`, `--surface`, `--fg`, `--subtle`, `--border`, `--accent`, `--accent-fg`, `--row-alt`, `--chip`, `--sel-bg`, `--plan-bg`, `--ok`, `--warn`, `--idle`, `--over`, `--danger`, `--danger-bg`, `--track`

- [ ] **Step 1: Scaffold the project**

Run from the repository root:

```bash
npm create vite@latest web -- --template react-ts
cd web
npm install
npm install -D tailwindcss @tailwindcss/vite vitest jsdom @testing-library/react @testing-library/user-event @testing-library/jest-dom
```

- [ ] **Step 2: Configure Vite to build into the server's wwwroot**

`web/vite.config.ts`:

```ts
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

export default defineConfig({
  plugins: [react(), tailwindcss()],
  // The built SPA is what the server serves; the server is the whole app.
  build: { outDir: '../src/7PaceDesktop.Server/wwwroot', emptyOutDir: true },
  server: {
    port: 5173,
    // In development the API lives on the dotnet server; run it with
    // ASPNETCORE_URLS=http://127.0.0.1:5111 dotnet run --project src/7PaceDesktop.Server
    proxy: { '/api': 'http://127.0.0.1:5111' },
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['./vitest.setup.ts'],
    globals: true,
  },
})
```

`web/vitest.setup.ts`:

```ts
import '@testing-library/jest-dom/vitest'
```

Add to `web/package.json`'s `scripts`:

```json
    "test": "vitest run",
    "test:watch": "vitest"
```

- [ ] **Step 3: Ignore build output and node_modules**

Append to `.gitignore`:

```
web/node_modules/
web/dist/
src/7PaceDesktop.Server/wwwroot/*
!src/7PaceDesktop.Server/wwwroot/.gitkeep
```

- [ ] **Step 4: Write the design tokens**

`web/src/theme.css`:

```css
@import "tailwindcss";

/* Lifted verbatim from src/7PaceDesktop.App/Themes/Palette.*.xaml so the web app looks
   continuous with the desktop app it replaces. */
:root {
  --bg: #F3F3F3;
  --surface: #FFFFFF;
  --fg: #1A1A1A;
  --subtle: #605E5C;
  --border: #D6D6D6;
  --accent: #0067C0;
  --accent-fg: #FFFFFF;
  --row-alt: #F7F7F7;
  --chip: #EFEFEF;
  --sel-bg: rgb(0 103 192 / 0.10);
  --plan-bg: rgb(0 103 192 / 0.14);
  --ok: #107C10;
  --warn: #C77700;
  --idle: #B9B9B9;
  --over: #7C5DBF;
  --danger: #C42B1C;
  --danger-bg: rgb(196 43 28 / 0.10);
  --track: #E4E4E4;
}

.dark {
  --bg: #1F1F1F;
  --surface: #2B2B2B;
  --fg: #F5F5F5;
  --subtle: #C8C8C8;
  --border: #3D3D3D;
  --accent: #60CDFF;
  --accent-fg: #00243D;
  --row-alt: #262626;
  --chip: #343434;
  --sel-bg: rgb(96 205 255 / 0.14);
  --plan-bg: rgb(96 205 255 / 0.18);
  --ok: #6CCB5F;
  --warn: #FCE100;
  --idle: #6A6A6A;
  --over: #B4A0FF;
  --danger: #FF99A4;
  --danger-bg: rgb(255 153 164 / 0.12);
  --track: #3A3A3A;
}

html, body, #root { height: 100%; }

body {
  margin: 0;
  background: var(--bg);
  color: var(--fg);
  font-family: "Segoe UI Variable", "Segoe UI", system-ui, sans-serif;
}

/* Focus is never suppressed - the calendar is keyboard-operable. */
:focus-visible { outline: 2px solid var(--accent); outline-offset: 2px; }
```

- [ ] **Step 5: Write the wire types**

`web/src/types.ts`:

```ts
export type DayStatus = 'nonWorking' | 'empty' | 'partial' | 'complete' | 'over' | 'unknown'
export type LoadState = 'loaded' | 'failed'
export type Theme = 'System' | 'Light' | 'Dark'

export interface Config {
  configured: boolean
  organization: string
  dailyHours: number
  theme: Theme
  hasToken: boolean
}

/** The token is write-only: omit it to keep the stored one. */
export interface ConfigUpdate {
  organization: string
  token?: string | null
  dailyHours: number
  theme: Theme
}

export interface WorkItem {
  id: number
  name: string
  isFavorite: boolean
}

export interface ExistingLog {
  id: string
  hours: number
  workItemId: number
  workItemName: string | null
  comment: string | null
}

export interface Day {
  date: string
  expected: number
  logged: number
  remaining: number
  status: DayStatus
  hitZeroFloor: boolean
  isoWeek: number
  inMonth: boolean
  holidayName: string | null
  existing: ExistingLog[]
}

export interface Totals {
  expected: number
  logged: number
  missing: number
}

export interface Month {
  year: number
  month: number
  from: string
  to: string
  loadState: LoadState
  error: string | null
  holidayWarning: string | null
  fetchedAt: string
  dailyHours: number
  totals: Totals
  days: Day[]
}

export interface FillLine {
  workItemId: number
  hours: number
}

export interface RegisterRequest {
  dates: string[]
  lines: FillLine[]
  simulate: boolean
}

export interface DayResult {
  date: string
  hours: number
  // Always the PLANNED hours, in both real and simulate runs.
  status: 'ok' | 'partial' | 'failed'
  error: string | null
}

export interface RegisterResponse {
  postedEntries: number
  failedEntries: number
  skippedDays: number
  totalHours: number
  days: DayResult[]
}
```

- [ ] **Step 6: Write the failing API client tests**

`web/src/api.test.ts`:

```ts
import { describe, expect, it, vi, afterEach } from 'vitest'
import { api, ApiError } from './api'

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

afterEach(() => vi.unstubAllGlobals())

describe('api', () => {
  it('sends the client header on every request', async () => {
    const fetchMock = vi.fn().mockResolvedValue(json({ configured: false }))
    vi.stubGlobal('fetch', fetchMock)

    await api.config()

    const [, init] = fetchMock.mock.calls[0]
    expect(init.headers['X-Pace-Client']).toBe('1')
  })

  it('builds the month URL from year and month', async () => {
    const fetchMock = vi.fn().mockResolvedValue(json({ days: [] }))
    vi.stubGlobal('fetch', fetchMock)

    await api.month(2026, 6)

    expect(fetchMock.mock.calls[0][0]).toBe('/api/month?year=2026&month=6')
  })

  it('throws ApiError carrying the status and the server message', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json({ error: 'Ogiltig månad.' }, 400)))

    await expect(api.month(2026, 13)).rejects.toMatchObject({
      status: 400,
      message: 'Ogiltig månad.',
    })
    await expect(api.month(2026, 13)).rejects.toBeInstanceOf(ApiError)
  })

  it('reports a conflict from register distinctly, so the UI can say nothing was posted', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json({ error: 'Kunde inte hämta.' }, 409)))

    await expect(api.register({ dates: ['2026-06-22'], lines: [], simulate: false }))
      .rejects.toMatchObject({ status: 409 })
  })

  it('surfaces an unreachable server as a clear message rather than a parse error', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('Failed to fetch')))

    await expect(api.config()).rejects.toMatchObject({ status: 0 })
  })
})
```

- [ ] **Step 7: Run the tests to verify they fail**

Run: `cd web && npm test`
Expected: FAIL — `./api` does not exist.

- [ ] **Step 8: Implement the API client**

`web/src/api.ts`:

```ts
import type {
  Config, ConfigUpdate, Month, RegisterRequest, RegisterResponse, WorkItem,
} from './types'

/** Every failure the UI can render: a server status plus a message worth showing. */
export class ApiError extends Error {
  constructor(readonly status: number, message: string) {
    super(message)
    this.name = 'ApiError'
  }
}

// A custom header forces a CORS preflight, and the server configures no CORS policy, so
// only the same-origin SPA can reach the mutating endpoints. See ClientHeaderFilter.
const headers = { 'Content-Type': 'application/json', 'X-Pace-Client': '1' }

async function call<T>(url: string, method = 'GET', body?: unknown): Promise<T> {
  let response: Response
  try {
    response = await fetch(url, {
      method,
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
    })
  } catch {
    throw new ApiError(0, 'Appen svarar inte. Kontrollera att 7Pace Desktop körs.')
  }

  if (!response.ok) {
    let message = `Fel ${response.status}`
    try {
      const payload = (await response.json()) as { error?: string }
      if (payload?.error) message = payload.error
    } catch {
      // Non-JSON error body: keep the status-only message.
    }
    throw new ApiError(response.status, message)
  }

  if (response.status === 204) return undefined as T
  const text = await response.text()
  return (text ? JSON.parse(text) : undefined) as T
}

export const api = {
  config: () => call<Config>('/api/config'),
  saveConfig: (body: ConfigUpdate) => call<void>('/api/config', 'PUT', body),
  workItems: () => call<WorkItem[]>('/api/workitems'),
  saveWorkItems: (items: WorkItem[]) => call<void>('/api/workitems', 'PUT', items),
  month: (year: number, month: number) => call<Month>(`/api/month?year=${year}&month=${month}`),
  register: (body: RegisterRequest) => call<RegisterResponse>('/api/register', 'POST', body),
}
```

- [ ] **Step 9: Wire up the entry point and a placeholder App**

`web/src/main.tsx`:

```tsx
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './theme.css'
import { App } from './App'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
```

`web/src/App.tsx`:

```tsx
/** Replaced by the real shell in Task 11. */
export function App() {
  return <div className="p-6 text-[var(--fg)]">7Pace Desktop</div>
}
```

Delete the Vite template's `web/src/App.css` and `web/src/index.css` if the scaffold created them,
and remove any import of them.

- [ ] **Step 10: Make the dotnet build produce the SPA**

Add to `src/7PaceDesktop.Server/7PaceDesktop.Server.csproj`:

```xml
  <!-- Build the SPA into wwwroot on Release builds and on publish, so the executable is the
       whole app. Debug builds skip it: run the Vite dev server instead. -->
  <Target Name="BuildSpa" BeforeTargets="Build" Condition="'$(Configuration)' == 'Release'">
    <Exec Command="npm ci" WorkingDirectory="$(MSBuildProjectDirectory)\..\..\web" />
    <Exec Command="npm run build" WorkingDirectory="$(MSBuildProjectDirectory)\..\..\web" />
  </Target>
```

- [ ] **Step 11: Run the tests to verify they pass**

Run: `cd web && npm test`
Expected: PASS, 5 tests.

- [ ] **Step 12: Verify the debug build is unaffected**

Run: `dotnet build 7PaceDesktop.slnx`
Expected: build succeeds without invoking npm.

- [ ] **Step 13: Commit**

```bash
git add .gitignore web src/7PaceDesktop.Server/7PaceDesktop.Server.csproj
git commit -m "feat: web scaffold with design tokens and typed API client"
```

---

### Task 11: Month calendar rendering

**Files:**
- Create: `web/src/dates.ts`, `web/src/dates.test.ts`
- Create: `web/src/components/DayCell.tsx`, `web/src/components/DayCell.test.tsx`
- Create: `web/src/components/Legend.tsx`, `web/src/components/StatusBar.tsx`, `web/src/components/Icons.tsx`
- Create: `web/src/views/MonthView.tsx`, `web/src/views/MonthView.test.tsx`
- Create: `web/src/useTheme.ts`
- Modify: `web/src/App.tsx`

**Interfaces:**
- Consumes: `Day`, `DayStatus`, `Month`, `Totals` from `types.ts`; `api.month` from `api.ts`.
- Produces:
  - `dates.ts` — `formatMonth(year, month) -> string` (Swedish, e.g. `"Juni 2026"`), `weekRows(days: Day[]) -> Day[][]`, `datesBetween(a, b) -> string[]`, `addMonths(year, month, delta) -> {year, month}`
  - `DayCell.tsx` — `<DayCell day plannedHours selected onPointerDown onPointerEnter onKeyDown />`
  - `Legend.tsx` — `<Legend />`; `StatusBar.tsx` — `<StatusBar month />`
  - `MonthView.tsx` — `<MonthView />`, owning the period, the fetch and the grid
  - `useTheme.ts` — `useTheme(theme: Theme)` toggling the `dark` class on `<html>`

- [ ] **Step 1: Write the failing date-helper tests**

`web/src/dates.test.ts`:

```ts
import { describe, expect, it } from 'vitest'
import { addMonths, datesBetween, formatMonth, weekRows } from './dates'
import type { Day } from './types'

const day = (date: string): Day => ({
  date, expected: 8, logged: 0, remaining: 8, status: 'empty', hitZeroFloor: false,
  isoWeek: 1, inMonth: true, holidayName: null, existing: [],
})

describe('dates', () => {
  it('formats a month in Swedish', () => {
    expect(formatMonth(2026, 6)).toBe('Juni 2026')
    expect(formatMonth(2026, 12)).toBe('December 2026')
  })

  it('steps months across a year boundary', () => {
    expect(addMonths(2026, 12, 1)).toEqual({ year: 2027, month: 1 })
    expect(addMonths(2026, 1, -1)).toEqual({ year: 2025, month: 12 })
  })

  it('lists the dates between two days inclusively, in either order', () => {
    expect(datesBetween('2026-06-22', '2026-06-25'))
      .toEqual(['2026-06-22', '2026-06-23', '2026-06-24', '2026-06-25'])
    expect(datesBetween('2026-06-25', '2026-06-22'))
      .toEqual(['2026-06-22', '2026-06-23', '2026-06-24', '2026-06-25'])
    expect(datesBetween('2026-06-22', '2026-06-22')).toEqual(['2026-06-22'])
  })

  it('splits a 35-day grid into five rows of seven', () => {
    const days = datesBetween('2026-06-01', '2026-07-05').map(day)

    const rows = weekRows(days)

    expect(rows).toHaveLength(5)
    expect(rows.every((r) => r.length === 7)).toBe(true)
    expect(rows[0][0].date).toBe('2026-06-01')
  })
})
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd web && npm test`
Expected: FAIL — `./dates` does not exist.

- [ ] **Step 3: Implement the date helpers**

`web/src/dates.ts`:

```ts
import type { Day } from './types'

const MONTHS = [
  'Januari', 'Februari', 'Mars', 'April', 'Maj', 'Juni',
  'Juli', 'Augusti', 'September', 'Oktober', 'November', 'December',
]

export const WEEKDAYS = ['mån', 'tis', 'ons', 'tor', 'fre', 'lör', 'sön']

export const formatMonth = (year: number, month: number) => `${MONTHS[month - 1]} ${year}`

export function addMonths(year: number, month: number, delta: number) {
  const zeroBased = year * 12 + (month - 1) + delta
  return { year: Math.floor(zeroBased / 12), month: (zeroBased % 12) + 1 }
}

/** Dates are handled as plain ISO strings; UTC arithmetic keeps them free of timezone drift. */
export function datesBetween(a: string, b: string): string[] {
  const [from, to] = a <= b ? [a, b] : [b, a]
  const out: string[] = []
  for (let d = new Date(`${from}T00:00:00Z`); ; d = new Date(d.getTime() + 86400000)) {
    const iso = d.toISOString().slice(0, 10)
    out.push(iso)
    if (iso >= to) break
  }
  return out
}

export function weekRows(days: Day[]): Day[][] {
  const rows: Day[][] = []
  for (let i = 0; i < days.length; i += 7) rows.push(days.slice(i, i + 7))
  return rows
}

/** Swedish decimal formatting, trimming a trailing ",0". */
export const hours = (value: number) =>
  Number.isInteger(value) ? String(value) : value.toFixed(2).replace(/\.?0+$/, '').replace('.', ',')
```

- [ ] **Step 4: Write the failing day-cell tests**

`web/src/components/DayCell.test.tsx`:

```tsx
import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { DayCell } from './DayCell'
import type { Day, DayStatus } from '../types'

const day = (over: Partial<Day> = {}): Day => ({
  date: '2026-06-03', expected: 8, logged: 6, remaining: 2, status: 'partial',
  hitZeroFloor: false, isoWeek: 23, inMonth: true, holidayName: null,
  existing: [{ id: 'a', hours: 6, workItemId: 12345, workItemName: 'Sprintarbete', comment: null }],
  ...over,
})

describe('DayCell', () => {
  it('shows logged over expected hours', () => {
    render(<DayCell day={day()} plannedHours={0} selected={false} />)

    expect(screen.getByText('6')).toBeInTheDocument()
    expect(screen.getByText('/ 8 h')).toBeInTheDocument()
  })

  it('shows the work item id of existing time', () => {
    render(<DayCell day={day()} plannedHours={0} selected={false} />)

    expect(screen.getByText('#12345')).toBeInTheDocument()
  })

  it('badges the planned top-up', () => {
    render(<DayCell day={day()} plannedHours={5} selected />)

    expect(screen.getByText('+5 h')).toBeInTheDocument()
  })

  it('badges a selected day that is already complete as skipped', () => {
    render(<DayCell day={day({ status: 'complete', logged: 8, remaining: 0 })} plannedHours={0} selected />)

    expect(screen.getByText('klar')).toBeInTheDocument()
  })

  it('names a holiday instead of hours', () => {
    render(
      <DayCell
        day={day({ status: 'nonWorking', expected: 0, logged: 0, remaining: 0, holidayName: 'Midsommarafton', existing: [] })}
        plannedHours={0}
        selected={false}
      />,
    )

    expect(screen.getByText('Midsommarafton')).toBeInTheDocument()
    expect(screen.queryByText('/ 0 h')).not.toBeInTheDocument()
  })

  it('marks an unknown day as not fetched rather than empty', () => {
    render(<DayCell day={day({ status: 'unknown', logged: 0, remaining: 8, existing: [] })} plannedHours={0} selected={false} />)

    expect(screen.getByText('?')).toBeInTheDocument()
    expect(screen.getByText('ej hämtad')).toBeInTheDocument()
  })

  it('states the hours in text for every status, so colour is never the only cue', () => {
    const statuses: DayStatus[] = ['empty', 'partial', 'complete', 'over']

    for (const status of statuses) {
      const { unmount } = render(
        <DayCell day={day({ status, logged: 4, remaining: 4 })} plannedHours={0} selected={false} />,
      )
      expect(screen.getByRole('button')).toHaveAccessibleName(/4/)
      unmount()
    }
  })

  it('exposes selection state to assistive technology', () => {
    render(<DayCell day={day()} plannedHours={0} selected />)

    expect(screen.getByRole('button')).toHaveAttribute('aria-pressed', 'true')
  })
})
```

- [ ] **Step 5: Run the tests to verify they fail**

Run: `cd web && npm test`
Expected: FAIL — `./DayCell` does not exist.

- [ ] **Step 6: Implement the day cell**

`web/src/components/DayCell.tsx`:

```tsx
import type { Day, DayStatus } from '../types'
import { hours } from '../dates'

const STRIPE: Record<DayStatus, string> = {
  complete: 'var(--ok)',
  partial: 'var(--warn)',
  empty: 'var(--idle)',
  over: 'var(--over)',
  unknown: 'var(--subtle)',
  nonWorking: 'transparent',
}

interface Props {
  day: Day
  plannedHours: number
  selected: boolean
  onPointerDown?: (event: React.PointerEvent) => void
  onPointerEnter?: (event: React.PointerEvent) => void
  onKeyDown?: (event: React.KeyboardEvent) => void
  tabIndex?: number
}

function accessibleName(day: Day, plannedHours: number): string {
  const date = day.date
  if (day.status === 'nonWorking') return `${date}, ledig${day.holidayName ? `, ${day.holidayName}` : ''}`
  if (day.status === 'unknown') return `${date}, registrerad tid ej hämtad`
  const base = `${date}, ${hours(day.logged)} av ${hours(day.expected)} timmar`
  return plannedHours > 0 ? `${base}, planerat ${hours(plannedHours)} timmar` : base
}

export function DayCell({
  day, plannedHours, selected, onPointerDown, onPointerEnter, onKeyDown, tabIndex = -1,
}: Props) {
  const nonWorking = day.status === 'nonWorking'
  const unknown = day.status === 'unknown'

  return (
    <button
      type="button"
      role="button"
      aria-pressed={selected}
      aria-label={accessibleName(day, plannedHours)}
      data-date={day.date}
      tabIndex={tabIndex}
      onPointerDown={onPointerDown}
      onPointerEnter={onPointerEnter}
      onKeyDown={onKeyDown}
      className="relative flex cursor-pointer flex-col overflow-hidden rounded-lg border p-2 text-left"
      style={{
        background: selected
          ? 'var(--sel-bg)'
          : unknown
            ? 'repeating-linear-gradient(135deg, var(--row-alt) 0 6px, var(--surface) 6px 12px)'
            : nonWorking ? 'var(--row-alt)' : 'var(--surface)',
        borderColor: selected ? 'var(--accent)' : 'var(--border)',
        boxShadow: selected ? 'inset 0 0 0 1px var(--accent)' : undefined,
        opacity: day.inMonth ? 1 : 0.4,
      }}
    >
      <span className="absolute inset-y-0 left-0 w-[3px]" style={{ background: STRIPE[day.status] }} />

      <span className="flex items-start justify-between gap-1.5">
        <span className="text-[15px] font-semibold" style={{ color: day.inMonth ? 'var(--fg)' : 'var(--subtle)' }}>
          {Number(day.date.slice(8, 10))}
        </span>
        {selected && plannedHours > 0 && (
          <span
            className="rounded px-1.5 py-0.5 text-[10px] font-semibold"
            style={{ background: 'var(--plan-bg)', color: 'var(--accent)' }}
          >
            +{hours(plannedHours)} h
          </span>
        )}
        {selected && plannedHours === 0 && !nonWorking && !unknown && (
          <span className="rounded px-1.5 py-0.5 text-[10px]" style={{ background: 'var(--chip)', color: 'var(--subtle)' }}>
            klar
          </span>
        )}
      </span>

      {nonWorking ? (
        <span className="mt-auto text-[11px]" style={{ color: 'var(--subtle)' }}>
          {day.holidayName ?? (day.inMonth ? 'Helg' : '')}
        </span>
      ) : unknown ? (
        <>
          <span className="mt-auto flex items-baseline gap-0.5">
            <span className="text-lg font-semibold leading-tight" style={{ color: 'var(--subtle)' }}>?</span>
            <span className="text-xs" style={{ color: 'var(--subtle)' }}>/ {hours(day.expected)} h</span>
          </span>
          <span className="mt-1 text-[10px]" style={{ color: 'var(--subtle)' }}>ej hämtad</span>
        </>
      ) : (
        <>
          <span className="mt-auto flex items-baseline gap-0.5">
            <span
              className="text-lg font-semibold leading-tight"
              style={{ color: day.status === 'empty' ? 'var(--subtle)' : 'var(--fg)' }}
            >
              {hours(day.logged)}
            </span>
            <span className="text-xs" style={{ color: 'var(--subtle)' }}>/ {hours(day.expected)} h</span>
          </span>
          <span className="mt-1 flex min-h-4 flex-wrap gap-1">
            {day.existing.slice(0, 3).map((log) => (
              <span
                key={log.id}
                title={log.workItemName ?? undefined}
                className="rounded px-1.5 py-0.5 text-[10px]"
                style={{ background: 'var(--chip)', color: 'var(--subtle)' }}
              >
                #{log.workItemId}
              </span>
            ))}
          </span>
        </>
      )}
    </button>
  )
}
```

- [ ] **Step 7: Implement the legend, status bar and icons**

`web/src/components/Legend.tsx`:

```tsx
const ITEMS = [
  ['var(--ok)', 'Klar'],
  ['var(--warn)', 'Delvis'],
  ['var(--idle)', 'Tom'],
  ['var(--over)', 'Över'],
  ['var(--row-alt)', 'Ledig'],
] as const

export function Legend() {
  return (
    <div className="flex items-center gap-3.5">
      {ITEMS.map(([color, label]) => (
        <span key={label} className="flex items-center gap-1.5 text-[11px]" style={{ color: 'var(--subtle)' }}>
          <span className="size-2 rounded-sm" style={{ background: color }} />
          {label}
        </span>
      ))}
    </div>
  )
}
```

`web/src/components/StatusBar.tsx`:

```tsx
import type { Month } from '../types'
import { formatMonth, hours } from '../dates'

export function StatusBar({ month }: { month: Month }) {
  const percent = month.totals.expected > 0
    ? Math.min(100, (month.totals.logged / month.totals.expected) * 100)
    : 0

  return (
    <div
      className="flex h-11 items-center gap-4 border-t px-4"
      style={{ borderColor: 'var(--border)', background: 'var(--surface)' }}
    >
      <span className="text-xs" style={{ color: 'var(--subtle)' }}>
        {formatMonth(month.year, month.month)}
      </span>
      <span className="text-[13px]">
        <strong className="font-semibold">{hours(month.totals.logged)}</strong> av{' '}
        {hours(month.totals.expected)} h loggade
      </span>
      <div className="flex h-1.5 flex-1 overflow-hidden rounded-full" style={{ background: 'var(--track)' }}>
        <div style={{ width: `${percent}%`, background: 'var(--accent)' }} />
      </div>
      <span className="text-[13px] font-semibold" style={{ color: 'var(--warn)' }}>
        {hours(month.totals.missing)} h saknas
      </span>
    </div>
  )
}
```

`web/src/components/Icons.tsx` — stroke-based 24-grid icons, no emoji:

```tsx
const base = {
  width: 16, height: 16, viewBox: '0 0 24 24', fill: 'none',
  stroke: 'currentColor', strokeWidth: 1.8, strokeLinecap: 'round' as const,
  strokeLinejoin: 'round' as const,
}

export const ChevronLeft = () => <svg {...base} width={18} height={18}><path d="M15 5 8 12l7 7" /></svg>
export const ChevronRight = () => <svg {...base} width={18} height={18}><path d="m9 5 7 7-7 7" /></svg>
export const Refresh = () => <svg {...base}><path d="M20 11a8 8 0 1 0-2.3 5.7" /><path d="M20 5v6h-6" /></svg>
export const Gear = () => (
  <svg {...base}>
    <circle cx="12" cy="12" r="3.2" />
    <path d="M12 2.5v2.6M12 18.9v2.6M21.5 12h-2.6M5.1 12H2.5M18.7 5.3l-1.8 1.8M7.1 16.9l-1.8 1.8M18.7 18.7l-1.8-1.8M7.1 7.1 5.3 5.3" />
  </svg>
)
export const Moon = () => <svg {...base}><path d="M20 14.5A8.5 8.5 0 0 1 9.5 4a8.5 8.5 0 1 0 10.5 10.5Z" /></svg>
export const Plus = () => <svg {...base} width={14} height={14}><path d="M12 5v14M5 12h14" /></svg>
export const Close = () => <svg {...base} width={14} height={14}><path d="M18 6 6 18M6 6l12 12" /></svg>
export const Check = () => <svg {...base} width={14} height={14}><path d="m4 12.5 5 5L20 6.5" /></svg>
export const Warning = () => (
  <svg {...base} width={18} height={18}><path d="M12 3.5 2.5 20h19L12 3.5Z" /><path d="M12 10v4" /><path d="M12 17.3v.1" /></svg>
)
```

- [ ] **Step 8: Implement the theme hook**

`web/src/useTheme.ts`:

```ts
import { useEffect } from 'react'
import type { Theme } from './types'

/**
 * Applies the three-way theme choice: System follows prefers-color-scheme, Light and Dark
 * pin it. The `dark` class on <html> is what theme.css keys off.
 */
export function useTheme(theme: Theme) {
  useEffect(() => {
    const query = window.matchMedia('(prefers-color-scheme: dark)')

    const apply = () => {
      const dark = theme === 'Dark' || (theme === 'System' && query.matches)
      document.documentElement.classList.toggle('dark', dark)
    }

    apply()
    if (theme !== 'System') return
    query.addEventListener('change', apply)
    return () => query.removeEventListener('change', apply)
  }, [theme])
}
```

- [ ] **Step 9: Write the failing month view tests**

`web/src/views/MonthView.test.tsx`:

```tsx
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { MonthView } from './MonthView'
import type { Month } from '../types'
import { datesBetween } from '../dates'

const monthPayload = (over: Partial<Month> = {}): Month => ({
  year: 2026, month: 6, from: '2026-06-01', to: '2026-07-05',
  loadState: 'loaded', error: null, holidayWarning: null,
  fetchedAt: '2026-06-30T12:00:00Z', dailyHours: 8,
  totals: { expected: 168, logged: 83, missing: 85 },
  days: datesBetween('2026-06-01', '2026-07-05').map((date) => ({
    date, expected: 8, logged: 0, remaining: 8, status: 'empty' as const,
    hitZeroFloor: false, isoWeek: 23, inMonth: date.startsWith('2026-06'),
    holidayName: null, existing: [],
  })),
  ...over,
})

vi.mock('../api', () => ({
  api: { month: vi.fn(), register: vi.fn(), workItems: vi.fn().mockResolvedValue([]) },
  ApiError: class extends Error {},
}))

const { api } = await import('../api')

beforeEach(() => vi.clearAllMocks())

describe('MonthView', () => {
  it('renders the fetched month, its totals and its title', async () => {
    vi.mocked(api.month).mockResolvedValue(monthPayload())

    render(<MonthView />)

    expect(await screen.findByText('Juni 2026')).toBeInTheDocument()
    expect(screen.getByText(/av 168 h loggade/)).toBeInTheDocument()
    expect(screen.getByText(/85 h saknas/)).toBeInTheDocument()
  })

  it('renders one cell per grid day, including the neighbouring month', async () => {
    vi.mocked(api.month).mockResolvedValue(monthPayload())

    render(<MonthView />)

    await waitFor(() => expect(screen.getAllByRole('button', { name: /2026-/ })).toHaveLength(35))
  })

  it('shows the week-number gutter', async () => {
    vi.mocked(api.month).mockResolvedValue(monthPayload())

    render(<MonthView />)

    expect(await screen.findByRole('button', { name: /vecka 23/i })).toBeInTheDocument()
  })

  it('steps to the next month and refetches', async () => {
    vi.mocked(api.month).mockResolvedValue(monthPayload())
    render(<MonthView />)
    await screen.findByText('Juni 2026')

    vi.mocked(api.month).mockResolvedValue(monthPayload({ year: 2026, month: 7 }))
    await userEvent.click(screen.getByRole('button', { name: 'Nästa månad' }))

    await waitFor(() => expect(api.month).toHaveBeenLastCalledWith(2026, 7))
  })

  it('warns when the holiday list could not be fetched', async () => {
    vi.mocked(api.month).mockResolvedValue(monthPayload({ holidayWarning: 'Kunde inte hämta röda dagar.' }))

    render(<MonthView />)

    expect(await screen.findByText(/Kunde inte hämta röda dagar/)).toBeInTheDocument()
  })

  it('shows the fetch failure without pretending the days are empty', async () => {
    vi.mocked(api.month).mockResolvedValue(monthPayload({
      loadState: 'failed',
      error: '7Pace API error 401: nope',
      days: monthPayload().days.map((d) => ({ ...d, status: 'unknown' as const })),
    }))

    render(<MonthView />)

    expect(await screen.findByText(/kunde inte hämtas/i)).toBeInTheDocument()
    expect(screen.getAllByText('ej hämtad').length).toBeGreaterThan(0)
  })
})
```

- [ ] **Step 10: Run the tests to verify they fail**

Run: `cd web && npm test`
Expected: FAIL — `./MonthView` does not exist.

- [ ] **Step 11: Implement the month view shell**

The selection behaviour arrives in Task 12 and the panel in Task 13; this step renders the
chrome, the gutter and the grid, and holds an empty selection.

`web/src/views/MonthView.tsx`:

```tsx
import { useCallback, useEffect, useState } from 'react'
import { api, ApiError } from '../api'
import type { Month } from '../types'
import { WEEKDAYS, addMonths, formatMonth, weekRows } from '../dates'
import { DayCell } from '../components/DayCell'
import { Legend } from '../components/Legend'
import { StatusBar } from '../components/StatusBar'
import { ChevronLeft, ChevronRight, Gear, Moon, Refresh, Warning } from '../components/Icons'

const today = new Date()

export function MonthView() {
  const [period, setPeriod] = useState({ year: today.getFullYear(), month: today.getMonth() + 1 })
  const [month, setMonth] = useState<Month | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)

  const load = useCallback(async () => {
    try {
      setMonth(await api.month(period.year, period.month))
      setLoadError(null)
    } catch (error) {
      setMonth(null)
      setLoadError(error instanceof ApiError ? error.message : 'Okänt fel.')
    }
  }, [period])

  useEffect(() => { void load() }, [load])

  const button = 'flex h-8 items-center gap-1.5 rounded-md border px-3 text-[13px]'
  const buttonStyle = { borderColor: 'var(--border)', background: 'var(--surface)', color: 'var(--fg)' }

  return (
    <div className="flex h-full flex-col">
      <header
        className="flex h-13 items-center justify-between gap-4 border-b px-4"
        style={{ borderColor: 'var(--border)', background: 'var(--surface)' }}
      >
        <div className="flex items-baseline gap-2.5">
          <span className="text-[15px] font-semibold">7Pace Desktop</span>
        </div>
        <div className="flex items-center gap-2">
          {month && (
            <span className="text-xs" style={{ color: 'var(--subtle)' }}>
              Hämtad {new Date(month.fetchedAt).toLocaleTimeString('sv-SE', { hour: '2-digit', minute: '2-digit' })}
            </span>
          )}
          <button type="button" className={button} style={buttonStyle} onClick={() => void load()}>
            <Refresh /> Uppdatera
          </button>
          <button type="button" aria-label="Inställningar" className={button} style={buttonStyle}><Gear /></button>
          <button type="button" aria-label="Tema" className={button} style={buttonStyle}><Moon /></button>
        </div>
      </header>

      <div className="flex h-13 items-center justify-between gap-4 border-b px-4" style={{ borderColor: 'var(--border)' }}>
        <div className="flex items-center gap-2.5">
          <button
            type="button" aria-label="Föregående månad" className={button} style={buttonStyle}
            onClick={() => setPeriod((p) => addMonths(p.year, p.month, -1))}
          >
            <ChevronLeft />
          </button>
          <span className="min-w-[118px] text-lg font-semibold">{formatMonth(period.year, period.month)}</span>
          <button
            type="button" aria-label="Nästa månad" className={button} style={buttonStyle}
            onClick={() => setPeriod((p) => addMonths(p.year, p.month, 1))}
          >
            <ChevronRight />
          </button>
          <button
            type="button" className={button} style={buttonStyle}
            onClick={() => setPeriod({ year: today.getFullYear(), month: today.getMonth() + 1 })}
          >
            Idag
          </button>
        </div>
        <Legend />
      </div>

      {loadError && (
        <div className="flex items-center gap-2 px-4 py-2 text-[13px]" style={{ color: 'var(--danger)' }}>
          <Warning /> {loadError}
        </div>
      )}

      {month?.holidayWarning && (
        <div className="px-4 py-2 text-[13px]" style={{ color: 'var(--warn)' }}>{month.holidayWarning}</div>
      )}

      {month?.loadState === 'failed' && (
        <div
          className="mx-4 mt-2 flex gap-2.5 rounded-lg border p-3"
          style={{ borderColor: 'var(--danger)', background: 'var(--danger-bg)' }}
        >
          <span style={{ color: 'var(--danger)' }}><Warning /></span>
          <div className="flex flex-col gap-1">
            <span className="text-[13px] font-semibold">Registrerad tid kunde inte hämtas</span>
            <span className="text-xs leading-relaxed" style={{ color: 'var(--subtle)' }}>
              Appen vet inte vad som redan är loggat och skulle riskera att dubbelregistrera.
              Uppdatera för att försöka igen. {month.error}
            </span>
          </div>
        </div>
      )}

      <div className="flex min-h-0 flex-1">
        {month && (
          <div className="flex min-w-0 flex-1 flex-col gap-1.5 p-4">
            <div className="flex gap-1.5">
              <div className="w-8.5 shrink-0 text-center text-[11px] font-semibold uppercase" style={{ color: 'var(--subtle)' }}>v</div>
              <div className="grid min-w-0 flex-1 grid-cols-7 gap-1.5">
                {WEEKDAYS.map((weekday) => (
                  <div key={weekday} className="px-1 pb-0.5 text-[11px] font-semibold uppercase tracking-wide" style={{ color: 'var(--subtle)' }}>
                    {weekday}
                  </div>
                ))}
              </div>
            </div>

            <div className="flex min-h-0 flex-1 gap-1.5">
              <div className="grid w-8.5 shrink-0 gap-1.5" style={{ gridTemplateRows: `repeat(${weekRows(month.days).length}, minmax(0, 1fr))` }}>
                {weekRows(month.days).map((row) => (
                  <button
                    key={row[0].date}
                    type="button"
                    aria-label={`Vecka ${row[0].isoWeek}`}
                    className="flex items-center justify-center rounded-md text-xs font-semibold"
                    style={{ color: 'var(--subtle)' }}
                  >
                    {row[0].isoWeek}
                  </button>
                ))}
              </div>

              <div
                className="grid min-w-0 flex-1 grid-cols-7 gap-1.5"
                style={{ gridTemplateRows: `repeat(${weekRows(month.days).length}, minmax(0, 1fr))` }}
              >
                {month.days.map((day) => (
                  <DayCell key={day.date} day={day} plannedHours={0} selected={false} />
                ))}
              </div>
            </div>
          </div>
        )}
      </div>

      {month && <StatusBar month={month} />}
    </div>
  )
}
```

- [ ] **Step 12: Point App at the month view**

`web/src/App.tsx`:

```tsx
import { MonthView } from './views/MonthView'

export function App() {
  return <MonthView />
}
```

- [ ] **Step 13: Run the tests to verify they pass**

Run: `cd web && npm test`
Expected: PASS — the date, day-cell and month-view suites are green.

- [ ] **Step 14: Look at it**

Run the server and the dev front end in two shells:

```bash
ASPNETCORE_URLS=http://127.0.0.1:5111 dotnet run --project src/7PaceDesktop.Server
cd web && npm run dev
```

Open `http://127.0.0.1:5173`. Confirm the month renders, the cells read correctly in both light and
dark (toggle the OS theme), and nothing overflows horizontally at 1280 px wide.

- [ ] **Step 15: Commit**

```bash
git add web
git commit -m "feat: month calendar rendering with day states and totals"
```

---

### Task 12: Day selection

**Files:**
- Create: `web/src/selection.ts`, `web/src/selection.test.ts`
- Modify: `web/src/views/MonthView.tsx`
- Modify: `web/src/views/MonthView.test.tsx`

**Interfaces:**
- Consumes: `Month`, `Day` from `types.ts`; `datesBetween` from `dates.ts`.
- Produces:
  - `interface SelectionState { selected: string[]; anchor: string | null }`
  - `type SelectionAction` — `{type:'dragStart',date}` | `{type:'dragTo',date}` | `{type:'dragEnd'}` | `{type:'toggle',date}` | `{type:'set',dates}` | `{type:'clear'}`
  - `selectionReducer(state, action) -> SelectionState`
  - `emptyWorkdays(month) -> string[]`, `weekDates(month, isoWeek) -> string[]`, `monthWorkdays(month) -> string[]`
  - `plannedFor(month, date) -> number`, `summarize(month, selected) -> FillSummaryView`
  - `interface FillSummaryView { emptyDays: number; partialDays: number; skippedDays: number; totalHours: number }`

`plannedFor` and `summarize` use only `max(0, expected - logged)`, which the server has already
computed as `day.remaining`. The split and rounding rules stay in `FillPlanner`.

- [ ] **Step 0: Make the server's dev port configurable**

This is an out-of-band addition, not part of the original Task 12 scope. Task 6 hard-bound Kestrel
with `builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0))`, which
overrides `ASPNETCORE_URLS` entirely. That is the correct security posture for the address, but it
also means the port is always ephemeral — so the dev workflow documented in `web/vite.config.ts`
(`proxy: { '/api': 'http://127.0.0.1:5111' }`) can never reach the server, and Task 13's manual
verification step needs it.

Fix it in `src/7PaceDesktop.Server/Program.cs`, replacing the single `Listen` line:

```csharp
// Loopback-only is the security property and is not configurable. The PORT is, so the Vite dev
// proxy can target a known one; 0 asks the OS for a free port, which is what a real run uses.
var port = int.TryParse(builder.Configuration["Port"], out var configured) ? configured : 0;
builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, port));
```

Then update the comment in `web/vite.config.ts` so it documents the command that actually works:

```
    // In development the API lives on the dotnet server; run it with
    // dotnet run --project src/7PaceDesktop.Server -- --Port=5111
```

Add a test to `tests/7PaceDesktop.Tests/ServerSmokeTests.cs` (or the fixture's existing file)
asserting that the configuration key is read — the fixture hosts via `TestServer`, so assert on
`Configuration["Port"]` round-tripping through `UseSetting`, in the same shape Task 15 uses for
`OpenBrowser`. Do not attempt to assert a real bound port under `WebApplicationFactory`.

Keep `IPAddress.Loopback` hard-coded. The port is not a security boundary; the address is.

- [ ] **Step 1: Write the failing selection tests**

`web/src/selection.test.ts`:

```ts
import { describe, expect, it } from 'vitest'
import {
  emptyWorkdays, monthWorkdays, plannedFor, selectionReducer, summarize, weekDates,
} from './selection'
import type { Day, DayStatus, Month } from './types'
import { datesBetween } from './dates'

// The grid starts Monday 2026-06-01, which is ISO week 23, so the week number follows the
// date's offset from that Monday. Deriving it from the day-of-month instead would give
// 2026-07-01..05 the same week 23 as 2026-06-01..07, and weekDates would return both runs.
const GRID_START = Date.UTC(2026, 5, 1)
const isoWeekFor = (date: string) => {
  const utc = Date.UTC(Number(date.slice(0, 4)), Number(date.slice(5, 7)) - 1, Number(date.slice(8, 10)))
  return 23 + Math.floor((utc - GRID_START) / 604800000)
}

const day = (date: string, over: Partial<Day> = {}): Day => ({
  date, expected: 8, logged: 0, remaining: 8, status: 'empty', hitZeroFloor: false,
  isoWeek: isoWeekFor(date), inMonth: date.startsWith('2026-06'),
  holidayName: null, existing: [], ...over,
})

const nonWorking = (date: string): Partial<Day> =>
  ({ status: 'nonWorking' as DayStatus, expected: 0, remaining: 0 })

const month = (over: Partial<Day>[] = []): Month => {
  const days = datesBetween('2026-06-01', '2026-07-05').map((d) => day(d))
  // Weekends are non-working.
  for (const d of days) {
    const weekday = new Date(`${d.date}T00:00:00Z`).getUTCDay()
    if (weekday === 0 || weekday === 6) Object.assign(d, nonWorking(d.date))
  }
  for (const patch of over) {
    const target = days.find((d) => d.date === patch.date)
    if (target) Object.assign(target, patch)
  }
  return {
    year: 2026, month: 6, from: '2026-06-01', to: '2026-07-05', loadState: 'loaded',
    error: null, holidayWarning: null, fetchedAt: '2026-06-30T12:00:00Z', dailyHours: 8,
    totals: { expected: 168, logged: 0, missing: 168 }, days,
  }
}

const empty = { selected: [], anchor: null }

describe('selectionReducer', () => {
  it('starts a drag on one day', () => {
    const state = selectionReducer(empty, { type: 'dragStart', date: '2026-06-22' })

    expect(state.selected).toEqual(['2026-06-22'])
    expect(state.anchor).toBe('2026-06-22')
  })

  it('extends a drag to a range, in either direction', () => {
    let state = selectionReducer(empty, { type: 'dragStart', date: '2026-06-24' })
    state = selectionReducer(state, { type: 'dragTo', date: '2026-06-26' })
    expect(state.selected).toEqual(['2026-06-24', '2026-06-25', '2026-06-26'])

    state = selectionReducer(state, { type: 'dragTo', date: '2026-06-22' })
    expect(state.selected).toEqual(['2026-06-22', '2026-06-23', '2026-06-24'])
  })

  it('ignores dragTo when no drag is in progress', () => {
    expect(selectionReducer(empty, { type: 'dragTo', date: '2026-06-22' })).toEqual(empty)
  })

  it('clears the anchor on dragEnd but keeps the selection', () => {
    let state = selectionReducer(empty, { type: 'dragStart', date: '2026-06-22' })
    state = selectionReducer(state, { type: 'dragEnd' })

    expect(state.selected).toEqual(['2026-06-22'])
    expect(state.anchor).toBeNull()
  })

  it('toggles a single day without disturbing the rest', () => {
    let state = selectionReducer(empty, { type: 'set', dates: ['2026-06-22', '2026-06-23'] })
    state = selectionReducer(state, { type: 'toggle', date: '2026-06-25' })
    expect(state.selected).toEqual(['2026-06-22', '2026-06-23', '2026-06-25'])

    state = selectionReducer(state, { type: 'toggle', date: '2026-06-22' })
    expect(state.selected).toEqual(['2026-06-23', '2026-06-25'])
  })

  it('sorts and de-duplicates a set', () => {
    const state = selectionReducer(empty, {
      type: 'set', dates: ['2026-06-25', '2026-06-22', '2026-06-25'],
    })

    expect(state.selected).toEqual(['2026-06-22', '2026-06-25'])
  })

  it('clears everything', () => {
    let state = selectionReducer(empty, { type: 'set', dates: ['2026-06-22'] })
    state = selectionReducer(state, { type: 'clear' })

    expect(state).toEqual(empty)
  })
})

describe('bulk selectors', () => {
  it('selects only unfilled workdays of the month', () => {
    const m = month([
      { date: '2026-06-03', status: 'partial', logged: 6, remaining: 2 },
      { date: '2026-06-04', status: 'complete', logged: 8, remaining: 0 },
    ])

    const dates = emptyWorkdays(m)

    expect(dates).not.toContain('2026-06-06')   // Saturday
    expect(dates).not.toContain('2026-06-03')   // partial, not empty
    expect(dates).not.toContain('2026-06-04')   // complete
    expect(dates).not.toContain('2026-07-01')   // outside the month
    expect(dates).toContain('2026-06-01')
  })

  it('selects a whole week, including its weekend cells', () => {
    expect(weekDates(month(), 23)).toEqual(datesBetween('2026-06-01', '2026-06-07'))
  })

  it('selects every workday of the month', () => {
    const dates = monthWorkdays(month())

    expect(dates).toHaveLength(22)               // June 2026 weekdays: it starts Monday and ends Tuesday
    expect(dates).not.toContain('2026-06-07')
  })
})

describe('preview', () => {
  it('plans the shortfall for a day, and nothing for days that need nothing', () => {
    const m = month([
      { date: '2026-06-24', status: 'partial', logged: 3, remaining: 5 },
      { date: '2026-06-25', status: 'complete', logged: 8, remaining: 0 },
      { date: '2026-06-19', ...nonWorking('2026-06-19'), holidayName: 'Midsommarafton' },
    ])

    expect(plannedFor(m, '2026-06-22')).toBe(8)
    expect(plannedFor(m, '2026-06-24')).toBe(5)
    expect(plannedFor(m, '2026-06-25')).toBe(0)
    expect(plannedFor(m, '2026-06-19')).toBe(0)
  })

  it('plans nothing for an unknown day, so a failed fetch cannot cause a top-up', () => {
    const m = month([{ date: '2026-06-22', status: 'unknown', logged: 0, remaining: 8 }])

    expect(plannedFor(m, '2026-06-22')).toBe(0)
  })

  it('summarises a mixed selection the way the server will', () => {
    const m = month([
      { date: '2026-06-24', status: 'partial', logged: 3, remaining: 5 },
      { date: '2026-06-25', status: 'complete', logged: 8, remaining: 0 },
    ])

    const summary = summarize(m, ['2026-06-22', '2026-06-23', '2026-06-24', '2026-06-25', '2026-06-26'])

    expect(summary).toEqual({ emptyDays: 3, partialDays: 1, skippedDays: 1, totalHours: 29 })
  })

  it('leaves non-working days out of the summary entirely', () => {
    const summary = summarize(month(), ['2026-06-06', '2026-06-07'])

    expect(summary).toEqual({ emptyDays: 0, partialDays: 0, skippedDays: 0, totalHours: 0 })
  })
})
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd web && npm test`
Expected: FAIL — `./selection` does not exist.

- [ ] **Step 3: Implement the selection module**

`web/src/selection.ts`:

```ts
import { datesBetween } from './dates'
import type { Month } from './types'

export interface SelectionState {
  selected: string[]
  anchor: string | null
}

export type SelectionAction =
  | { type: 'dragStart'; date: string }
  | { type: 'dragTo'; date: string }
  | { type: 'dragEnd' }
  | { type: 'toggle'; date: string }
  | { type: 'set'; dates: string[] }
  | { type: 'clear' }

const normalise = (dates: string[]) => [...new Set(dates)].sort()

export function selectionReducer(state: SelectionState, action: SelectionAction): SelectionState {
  switch (action.type) {
    case 'dragStart':
      return { selected: [action.date], anchor: action.date }
    case 'dragTo':
      // A dragTo without an anchor means the pointer entered a cell with no drag in progress.
      return state.anchor ? { ...state, selected: datesBetween(state.anchor, action.date) } : state
    case 'dragEnd':
      return { ...state, anchor: null }
    case 'toggle':
      return {
        ...state,
        selected: state.selected.includes(action.date)
          ? state.selected.filter((d) => d !== action.date)
          : normalise([...state.selected, action.date]),
      }
    case 'set':
      return { selected: normalise(action.dates), anchor: null }
    case 'clear':
      return { selected: [], anchor: null }
  }
}

/** Unfilled workdays of the displayed month, ignoring the grid's neighbouring-month cells. */
export const emptyWorkdays = (month: Month) =>
  month.days.filter((d) => d.inMonth && d.status === 'empty').map((d) => d.date)

export const monthWorkdays = (month: Month) =>
  month.days.filter((d) => d.inMonth && d.status !== 'nonWorking').map((d) => d.date)

export const weekDates = (month: Month, isoWeek: number) =>
  month.days.filter((d) => d.isoWeek === isoWeek).map((d) => d.date)

/**
 * Hours this day would receive. Only the day total is computed here - how it splits across
 * work items, and how rounding residuals land, is FillPlanner's job on the server.
 */
export function plannedFor(month: Month, date: string): number {
  const day = month.days.find((d) => d.date === date)
  if (!day) return 0
  if (day.status === 'nonWorking' || day.status === 'unknown') return 0
  return day.remaining
}

export interface FillSummaryView {
  emptyDays: number
  partialDays: number
  skippedDays: number
  totalHours: number
}

export function summarize(month: Month, selected: string[]): FillSummaryView {
  let emptyDays = 0
  let partialDays = 0
  let skippedDays = 0
  let totalHours = 0

  for (const date of selected) {
    const day = month.days.find((d) => d.date === date)
    if (!day || day.status === 'nonWorking' || day.status === 'unknown') continue

    if (day.remaining <= 0) {
      skippedDays += 1
      continue
    }
    if (day.status === 'empty') emptyDays += 1
    else partialDays += 1
    totalHours += day.remaining
  }

  return { emptyDays, partialDays, skippedDays, totalHours: Math.round(totalHours * 100) / 100 }
}
```

- [ ] **Step 4: Write the failing interaction tests**

Append to `web/src/views/MonthView.test.tsx`:

```tsx
  it('selects a range by dragging across cells', async () => {
    vi.mocked(api.month).mockResolvedValue(monthPayload())
    render(<MonthView />)
    await screen.findByText('Juni 2026')

    const from = screen.getByRole('button', { name: /2026-06-22/ })
    const to = screen.getByRole('button', { name: /2026-06-24/ })
    await userEvent.pointer([
      { keys: '[MouseLeft>]', target: from },
      { target: to },
      { keys: '[/MouseLeft]' },
    ])

    expect(screen.getByRole('button', { name: /2026-06-23/ })).toHaveAttribute('aria-pressed', 'true')
    expect(screen.getByRole('button', { name: /2026-06-25/ })).toHaveAttribute('aria-pressed', 'false')
  })

  it('toggles a single day with ctrl-click without clearing the rest', async () => {
    vi.mocked(api.month).mockResolvedValue(monthPayload())
    render(<MonthView />)
    await screen.findByText('Juni 2026')

    await userEvent.click(screen.getByRole('button', { name: /2026-06-22/ }))
    await userEvent.keyboard('{Control>}')
    await userEvent.click(screen.getByRole('button', { name: /2026-06-25/ }))
    await userEvent.keyboard('{/Control}')

    expect(screen.getByRole('button', { name: /2026-06-22/ })).toHaveAttribute('aria-pressed', 'true')
    expect(screen.getByRole('button', { name: /2026-06-25/ })).toHaveAttribute('aria-pressed', 'true')
  })

  it('selects a whole week from the gutter', async () => {
    vi.mocked(api.month).mockResolvedValue(monthPayload())
    render(<MonthView />)
    await screen.findByText('Juni 2026')

    await userEvent.click(screen.getByRole('button', { name: /vecka 23/i }))

    expect(screen.getByRole('button', { name: /2026-06-01/ })).toHaveAttribute('aria-pressed', 'true')
    expect(screen.getByRole('button', { name: /2026-06-07/ })).toHaveAttribute('aria-pressed', 'true')
  })

  it('selects every unfilled workday, then clears', async () => {
    vi.mocked(api.month).mockResolvedValue(monthPayload())
    render(<MonthView />)
    await screen.findByText('Juni 2026')

    await userEvent.click(screen.getByRole('button', { name: 'Alla tomma dagar' }))
    expect(screen.getByRole('button', { name: /2026-06-01/ })).toHaveAttribute('aria-pressed', 'true')

    await userEvent.click(screen.getByRole('button', { name: 'Rensa markering' }))
    expect(screen.getByRole('button', { name: /2026-06-01/ })).toHaveAttribute('aria-pressed', 'false')
  })

  it('toggles the focused day with the space key', async () => {
    vi.mocked(api.month).mockResolvedValue(monthPayload())
    render(<MonthView />)
    await screen.findByText('Juni 2026')

    const cell = screen.getByRole('button', { name: /2026-06-22/ })
    cell.focus()
    await userEvent.keyboard(' ')

    expect(cell).toHaveAttribute('aria-pressed', 'true')
  })

  it('badges the planned top-up on selected days', async () => {
    vi.mocked(api.month).mockResolvedValue(monthPayload())
    render(<MonthView />)
    await screen.findByText('Juni 2026')

    await userEvent.click(screen.getByRole('button', { name: /2026-06-22/ }))

    expect(screen.getByText('+8 h')).toBeInTheDocument()
  })
```

- [ ] **Step 5: Run the tests to verify they fail**

Run: `cd web && npm test`
Expected: FAIL — the cells do not respond to selection yet.

- [ ] **Step 6: Wire the interaction into MonthView**

In `web/src/views/MonthView.tsx`, add the imports:

```tsx
import { useReducer, useRef } from 'react'
import {
  emptyWorkdays, monthWorkdays, plannedFor, selectionReducer, weekDates,
} from '../selection'
```

Add the selection state and handlers inside the component, after `load`:

```tsx
  const [selection, dispatch] = useReducer(selectionReducer, { selected: [], anchor: null })
  const dragging = useRef(false)

  // The selection resets when the period changes: dates outside the grid cannot be registered.
  useEffect(() => { dispatch({ type: 'clear' }) }, [period])

  useEffect(() => {
    const stop = () => {
      dragging.current = false
      dispatch({ type: 'dragEnd' })
    }
    window.addEventListener('pointerup', stop)
    return () => window.removeEventListener('pointerup', stop)
  }, [])

  const onCellPointerDown = (date: string) => (event: React.PointerEvent) => {
    event.preventDefault()
    if (event.ctrlKey || event.metaKey) {
      dispatch({ type: 'toggle', date })
      return
    }
    dragging.current = true
    dispatch({ type: 'dragStart', date })
  }

  const onCellPointerEnter = (date: string) => () => {
    if (dragging.current) dispatch({ type: 'dragTo', date })
  }

  const onCellKeyDown = (date: string) => (event: React.KeyboardEvent) => {
    if (event.key === ' ' || event.key === 'Enter') {
      event.preventDefault()
      dispatch({ type: 'toggle', date })
      return
    }
    if (event.key === 'a' && (event.ctrlKey || event.metaKey) && month) {
      event.preventDefault()
      dispatch({ type: 'set', dates: monthWorkdays(month) })
      return
    }
    const step = { ArrowLeft: -1, ArrowRight: 1, ArrowUp: -7, ArrowDown: 7 }[event.key]
    if (step === undefined || !month) return
    event.preventDefault()
    const index = month.days.findIndex((d) => d.date === date) + step
    const next = month.days[index]
    if (!next) return
    const target = document.querySelector<HTMLButtonElement>(`[data-date="${next.date}"]`)
    target?.focus()
    if (event.shiftKey && selection.selected.length > 0) {
      dispatch({ type: 'set', dates: datesBetween(selection.selected[0], next.date) })
    }
  }
```

Add `datesBetween` to the `../dates` import.

Give the two bulk buttons real behaviour in the month bar, after the `Idag` button:

```tsx
          <span className="h-5.5 w-px" style={{ background: 'var(--border)' }} />
          <button
            type="button" className={button} style={buttonStyle}
            disabled={!month}
            onClick={() => month && dispatch({ type: 'set', dates: emptyWorkdays(month) })}
          >
            Alla tomma dagar
          </button>
          <button type="button" className={button} style={buttonStyle} onClick={() => dispatch({ type: 'clear' })}>
            Rensa markering
          </button>
```

Make each week-gutter button select its week:

```tsx
                  <button
                    key={row[0].date}
                    type="button"
                    aria-label={`Vecka ${row[0].isoWeek}`}
                    onClick={() => dispatch({ type: 'set', dates: weekDates(month, row[0].isoWeek) })}
                    className="flex items-center justify-center rounded-md text-xs font-semibold"
                    style={{ color: 'var(--subtle)' }}
                  >
                    {row[0].isoWeek}
                  </button>
```

And pass selection state and handlers to each cell:

```tsx
                {month.days.map((day, index) => (
                  <DayCell
                    key={day.date}
                    day={day}
                    plannedHours={selection.selected.includes(day.date) ? plannedFor(month, day.date) : 0}
                    selected={selection.selected.includes(day.date)}
                    tabIndex={index === 0 ? 0 : -1}
                    onPointerDown={onCellPointerDown(day.date)}
                    onPointerEnter={onCellPointerEnter(day.date)}
                    onKeyDown={onCellKeyDown(day.date)}
                  />
                ))}
```

Expose the selection to the next task by keeping `selection` and `dispatch` in this component;
Task 13 renders the panel beside the grid using them.

- [ ] **Step 7: Run the tests to verify they pass**

Run: `cd web && npm test`
Expected: PASS — selection and month-view suites green.

- [ ] **Step 8: Try the drag by hand**

With the dev servers running, drag across a week, ctrl-click a day out of the range, click a week
number, and press `Alla tomma dagar`. Confirm the planned badges appear and that dragging never
selects text.

- [ ] **Step 9: Commit**

```bash
git add web
git commit -m "feat: drag, week and bulk day selection"
```

---

### Task 13: Selection panel and registering

**Files:**
- Create: `web/src/views/SelectionPanel.tsx`, `web/src/views/SelectionPanel.test.tsx`
- Modify: `web/src/views/MonthView.tsx`

**Interfaces:**
- Consumes: `summarize`, `SelectionState` from `selection.ts`; `api.register`, `api.workItems`, `ApiError`; `Month`, `WorkItem`, `FillLine`, `RegisterResponse`.
- Produces:
  - `<SelectionPanel month workItems selected onRegistered onClear />`
  - `onRegistered(response: RegisterResponse)` — the month view refetches and shows the outcome.

- [ ] **Step 1: Write the failing tests**

`web/src/views/SelectionPanel.test.tsx`:

```tsx
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { SelectionPanel } from './SelectionPanel'
import type { Month, WorkItem } from '../types'
import { datesBetween } from '../dates'

vi.mock('../api', () => ({
  api: { register: vi.fn() },
  ApiError: class ApiError extends Error {
    constructor(public status: number, message: string) { super(message) }
  },
}))
const { api } = await import('../api')

const workItems: WorkItem[] = [
  { id: 12345, name: 'Sprintarbete', isFavorite: true },
  { id: 12401, name: 'Support', isFavorite: false },
]

const month = (loadState: 'loaded' | 'failed' = 'loaded'): Month => ({
  year: 2026, month: 6, from: '2026-06-01', to: '2026-07-05', loadState,
  error: loadState === 'failed' ? 'nope' : null, holidayWarning: null,
  fetchedAt: '2026-06-30T12:00:00Z', dailyHours: 8,
  totals: { expected: 168, logged: 0, missing: 168 },
  days: datesBetween('2026-06-01', '2026-07-05').map((date) => ({
    date, expected: 8, logged: 0, remaining: 8,
    status: loadState === 'failed' ? ('unknown' as const) : ('empty' as const),
    hitZeroFloor: false, isoWeek: 26, inMonth: true, holidayName: null, existing: [],
  })),
})

const panel = (over: Partial<Parameters<typeof SelectionPanel>[0]> = {}) =>
  render(
    <SelectionPanel
      month={month()}
      workItems={workItems}
      selected={['2026-06-22', '2026-06-23']}
      onRegistered={vi.fn()}
      onClear={vi.fn()}
      {...over}
    />,
  )

beforeEach(() => vi.clearAllMocks())

describe('SelectionPanel', () => {
  it('reports the selected day count and the hours to register', () => {
    panel()

    expect(screen.getByText('2 dagar valda')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Registrera 16 h/ })).toBeEnabled()
  })

  it('seeds one line on the favourite work item at the full daily target', () => {
    panel()

    expect(screen.getByRole('combobox')).toHaveValue('12345')
    expect(screen.getByLabelText(/timmar för rad 1/i)).toHaveValue(8)
  })

  it('blocks registering until the lines sum to the daily target', async () => {
    panel()

    await userEvent.click(screen.getByRole('button', { name: /Lägg till work item/ }))

    // The new line starts at 0 h, so the split now sums to 8 of 8 and stays balanced.
    await userEvent.clear(screen.getByLabelText(/timmar för rad 1/i))
    await userEvent.type(screen.getByLabelText(/timmar för rad 1/i), '5')

    expect(screen.getByText(/5 av 8 h/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Registrera/ })).toBeDisabled()
  })

  it('summarises empty, partial and skipped days', () => {
    const m = month()
    m.days.find((d) => d.date === '2026-06-24')!.logged = 3
    m.days.find((d) => d.date === '2026-06-24')!.remaining = 5
    m.days.find((d) => d.date === '2026-06-24')!.status = 'partial'
    m.days.find((d) => d.date === '2026-06-25')!.logged = 8
    m.days.find((d) => d.date === '2026-06-25')!.remaining = 0
    m.days.find((d) => d.date === '2026-06-25')!.status = 'complete'

    panel({ month: m, selected: ['2026-06-22', '2026-06-23', '2026-06-24', '2026-06-25', '2026-06-26'] })

    expect(screen.getByText(/3 tomma dagar/)).toBeInTheDocument()
    expect(screen.getByText(/1 delvis dag/)).toBeInTheDocument()
    expect(screen.getByText(/hoppas över/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Registrera 29 h/ })).toBeInTheDocument()
  })

  it('posts the selection and the lines, then reports back', async () => {
    const onRegistered = vi.fn()
    vi.mocked(api.register).mockResolvedValue({
      postedEntries: 2, failedEntries: 0, skippedDays: 0, totalHours: 16,
      days: [
        { date: '2026-06-22', hours: 8, status: 'ok', error: null },
        { date: '2026-06-23', hours: 8, status: 'ok', error: null },
      ],
    })
    panel({ onRegistered })

    await userEvent.click(screen.getByRole('button', { name: /Registrera 16 h/ }))

    await waitFor(() => expect(api.register).toHaveBeenCalledWith({
      dates: ['2026-06-22', '2026-06-23'],
      lines: [{ workItemId: 12345, hours: 8 }],
      simulate: false,
    }))
    expect(onRegistered).toHaveBeenCalled()
  })

  it('passes the simulate flag when the box is ticked', async () => {
    vi.mocked(api.register).mockResolvedValue({
      postedEntries: 2, failedEntries: 0, skippedDays: 0, totalHours: 16, days: [],
    })
    panel()

    await userEvent.click(screen.getByLabelText(/Simulera/))
    await userEvent.click(screen.getByRole('button', { name: /Registrera/ }))

    await waitFor(() => expect(vi.mocked(api.register).mock.calls[0][0].simulate).toBe(true))
  })

  it('shows per-day failures after a partial success', async () => {
    vi.mocked(api.register).mockResolvedValue({
      postedEntries: 1, failedEntries: 1, skippedDays: 0, totalHours: 16,
      days: [
        { date: '2026-06-22', hours: 8, status: 'ok', error: null },
        { date: '2026-06-23', hours: 8, status: 'failed', error: '7Pace API error 500: boom' },
      ],
    })
    panel()

    await userEvent.click(screen.getByRole('button', { name: /Registrera/ }))

    expect(await screen.findByText(/2026-06-23/)).toBeInTheDocument()
    expect(screen.getByText(/500/)).toBeInTheDocument()
  })

  it('says nothing was registered when the server refuses on a stale read', async () => {
    const { ApiError } = await import('../api')
    vi.mocked(api.register).mockRejectedValue(new ApiError(409, 'Kunde inte hämta redan registrerad tid.'))
    panel()

    await userEvent.click(screen.getByRole('button', { name: /Registrera/ }))

    expect(await screen.findByText(/Kunde inte hämta redan registrerad tid/)).toBeInTheDocument()
  })

  it('blocks registering entirely when the month could not be fetched', () => {
    panel({ month: month('failed') })

    expect(screen.getByRole('button', { name: /Registrera/ })).toBeDisabled()
    expect(screen.getByText(/kunde inte hämtas/i)).toBeInTheDocument()
    expect(screen.getByText('— h')).toBeInTheDocument()
  })

  it('prompts to select days when nothing is selected', () => {
    panel({ selected: [] })

    expect(screen.getByText(/Dra i kalendern/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Registrera/ })).toBeDisabled()
  })
})
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd web && npm test`
Expected: FAIL — `./SelectionPanel` does not exist.

- [ ] **Step 3: Implement the panel**

`web/src/views/SelectionPanel.tsx`:

```tsx
import { useEffect, useMemo, useState } from 'react'
import { api, ApiError } from '../api'
import type { FillLine, Month, RegisterResponse, WorkItem } from '../types'
import { hours } from '../dates'
import { summarize } from '../selection'
import { Check, Close, Plus, Refresh, Warning } from '../components/Icons'

interface Props {
  month: Month
  workItems: WorkItem[]
  selected: string[]
  onRegistered: (response: RegisterResponse) => void
  onClear: () => void
}

const EPSILON = 0.001

export function SelectionPanel({ month, workItems, selected, onRegistered, onClear }: Props) {
  const favourite = workItems.find((w) => w.isFavorite) ?? workItems[0]
  const [lines, setLines] = useState<FillLine[]>([])
  const [simulate, setSimulate] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [result, setResult] = useState<RegisterResponse | null>(null)

  // One line on the favourite at the full target is the common case, so it is the default.
  useEffect(() => {
    if (favourite) setLines([{ workItemId: favourite.id, hours: month.dailyHours }])
  }, [favourite, month.dailyHours])

  const summary = useMemo(() => summarize(month, selected), [month, selected])
  const linesSum = lines.reduce((total, line) => total + line.hours, 0)
  const balanced = Math.abs(linesSum - month.dailyHours) <= EPSILON
  const blocked = month.loadState === 'failed'
  const canRegister = !blocked && !busy && balanced && selected.length > 0 && summary.totalHours > 0

  const update = (index: number, patch: Partial<FillLine>) =>
    setLines((current) => current.map((line, i) => (i === index ? { ...line, ...patch } : line)))

  async function register() {
    setBusy(true)
    setError(null)
    setResult(null)
    try {
      const response = await api.register({ dates: selected, lines, simulate })
      setResult(response)
      onRegistered(response)
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'Okänt fel.')
    } finally {
      setBusy(false)
    }
  }

  const label = 'text-[11px] font-semibold uppercase tracking-wide'
  const field = 'h-8 rounded-md border px-2 text-[13px]'
  const fieldStyle = { borderColor: 'var(--border)', background: 'var(--surface)', color: 'var(--fg)' }

  return (
    <aside
      className="flex w-85 shrink-0 flex-col gap-3.5 overflow-y-auto border-l p-4"
      style={{ borderColor: 'var(--border)', background: 'var(--surface)' }}
    >
      <div className="flex flex-col gap-1">
        <span className="text-[15px] font-semibold">
          {selected.length === 0
            ? 'Inga dagar valda'
            : `${selected.length} ${selected.length === 1 ? 'dag' : 'dagar'} valda`}
        </span>
        <span className="text-xs" style={{ color: 'var(--subtle)' }}>
          {selected.length === 0
            ? 'Dra i kalendern för att välja dagar.'
            : `${selected[0]} – ${selected[selected.length - 1]}`}
        </span>
      </div>

      {blocked && (
        <div
          className="flex gap-2.5 rounded-lg border p-3"
          style={{ borderColor: 'var(--danger)', background: 'var(--danger-bg)' }}
        >
          <span className="shrink-0" style={{ color: 'var(--danger)' }}><Warning /></span>
          <div className="flex flex-col gap-1">
            <span className="text-[13px] font-semibold">Registrerad tid kunde inte hämtas</span>
            <span className="text-xs leading-relaxed" style={{ color: 'var(--subtle)' }}>
              Appen vet inte vad som redan är loggat och skulle riskera att dubbelregistrera.
              Uppdatera för att försöka igen.
            </span>
          </div>
        </div>
      )}

      <div className="flex flex-col gap-2">
        <div className={label} style={{ color: 'var(--subtle)' }}>Mål per dag</div>
        <div className="flex items-center gap-2">
          <span className={field} style={{ ...fieldStyle, lineHeight: '2rem', width: '4.5rem', textAlign: 'right' }}>
            {hours(month.dailyHours)} h
          </span>
          <span className="text-[11px] leading-snug" style={{ color: 'var(--subtle)' }}>
            Från inställningar. Röda dagar och dagen före kortas automatiskt.
          </span>
        </div>
      </div>

      <div className="h-px" style={{ background: 'var(--border)' }} />

      <div className="flex flex-col gap-2">
        <div className={label} style={{ color: 'var(--subtle)' }}>Fördelning per dag</div>
        {lines.map((line, index) => (
          <div key={index} className="flex items-center gap-1.5">
            <select
              aria-label={`Work item för rad ${index + 1}`}
              className={`${field} min-w-0 flex-1`}
              style={fieldStyle}
              value={line.workItemId}
              onChange={(event) => update(index, { workItemId: Number(event.target.value) })}
            >
              {workItems.map((item) => (
                <option key={item.id} value={item.id}>#{item.id} {item.name}</option>
              ))}
            </select>
            <input
              type="number" min={0} step={0.25}
              aria-label={`Timmar för rad ${index + 1}`}
              className={`${field} w-13 text-right`}
              style={fieldStyle}
              value={line.hours}
              onChange={(event) => update(index, { hours: Number(event.target.value) || 0 })}
            />
            <button
              type="button"
              aria-label={`Ta bort rad ${index + 1}`}
              disabled={lines.length === 1}
              className="flex size-7 items-center justify-center rounded-md disabled:opacity-40"
              style={{ color: 'var(--subtle)' }}
              onClick={() => setLines((current) => current.filter((_, i) => i !== index))}
            >
              <Close />
            </button>
          </div>
        ))}
        <div className="flex items-center justify-between gap-2">
          <button
            type="button"
            className="flex h-7 items-center gap-1.5 rounded-md border border-dashed px-2 text-xs"
            style={{ borderColor: 'var(--border)', color: 'var(--accent)' }}
            onClick={() => favourite && setLines((current) => [...current, { workItemId: favourite.id, hours: 0 }])}
          >
            <Plus /> Lägg till work item
          </button>
          <span
            className="flex items-center gap-1 text-xs"
            style={{ color: balanced ? 'var(--ok)' : 'var(--warn)' }}
          >
            {balanced && <Check />}
            {hours(linesSum)} av {hours(month.dailyHours)} h
          </span>
        </div>
      </div>

      <div className="h-px" style={{ background: 'var(--border)' }} />

      <div className="flex flex-col gap-2">
        <div className={label} style={{ color: 'var(--subtle)' }}>Fylls upp till målet</div>
        {blocked ? (
          <Row label={`${selected.length} valda dagar`} value="okänt" color="var(--subtle)" />
        ) : (
          <>
            {summary.emptyDays > 0 && (
              <Row
                label={`${summary.emptyDays} ${summary.emptyDays === 1 ? 'tom dag' : 'tomma dagar'}`}
                value={`${hours(summary.emptyDays * month.dailyHours)} h`}
              />
            )}
            {summary.partialDays > 0 && (
              <Row
                label={`${summary.partialDays} delvis ${summary.partialDays === 1 ? 'dag' : 'dagar'}`}
                value={`${hours(summary.totalHours - summary.emptyDays * month.dailyHours)} h`}
                color="var(--warn)"
              />
            )}
            {summary.skippedDays > 0 && (
              <Row
                label={`${summary.skippedDays} ${summary.skippedDays === 1 ? 'dag' : 'dagar'} redan klar`}
                value="hoppas över"
                color="var(--subtle)"
              />
            )}
          </>
        )}
        <div className="mt-0.5 flex items-baseline justify-between">
          <span className="text-[13px]">Att registrera</span>
          <span className="text-[22px] font-semibold" style={{ color: blocked ? 'var(--subtle)' : 'var(--fg)' }}>
            {blocked ? '— h' : `${hours(summary.totalHours)} h`}
          </span>
        </div>
      </div>

      {error && (
        <div className="rounded-md p-2 text-xs" style={{ background: 'var(--danger-bg)', color: 'var(--danger)' }}>
          {error}
        </div>
      )}

      {result && (
        <div className="flex flex-col gap-1 text-xs">
          <span style={{ color: 'var(--subtle)' }}>
            {result.failedEntries === 0
              ? `${result.postedEntries} poster registrerade.`
              : `${result.postedEntries} registrerade, ${result.failedEntries} misslyckades.`}
          </span>
          {result.days.filter((d) => d.status !== 'ok').map((day) => (
            <span key={day.date} style={{ color: 'var(--danger)' }}>{day.date}: {day.error}</span>
          ))}
        </div>
      )}

      <div className="mt-auto flex flex-col gap-2.5">
        <label className="flex items-center gap-2 text-[13px]">
          <input type="checkbox" checked={simulate} onChange={(event) => setSimulate(event.target.checked)} />
          Simulera (skicka inget)
        </label>
        <button
          type="button"
          disabled={!canRegister}
          onClick={() => void register()}
          className="h-9.5 rounded-md border text-sm font-semibold disabled:opacity-50"
          style={{ borderColor: 'var(--accent)', background: 'var(--accent)', color: 'var(--accent-fg)' }}
        >
          {busy ? 'Registrerar…' : blocked ? 'Registrera' : `Registrera ${hours(summary.totalHours)} h`}
        </button>
        <span className="text-center text-[11px]" style={{ color: 'var(--subtle)' }}>
          {blocked ? 'Blockerad tills tiden är hämtad' : 'Kalendern hämtas om från 7Pace efteråt'}
        </span>
        {selected.length > 0 && (
          <button type="button" className="text-[11px] underline" style={{ color: 'var(--subtle)' }} onClick={onClear}>
            Rensa markering
          </button>
        )}
      </div>
    </aside>
  )
}

function Row({ label, value, color = 'var(--fg)' }: { label: string; value: string; color?: string }) {
  return (
    <div className="flex items-baseline justify-between gap-2 text-[13px]">
      <span style={{ color: 'var(--subtle)' }}>{label}</span>
      <span className="font-medium" style={{ color }}>{value}</span>
    </div>
  )
}
```

- [ ] **Step 4: Mount the panel in the month view**

In `web/src/views/MonthView.tsx`, load the work items alongside the month:

```tsx
  const [workItems, setWorkItems] = useState<WorkItem[]>([])
  useEffect(() => { void api.workItems().then(setWorkItems).catch(() => setWorkItems([])) }, [])
```

Import `WorkItem` from `../types` and `SelectionPanel` from `./SelectionPanel`, then render it as the
second child of the `flex min-h-0 flex-1` row, after the calendar block:

```tsx
        {month && (
          <SelectionPanel
            month={month}
            workItems={workItems}
            selected={selection.selected}
            onRegistered={() => void load()}
            onClear={() => dispatch({ type: 'clear' })}
          />
        )}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `cd web && npm test`
Expected: PASS, the panel suite green and the month-view suite still green.

- [ ] **Step 6: Register for real, in simulate mode**

With the dev servers running and a real token configured, select a few days, tick `Simulera`, and
press Registrera. Confirm the reported hours match the badges, and that unticking `Simulera` and
registering one empty day writes exactly that day in 7Pace.

- [ ] **Step 7: Commit**

```bash
git add web
git commit -m "feat: selection panel with work item splits and registering"
```

---

### Task 14: Setup, work items and settings

**Files:**
- Create: `web/src/views/SetupWizard.tsx`, `web/src/views/SetupWizard.test.tsx`
- Create: `web/src/views/WorkItemsDialog.tsx`, `web/src/views/WorkItemsDialog.test.tsx`
- Create: `web/src/views/SettingsDialog.tsx`
- Create: `web/src/components/Dialog.tsx`
- Modify: `web/src/App.tsx`, `web/src/App.test.tsx` (new), `web/src/views/MonthView.tsx`

**Interfaces:**
- Consumes: `api.config`, `api.saveConfig`, `api.workItems`, `api.saveWorkItems`, `ApiError`, `useTheme`.
- Produces:
  - `<Dialog title onClose>` — a modal shell with a labelled close button
  - `<SetupWizard onDone />` — organization, token and a first work item; blocks until all three are valid
  - `<WorkItemsDialog items onSaved onClose />` — add, remove, set favourite, enforcing one favourite and at least one item
  - `<SettingsDialog config onSaved onClose />` — organization, replacement token, daily hours, theme
  - `<App />` — fetches config, gates on `configured`, applies the theme, owns the dialogs

- [ ] **Step 1: Write the failing tests**

`web/src/views/SetupWizard.test.tsx`:

```tsx
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { SetupWizard } from './SetupWizard'

vi.mock('../api', () => ({
  api: { saveConfig: vi.fn(), saveWorkItems: vi.fn() },
  ApiError: class ApiError extends Error {
    constructor(public status: number, message: string) { super(message) }
  },
}))
const { api } = await import('../api')

beforeEach(() => vi.clearAllMocks())

describe('SetupWizard', () => {
  it('cannot be completed until organization, token and a work item are given', async () => {
    render(<SetupWizard onDone={vi.fn()} />)
    const done = screen.getByRole('button', { name: /Kom igång/ })
    expect(done).toBeDisabled()

    await userEvent.type(screen.getByLabelText(/Organisation/), 'icore')
    expect(done).toBeDisabled()

    await userEvent.type(screen.getByLabelText(/API-token/), 'secret')
    expect(done).toBeDisabled()

    await userEvent.type(screen.getByLabelText(/Work item-ID/), '12345')
    await userEvent.type(screen.getByLabelText(/Namn/), 'Sprintarbete')
    expect(done).toBeEnabled()
  })

  it('saves the config and the first work item, marking it favourite', async () => {
    const onDone = vi.fn()
    vi.mocked(api.saveConfig).mockResolvedValue(undefined)
    vi.mocked(api.saveWorkItems).mockResolvedValue(undefined)
    render(<SetupWizard onDone={onDone} />)

    await userEvent.type(screen.getByLabelText(/Organisation/), 'icore')
    await userEvent.type(screen.getByLabelText(/API-token/), 'secret')
    await userEvent.type(screen.getByLabelText(/Work item-ID/), '12345')
    await userEvent.type(screen.getByLabelText(/Namn/), 'Sprintarbete')
    await userEvent.click(screen.getByRole('button', { name: /Kom igång/ }))

    await waitFor(() => expect(api.saveConfig).toHaveBeenCalledWith(
      expect.objectContaining({ organization: 'icore', token: 'secret' }),
    ))
    expect(api.saveWorkItems).toHaveBeenCalledWith([
      { id: 12345, name: 'Sprintarbete', isFavorite: true },
    ])
    expect(onDone).toHaveBeenCalled()
  })

  it('shows the server message when the organization is rejected', async () => {
    const { ApiError } = await import('../api')
    vi.mocked(api.saveConfig).mockRejectedValue(new ApiError(400, "'iCore v3' är inte ett giltigt kontonamn."))
    render(<SetupWizard onDone={vi.fn()} />)

    await userEvent.type(screen.getByLabelText(/Organisation/), 'iCore v3')
    await userEvent.type(screen.getByLabelText(/API-token/), 'secret')
    await userEvent.type(screen.getByLabelText(/Work item-ID/), '1')
    await userEvent.type(screen.getByLabelText(/Namn/), 'A')
    await userEvent.click(screen.getByRole('button', { name: /Kom igång/ }))

    expect(await screen.findByText(/inte ett giltigt kontonamn/)).toBeInTheDocument()
  })

  it('does not echo the token back into the DOM as plain text', async () => {
    render(<SetupWizard onDone={vi.fn()} />)

    const token = screen.getByLabelText(/API-token/)

    expect(token).toHaveAttribute('type', 'password')
  })
})
```

`web/src/views/WorkItemsDialog.test.tsx`:

```tsx
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { WorkItemsDialog } from './WorkItemsDialog'
import type { WorkItem } from '../types'

vi.mock('../api', () => ({
  api: { saveWorkItems: vi.fn() },
  ApiError: class ApiError extends Error {
    constructor(public status: number, message: string) { super(message) }
  },
}))
const { api } = await import('../api')

const items: WorkItem[] = [
  { id: 12345, name: 'Sprintarbete', isFavorite: true },
  { id: 12401, name: 'Support', isFavorite: false },
]

beforeEach(() => vi.clearAllMocks())

describe('WorkItemsDialog', () => {
  it('lists the configured work items', () => {
    render(<WorkItemsDialog items={items} onSaved={vi.fn()} onClose={vi.fn()} />)

    expect(screen.getByText(/Sprintarbete/)).toBeInTheDocument()
    expect(screen.getByText(/Support/)).toBeInTheDocument()
  })

  it('moves the favourite so exactly one stays favourite', async () => {
    vi.mocked(api.saveWorkItems).mockResolvedValue(undefined)
    render(<WorkItemsDialog items={items} onSaved={vi.fn()} onClose={vi.fn()} />)

    await userEvent.click(screen.getByRole('button', { name: /Gör 12401 till favorit/ }))
    await userEvent.click(screen.getByRole('button', { name: /Spara/ }))

    await waitFor(() => expect(api.saveWorkItems).toHaveBeenCalledWith([
      { id: 12345, name: 'Sprintarbete', isFavorite: false },
      { id: 12401, name: 'Support', isFavorite: true },
    ]))
  })

  it('refuses to remove the last work item', async () => {
    render(<WorkItemsDialog items={[items[0]]} onSaved={vi.fn()} onClose={vi.fn()} />)

    expect(screen.getByRole('button', { name: /Ta bort 12345/ })).toBeDisabled()
  })

  it('moves the favourite to the remaining item when the favourite is removed', async () => {
    vi.mocked(api.saveWorkItems).mockResolvedValue(undefined)
    render(<WorkItemsDialog items={items} onSaved={vi.fn()} onClose={vi.fn()} />)

    await userEvent.click(screen.getByRole('button', { name: /Ta bort 12345/ }))
    await userEvent.click(screen.getByRole('button', { name: /Spara/ }))

    await waitFor(() => expect(api.saveWorkItems).toHaveBeenCalledWith([
      { id: 12401, name: 'Support', isFavorite: true },
    ]))
  })

  it('adds a work item', async () => {
    vi.mocked(api.saveWorkItems).mockResolvedValue(undefined)
    render(<WorkItemsDialog items={items} onSaved={vi.fn()} onClose={vi.fn()} />)

    await userEvent.type(screen.getByLabelText(/Nytt work item-ID/), '99999')
    await userEvent.type(screen.getByLabelText(/Nytt namn/), 'Möten')
    await userEvent.click(screen.getByRole('button', { name: /Lägg till/ }))
    await userEvent.click(screen.getByRole('button', { name: /Spara/ }))

    await waitFor(() => expect(vi.mocked(api.saveWorkItems).mock.calls[0][0]).toHaveLength(3))
  })
})
```

`web/src/App.test.tsx`:

```tsx
import { render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { App } from './App'

vi.mock('./api', () => ({
  api: { config: vi.fn(), month: vi.fn().mockResolvedValue(null), workItems: vi.fn().mockResolvedValue([]) },
  ApiError: class ApiError extends Error {
    constructor(public status: number, message: string) { super(message) }
  },
}))
const { api } = await import('./api')

beforeEach(() => vi.clearAllMocks())

describe('App', () => {
  it('shows the setup wizard until the app is configured', async () => {
    vi.mocked(api.config).mockResolvedValue({
      configured: false, organization: '', dailyHours: 8, theme: 'System', hasToken: false,
    })

    render(<App />)

    expect(await screen.findByText(/Kom igång/)).toBeInTheDocument()
  })

  it('shows the calendar once configured', async () => {
    vi.mocked(api.config).mockResolvedValue({
      configured: true, organization: 'icore', dailyHours: 8, theme: 'System', hasToken: true,
    })

    render(<App />)

    expect(await screen.findByText('7Pace Desktop')).toBeInTheDocument()
  })

  it('says the app is not running when the API cannot be reached', async () => {
    const { ApiError } = await import('./api')
    vi.mocked(api.config).mockRejectedValue(new ApiError(0, 'Appen svarar inte.'))

    render(<App />)

    expect(await screen.findByText(/svarar inte/)).toBeInTheDocument()
  })
})
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd web && npm test`
Expected: FAIL — `SetupWizard`, `WorkItemsDialog` and the new `App` do not exist.

- [ ] **Step 3: Implement the dialog shell**

`web/src/components/Dialog.tsx`:

```tsx
import type { ReactNode } from 'react'
import { Close } from './Icons'

export function Dialog({ title, onClose, children }: { title: string; onClose: () => void; children: ReactNode }) {
  return (
    <div className="fixed inset-0 z-10 flex items-center justify-center bg-black/40 p-4">
      <div
        role="dialog" aria-modal="true" aria-label={title}
        className="flex max-h-full w-full max-w-lg flex-col gap-4 overflow-y-auto rounded-xl border p-5"
        style={{ borderColor: 'var(--border)', background: 'var(--surface)' }}
      >
        <div className="flex items-center justify-between">
          <h2 className="text-base font-semibold">{title}</h2>
          <button
            type="button" aria-label="Stäng" onClick={onClose}
            className="flex size-7 items-center justify-center rounded-md"
            style={{ color: 'var(--subtle)' }}
          >
            <Close />
          </button>
        </div>
        {children}
      </div>
    </div>
  )
}
```

- [ ] **Step 4: Implement the setup wizard**

`web/src/views/SetupWizard.tsx`:

```tsx
import { useState } from 'react'
import { api, ApiError } from '../api'

export function SetupWizard({ onDone }: { onDone: () => void }) {
  const [organization, setOrganization] = useState('')
  const [token, setToken] = useState('')
  const [workItemId, setWorkItemId] = useState('')
  const [name, setName] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const id = Number(workItemId)
  const complete = organization.trim() !== '' && token.trim() !== '' && id > 0 && name.trim() !== ''

  async function submit() {
    setBusy(true)
    setError(null)
    try {
      // Config first: the work item endpoint does not depend on it, but a bad organization
      // should stop the flow before anything is written.
      await api.saveConfig({ organization: organization.trim(), token: token.trim(), dailyHours: 8, theme: 'System' })
      await api.saveWorkItems([{ id, name: name.trim(), isFavorite: true }])
      onDone()
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'Okänt fel.')
    } finally {
      setBusy(false)
    }
  }

  const field = 'h-9 rounded-md border px-2.5 text-sm'
  const fieldStyle = { borderColor: 'var(--border)', background: 'var(--surface)', color: 'var(--fg)' }

  return (
    <div className="flex h-full items-center justify-center p-6">
      <div
        className="flex w-full max-w-md flex-col gap-4 rounded-xl border p-6"
        style={{ borderColor: 'var(--border)', background: 'var(--surface)' }}
      >
        <div className="flex flex-col gap-1">
          <h1 className="text-lg font-semibold">7Pace Desktop</h1>
          <p className="text-[13px] leading-relaxed" style={{ color: 'var(--subtle)' }}>
            Tre saker behövs innan du kan börja: ditt Azure DevOps-konto, en 7Pace API-token
            och minst ett work item att rapportera på.
          </p>
        </div>

        <Field label="Organisation (Azure DevOps-konto)" hint="Bara kontonamnet, t.ex. icore.">
          <input className={field} style={fieldStyle} value={organization}
                 onChange={(e) => setOrganization(e.target.value)} />
        </Field>

        <Field label="API-token" hint="7Pace: Settings > Reporting and API. Sparas i Windows autentiseringshanterare.">
          <input type="password" className={field} style={fieldStyle} value={token}
                 onChange={(e) => setToken(e.target.value)} />
        </Field>

        <div className="grid grid-cols-[7rem_1fr] gap-3">
          <Field label="Work item-ID">
            <input type="number" className={field} style={fieldStyle} value={workItemId}
                   onChange={(e) => setWorkItemId(e.target.value)} />
          </Field>
          <Field label="Namn">
            <input className={field} style={fieldStyle} value={name} onChange={(e) => setName(e.target.value)} />
          </Field>
        </div>

        {error && (
          <div className="rounded-md p-2 text-xs" style={{ background: 'var(--danger-bg)', color: 'var(--danger)' }}>
            {error}
          </div>
        )}

        <button
          type="button" disabled={!complete || busy} onClick={() => void submit()}
          className="h-9.5 rounded-md border text-sm font-semibold disabled:opacity-50"
          style={{ borderColor: 'var(--accent)', background: 'var(--accent)', color: 'var(--accent-fg)' }}
        >
          {busy ? 'Sparar…' : 'Kom igång'}
        </button>
      </div>
    </div>
  )
}

function Field({ label, hint, children }: { label: string; hint?: string; children: React.ReactNode }) {
  return (
    <label className="flex flex-col gap-1">
      <span className="text-[13px] font-medium">{label}</span>
      {children}
      {hint && <span className="text-[11px] leading-snug" style={{ color: 'var(--subtle)' }}>{hint}</span>}
    </label>
  )
}
```

- [ ] **Step 5: Implement the work items dialog**

`web/src/views/WorkItemsDialog.tsx`:

```tsx
import { useState } from 'react'
import { api, ApiError } from '../api'
import type { WorkItem } from '../types'
import { Dialog } from '../components/Dialog'
import { Check, Close, Plus } from '../components/Icons'

interface Props {
  items: WorkItem[]
  onSaved: (items: WorkItem[]) => void
  onClose: () => void
}

export function WorkItemsDialog({ items, onSaved, onClose }: Props) {
  const [draft, setDraft] = useState<WorkItem[]>(items)
  const [newId, setNewId] = useState('')
  const [newName, setNewName] = useState('')
  const [error, setError] = useState<string | null>(null)

  const setFavourite = (id: number) =>
    setDraft((current) => current.map((item) => ({ ...item, isFavorite: item.id === id })))

  /** Removing the favourite hands the role to the first survivor, so exactly one always holds it. */
  const remove = (id: number) =>
    setDraft((current) => {
      const rest = current.filter((item) => item.id !== id)
      return rest.some((item) => item.isFavorite)
        ? rest
        : rest.map((item, index) => ({ ...item, isFavorite: index === 0 }))
    })

  const add = () => {
    const id = Number(newId)
    if (id <= 0 || newName.trim() === '') return
    if (draft.some((item) => item.id === id)) {
      setError('Det work itemet finns redan.')
      return
    }
    setDraft((current) => [...current, { id, name: newName.trim(), isFavorite: current.length === 0 }])
    setNewId('')
    setNewName('')
    setError(null)
  }

  async function save() {
    try {
      await api.saveWorkItems(draft)
      onSaved(draft)
      onClose()
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'Okänt fel.')
    }
  }

  const field = 'h-8 rounded-md border px-2 text-[13px]'
  const fieldStyle = { borderColor: 'var(--border)', background: 'var(--surface)', color: 'var(--fg)' }

  return (
    <Dialog title="Work items" onClose={onClose}>
      <div className="flex flex-col gap-1.5">
        {draft.map((item) => (
          <div key={item.id} className="flex items-center gap-2 rounded-md border p-2" style={fieldStyle}>
            <span className="min-w-0 flex-1 truncate text-[13px]">#{item.id} {item.name}</span>
            <button
              type="button"
              aria-label={`Gör ${item.id} till favorit`}
              onClick={() => setFavourite(item.id)}
              className="flex size-7 items-center justify-center rounded-md"
              style={{ color: item.isFavorite ? 'var(--accent)' : 'var(--subtle)' }}
            >
              <Check />
            </button>
            <button
              type="button"
              aria-label={`Ta bort ${item.id}`}
              disabled={draft.length === 1}
              onClick={() => remove(item.id)}
              className="flex size-7 items-center justify-center rounded-md disabled:opacity-40"
              style={{ color: 'var(--subtle)' }}
            >
              <Close />
            </button>
          </div>
        ))}
      </div>

      <div className="flex items-end gap-2">
        <label className="flex flex-col gap-1">
          <span className="text-[11px]" style={{ color: 'var(--subtle)' }}>Nytt work item-ID</span>
          <input type="number" className={`${field} w-28`} style={fieldStyle} value={newId}
                 onChange={(e) => setNewId(e.target.value)} />
        </label>
        <label className="flex min-w-0 flex-1 flex-col gap-1">
          <span className="text-[11px]" style={{ color: 'var(--subtle)' }}>Nytt namn</span>
          <input className={field} style={fieldStyle} value={newName} onChange={(e) => setNewName(e.target.value)} />
        </label>
        <button
          type="button" onClick={add}
          className="flex h-8 items-center gap-1.5 rounded-md border px-2 text-xs"
          style={{ borderColor: 'var(--border)', color: 'var(--accent)' }}
        >
          <Plus /> Lägg till
        </button>
      </div>

      {error && <div className="text-xs" style={{ color: 'var(--danger)' }}>{error}</div>}

      <button
        type="button" onClick={() => void save()}
        className="h-9 rounded-md border text-sm font-semibold"
        style={{ borderColor: 'var(--accent)', background: 'var(--accent)', color: 'var(--accent-fg)' }}
      >
        Spara
      </button>
    </Dialog>
  )
}
```

- [ ] **Step 6: Implement the settings dialog**

`web/src/views/SettingsDialog.tsx`:

```tsx
import { useState } from 'react'
import { api, ApiError } from '../api'
import type { Config, Theme } from '../types'
import { Dialog } from '../components/Dialog'

const THEMES: Theme[] = ['System', 'Light', 'Dark']
const THEME_LABELS: Record<Theme, string> = { System: 'Följ system', Light: 'Ljust', Dark: 'Mörkt' }

interface Props {
  config: Config
  onSaved: (config: Config) => void
  onClose: () => void
}

export function SettingsDialog({ config, onSaved, onClose }: Props) {
  const [organization, setOrganization] = useState(config.organization)
  const [token, setToken] = useState('')
  const [dailyHours, setDailyHours] = useState(config.dailyHours)
  const [theme, setTheme] = useState<Theme>(config.theme)
  const [error, setError] = useState<string | null>(null)

  async function save() {
    try {
      // An empty token field means "keep the stored one" - the UI can never read it back.
      await api.saveConfig({
        organization,
        token: token.trim() === '' ? null : token.trim(),
        dailyHours,
        theme,
      })
      onSaved({ ...config, organization, dailyHours, theme, hasToken: config.hasToken || token !== '' })
      onClose()
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'Okänt fel.')
    }
  }

  const field = 'h-9 rounded-md border px-2.5 text-sm'
  const fieldStyle = { borderColor: 'var(--border)', background: 'var(--surface)', color: 'var(--fg)' }

  return (
    <Dialog title="Inställningar" onClose={onClose}>
      <label className="flex flex-col gap-1">
        <span className="text-[13px] font-medium">Organisation</span>
        <input className={field} style={fieldStyle} value={organization} onChange={(e) => setOrganization(e.target.value)} />
      </label>

      <label className="flex flex-col gap-1">
        <span className="text-[13px] font-medium">Ny API-token</span>
        <input type="password" className={field} style={fieldStyle} value={token}
               onChange={(e) => setToken(e.target.value)} />
        <span className="text-[11px]" style={{ color: 'var(--subtle)' }}>
          Lämna tomt för att behålla den sparade tokenen.
        </span>
      </label>

      <label className="flex flex-col gap-1">
        <span className="text-[13px] font-medium">Timmar per dag</span>
        <input type="number" min={1} max={24} step={0.5} className={`${field} w-28`} style={fieldStyle}
               value={dailyHours} onChange={(e) => setDailyHours(Number(e.target.value) || 0)} />
      </label>

      <fieldset className="flex flex-col gap-1">
        <legend className="text-[13px] font-medium">Tema</legend>
        <div className="flex gap-2">
          {THEMES.map((option) => (
            <label key={option} className="flex items-center gap-1.5 text-[13px]">
              <input type="radio" name="theme" checked={theme === option} onChange={() => setTheme(option)} />
              {THEME_LABELS[option]}
            </label>
          ))}
        </div>
      </fieldset>

      {error && <div className="text-xs" style={{ color: 'var(--danger)' }}>{error}</div>}

      <button
        type="button" onClick={() => void save()}
        className="h-9 rounded-md border text-sm font-semibold"
        style={{ borderColor: 'var(--accent)', background: 'var(--accent)', color: 'var(--accent-fg)' }}
      >
        Spara
      </button>
    </Dialog>
  )
}
```

- [ ] **Step 7: Gate the app on configuration and own the dialogs**

`web/src/App.tsx`:

```tsx
import { useCallback, useEffect, useState } from 'react'
import { api, ApiError } from './api'
import type { Config } from './types'
import { useTheme } from './useTheme'
import { MonthView } from './views/MonthView'
import { SetupWizard } from './views/SetupWizard'

export function App() {
  const [config, setConfig] = useState<Config | null>(null)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(async () => {
    try {
      setConfig(await api.config())
      setError(null)
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'Okänt fel.')
    }
  }, [])

  useEffect(() => { void load() }, [load])
  useTheme(config?.theme ?? 'System')

  if (error) {
    return (
      <div className="flex h-full items-center justify-center p-6 text-center text-sm" style={{ color: 'var(--danger)' }}>
        {error}
      </div>
    )
  }

  if (!config) return <div className="p-6 text-sm" style={{ color: 'var(--subtle)' }}>Laddar…</div>
  if (!config.configured) return <SetupWizard onDone={() => void load()} />

  return <MonthView config={config} onConfigChanged={setConfig} />
}
```

In `web/src/views/MonthView.tsx`, accept the two new props and use them to open the dialogs. Add to
the imports:

```tsx
import type { Config, WorkItem } from '../types'
import { SettingsDialog } from './SettingsDialog'
import { WorkItemsDialog } from './WorkItemsDialog'
```

Change the signature to `export function MonthView({ config, onConfigChanged }: { config: Config; onConfigChanged: (config: Config) => void })`,
add dialog state:

```tsx
  const [dialog, setDialog] = useState<'settings' | 'workitems' | null>(null)
```

Give the gear button `onClick={() => setDialog('settings')}`, add a third header button labelled
`Work items` with `onClick={() => setDialog('workitems')}`, and make the theme button cycle the
three values, persisting each so the choice survives a reload:

```tsx
  const cycleTheme = async () => {
    const order: Theme[] = ['System', 'Light', 'Dark']
    const next = order[(order.indexOf(config.theme) + 1) % order.length]
    onConfigChanged({ ...config, theme: next })
    try {
      await api.saveConfig({
        organization: config.organization,
        token: null,
        dailyHours: config.dailyHours,
        theme: next,
      })
    } catch {
      // A failed persist leaves the theme applied for this session only; not worth a banner.
    }
  }
```

with the button becoming:

```tsx
          <button
            type="button"
            aria-label={`Tema: ${config.theme}`}
            className={button}
            style={buttonStyle}
            onClick={() => void cycleTheme()}
          >
            <Moon />
          </button>
```

Import `Theme` alongside `Config` and `WorkItem` from `../types`.

Because `MonthView` now takes props, update every `render(<MonthView />)` in
`web/src/views/MonthView.test.tsx` to pass them:

```tsx
const config: Config = {
  configured: true, organization: 'icore', dailyHours: 8, theme: 'System', hasToken: true,
}

const renderMonthView = () => render(<MonthView config={config} onConfigChanged={vi.fn()} />)
```

and replace the `render(<MonthView />)` calls with `renderMonthView()`. Add
`import type { Config } from '../types'` to that file.

Finally render at the end of the component:

```tsx
      {dialog === 'settings' && (
        <SettingsDialog
          config={config}
          onSaved={(next) => { onConfigChanged(next); void load() }}
          onClose={() => setDialog(null)}
        />
      )}
      {dialog === 'workitems' && (
        <WorkItemsDialog items={workItems} onSaved={setWorkItems} onClose={() => setDialog(null)} />
      )}
```

- [ ] **Step 8: Run the tests to verify they pass**

Run: `cd web && npm test`
Expected: PASS, all front-end suites green.

- [ ] **Step 9: Walk the first-run flow**

Rename `%AppData%\7PaceDesktop` aside, restart the server, and confirm the wizard appears, refuses
to proceed until all four fields are filled, rejects `iCore v3` with the server's message, and lands
on the calendar afterwards. Restore the real directory when done.

- [ ] **Step 10: Commit**

```bash
git add web
git commit -m "feat: setup wizard, work item and settings dialogs"
```

---

### Task 15: Single-file distribution and launcher

**Files:**
- Modify: `src/7PaceDesktop.Server/Program.cs`
- Modify: `src/7PaceDesktop.Server/7PaceDesktop.Server.csproj`
- Modify: `tests/7PaceDesktop.Tests/ServerFixture.cs`
- Create: `tests/7PaceDesktop.Tests/LauncherTests.cs`
- Create: `README.md` (or extend it if one exists)

**Interfaces:**
- Consumes: `Program`, `ServerFixture`.
- Produces: the server binds `127.0.0.1` on a free port and opens the default browser at that
  address, unless the `OpenBrowser` setting is `false`.

- [ ] **Step 1: Write the failing test**

`tests/7PaceDesktop.Tests/LauncherTests.cs`:

```csharp
namespace PaceDesktop.Tests;

public class LauncherTests
{
    [Fact]
    public void Fixture_DisablesTheBrowserLaunch()
    {
        // If this setting is ever dropped, running the test suite opens a browser tab per
        // fixture. The assertion exists to keep that from regressing silently.
        using var server = new ServerFixture();

        Assert.Equal("false", server.Configuration["OpenBrowser"]);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/7PaceDesktop.Tests/7PaceDesktop.Tests.csproj --filter "FullyQualifiedName~LauncherTests"`
Expected: compile error — `ServerFixture.Configuration` does not exist.

- [ ] **Step 3: Expose configuration from the fixture and disable the browser**

In `tests/7PaceDesktop.Tests/ServerFixture.cs`, add to the `WithWebHostBuilder` callback, beside the
existing `UseSetting` call:

```csharp
            builder.UseSetting("OpenBrowser", "false");
```

and expose the configuration by adding a property, assigned after `Client` is created:

```csharp
    public IConfiguration Configuration { get; private set; } = null!;
```

with, at the end of the constructor:

```csharp
        Configuration = _factory.Services.GetRequiredService<IConfiguration>();
```

Add `using Microsoft.Extensions.Configuration;` to the file.

- [ ] **Step 4: Bind localhost and open the browser**

Task 6 already binds loopback on a free port, via
`builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));`. **Do not add
`builder.WebHost.UseUrls("http://127.0.0.1:0")` as well** — two competing address configurations
make Kestrel log an "Overriding address(es)" warning, and every task must end with zero warnings.
The existing `Listen` call is what satisfies the loopback-only requirement; `app.Urls` is still
populated from it, so the browser launch below works unchanged.

In `src/7PaceDesktop.Server/Program.cs`, add `using System.Diagnostics;` and, immediately before
`app.Run();`:

```csharp
// Tests host the app in-process and must not spawn browsers.
if (builder.Configuration["OpenBrowser"] != "false")
{
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        var url = app.Urls.FirstOrDefault();
        if (url is null) return;
        Console.WriteLine($"7Pace Desktop körs på {url}");
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    });
}
```

- [ ] **Step 5: Configure single-file publish**

Add to the `PropertyGroup` in `src/7PaceDesktop.Server/7PaceDesktop.Server.csproj`:

```xml
    <AssemblyName>7PaceDesktop</AssemblyName>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <SelfContained>true</SelfContained>
    <PublishSingleFile>true</PublishSingleFile>
    <InvariantGlobalization>false</InvariantGlobalization>
```

`InvariantGlobalization` stays off because `formatMonth`'s counterpart, `ISOWeek`, and Swedish
date formatting depend on real culture data.

- [ ] **Step 6: Publish and run it**

Run:

```bash
dotnet publish src/7PaceDesktop.Server -c Release -o publish
./publish/7PaceDesktop.exe
```

Expected: the SPA is built by the `BuildSpa` target, the console prints a `127.0.0.1` URL, the
default browser opens on the calendar, and the app works with no dev server running.

- [ ] **Step 7: Document how to run it**

Write `README.md`:

```markdown
# 7Pace Desktop

Bulk-register work hours into 7Pace Timetracker, with the month's already-registered time
visible so days are topped up rather than duplicated.

## Running it

Download or build `7PaceDesktop.exe`, then run it. It serves a local web app on
`127.0.0.1` and opens your browser. Nothing is hosted and nothing leaves your machine
except the calls to 7Pace itself.

On first run it asks for three things:

1. **Organisation** — your Azure DevOps account name, for example `icore`. Not the project.
2. **API-token** — from 7Pace: *Settings > Reporting and API*. Stored in Windows Credential
   Manager, never in a file and never sent to the browser.
3. **A work item** — at least one, to report time against.

## Building from source

```bash
dotnet publish src/7PaceDesktop.Server -c Release -o publish
```

The Release build runs `npm ci && npm run build` in `web/` and embeds the result, so the
executable is the whole app. Node 20 or later is required to build; not to run.

## Development

Two shells:

```bash
ASPNETCORE_URLS=http://127.0.0.1:5111 dotnet run --project src/7PaceDesktop.Server
cd web && npm run dev
```

Then open `http://127.0.0.1:5173`. Vite proxies `/api` to the dotnet server.

## Tests

```bash
dotnet test tests/7PaceDesktop.Tests/7PaceDesktop.Tests.csproj
cd web && npm test
```
```

- [ ] **Step 8: Run the whole suite**

Run: `dotnet test tests/7PaceDesktop.Tests/7PaceDesktop.Tests.csproj && cd web && npm test`
Expected: PASS on both, and no browser window opens during the C# run.

- [ ] **Step 9: Commit**

```bash
git add src/7PaceDesktop.Server tests/7PaceDesktop.Tests README.md
git commit -m "feat: single-file executable that serves the app and opens the browser"
```

---

### Task 16: Remove the WPF app

**Files:**
- Delete: `src/7PaceDesktop.App/` (whole project)
- Delete: `src/7PaceDesktop.Core/TimeEntryGenerator.cs`
- Delete: `tests/7PaceDesktop.Tests/TimeEntryGeneratorTests.cs`, `MainViewModelTests.cs`, `SetupViewModelTests.cs`, `WorkItemsViewModelTests.cs`
- Modify: `7PaceDesktop.slnx`, `tests/7PaceDesktop.Tests/7PaceDesktop.Tests.csproj`

**Interfaces:**
- Consumes: nothing. Every behaviour these files carried now lives in `Core.Planning`, the server
  endpoints, or the web views.
- Produces: a solution with three projects — `Core`, `Server`, `Tests` — and no WPF dependency.

Do this task only once Tasks 10 to 15 are complete and the web app has been used successfully
against the real 7Pace instance. Until then the WPF app is the working fallback.

- [ ] **Step 1: Confirm nothing outside the App project references it**

Run: `grep -rn "PaceDesktop.App" --include=*.cs --include=*.csproj --include=*.slnx . | grep -v "src/7PaceDesktop.App/"`
Expected: only `7PaceDesktop.slnx` and `tests/7PaceDesktop.Tests/7PaceDesktop.Tests.csproj`.

Run: `grep -rn "TimeEntryGenerator" --include=*.cs . | grep -v "src/7PaceDesktop.App/"`
Expected: only `tests/7PaceDesktop.Tests/TimeEntryGeneratorTests.cs`.

If either command reports anything else, stop and port that usage before deleting.

- [ ] **Step 2: Drop the project reference and the view-model tests**

In `tests/7PaceDesktop.Tests/7PaceDesktop.Tests.csproj`, delete the line:

```xml
    <ProjectReference Include="..\..\src\7PaceDesktop.App\7PaceDesktop.App.csproj" />
```

Run:

```bash
git rm tests/7PaceDesktop.Tests/MainViewModelTests.cs \
       tests/7PaceDesktop.Tests/SetupViewModelTests.cs \
       tests/7PaceDesktop.Tests/WorkItemsViewModelTests.cs \
       tests/7PaceDesktop.Tests/TimeEntryGeneratorTests.cs
```

Their coverage moved: generation rules to `WorkScheduleTests` and `FillPlannerTests`, submit and
retry behaviour to `RegisterEndpointTests`, setup and work item validation to
`ConfigEndpointTests`, and the corresponding UI behaviour to the front-end suites.

- [ ] **Step 3: Remove the project**

Run:

```bash
dotnet sln 7PaceDesktop.slnx remove src/7PaceDesktop.App/7PaceDesktop.App.csproj
git rm -r src/7PaceDesktop.App
git rm src/7PaceDesktop.Core/TimeEntryGenerator.cs
```

- [ ] **Step 4: Verify the solution still builds and the suite is green**

Run: `dotnet build 7PaceDesktop.slnx && dotnet test tests/7PaceDesktop.Tests/7PaceDesktop.Tests.csproj`
Expected: build succeeds with three projects; every remaining test passes.

- [ ] **Step 5: Confirm nothing was silently lost**

Run: `dotnet test tests/7PaceDesktop.Tests/7PaceDesktop.Tests.csproj --list-tests`

Read the list and confirm it covers: the 7Pace client (read and write), `WorkSchedule`,
`MonthPlan`, `FillPlanner`, storage and migration, credentials, holidays, and the four server
endpoint groups. If a behaviour from a deleted test file has no equivalent, add the test before
committing.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "refactor: remove the WPF app now that the web app replaces it"
```

---

### Task 17: Verify the 7Pace read contract live

**Files:**
- Modify (only if the live response differs): `src/7PaceDesktop.Core/Services/PaceApiClient.cs`, `tests/7PaceDesktop.Tests/PaceApiClientTests.cs`
- Modify: `.superpowers/lessons-learned.md`, `.superpowers/project-history.md`
- Modify: `docs/superpowers/specs/2026-08-28-calendar-first-redesign-design.md` (mark the contract verified)

**Interfaces:**
- Consumes: `PaceApiClient.ParseWorkLogs`, `GetWorkLogsAsync`.
- Produces: a verified read contract, or a corrected parser plus a test pinning the real shape.

This is a release gate. The `POST` contract was wrong on both the host name and the field casing
the first time it was written from documentation, and that was only found by calling the real
instance. Do not skip it.

- [ ] **Step 1: Fetch a real response**

Ask the user to run this in the session, replacing the token, and to paste the output. It reads a
month they know has logged time:

```powershell
$token = Read-Host -AsSecureString "7Pace API token" | ConvertFrom-SecureString -AsPlainText
curl.exe -s -H "Authorization: Bearer $token" "https://icore.timehub.7pace.com/api/rest/workLogs?api-version=3.2&`$fromTimestamp=2026-06-01T00:00:00&`$toTimestamp=2026-07-01T00:00:00&`$count=3" | ConvertFrom-Json | ConvertTo-Json -Depth 8
```

- [ ] **Step 2: Compare the response against the four assumptions**

Check each and write down the answer:

1. **Envelope** — is the array at `data.workLogs`, at `data`, or at the root? The parser accepts
   all three, so any of them passes. Anything else needs a new branch in `FindArray`.
2. **Field names** — are they `id`, `timeStamp`, `length`, `workItemId`, `comment`? The lookup is
   case-insensitive, so casing does not matter. A different name does.
3. **`length` unit** — is it seconds? A 6-hour worklog should read `21600`. If it is minutes, the
   `/ 3600.0` divisor is wrong and every displayed hour is 60 times too small.
4. **Timestamp timezone** — does `timeStamp` carry an offset or a trailing `Z`? The parser takes
   the first ten characters, so a UTC timestamp would misfile an early-morning or late-evening
   worklog by a day.

- [ ] **Step 3: If anything differs, fix it test-first**

Add the real response body, with any identifying content trimmed, as a new `[Fact]` in
`PaceApiClientTests` pinning the actual shape:

```csharp
    [Fact]
    public void ParseWorkLogs_ReadsTheLiveResponseShape()
    {
        // Captured from icore.timehub.7pace.com, api-version=3.2, on the date of Task 17.
        const string json = """
            <paste the trimmed real response here>
            """;
        using var doc = JsonDocument.Parse(json);

        var logs = PaceApiClient.ParseWorkLogs(doc.RootElement);

        Assert.NotEmpty(logs);
        Assert.All(logs, l => Assert.True(l.Hours > 0));
        Assert.All(logs, l => Assert.True(l.WorkItemId > 0));
        Assert.All(logs, l => Assert.NotEqual(default, l.Date));
    }
```

Run it, watch it fail, then correct `ParseWorkLogs` until it passes. If the timestamp carries an
offset, convert to local time before taking the date rather than slicing the string, and add a
test for a worklog logged at 23:30 local.

- [ ] **Step 4: Check a month end to end**

Run the published executable. Open a month you know well and confirm, against the 7Pace web UI:

- the logged hours per day match,
- the month total matches,
- days you know are full show as `Klar`, and partially logged days show the right shortfall.

Then select two days you know are empty, tick `Simulera`, and confirm the proposed hours are what
you expect. Untick it, register one day, and confirm in 7Pace that exactly one worklog appeared
with the right hours and work item.

- [ ] **Step 5: Mark the contract verified**

In the spec's **API contract with 7Pace** section, replace the `**Unverified.**` paragraph with
what was actually observed, and remove the matching entries from the **Assumptions** section.

- [ ] **Step 6: Record what was learned**

Append one line per finding to `.superpowers/lessons-learned.md` — in particular the real
envelope, field names, `length` unit and timestamp timezone, since those are exactly the facts
that were guessed. Add a narrative line to `.superpowers/project-history.md`.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "fix: verify the 7Pace read contract against the live instance"
```

---

## Deviations from the spec, decided while planning

These are deliberate and the spec has been updated to match:

1. **Staleness is enforced server-side.** The spec had the client refetch when its data was older
   than five minutes. Task 9 instead has `POST /api/register` refetch unconditionally before
   planning and return `409 Conflict` if that read fails. A client can therefore never cause a
   top-up from stale or assumed-zero state, and the front end carries no staleness logic.
2. **`FillSpec.Target` is derived, not passed.** The spec described a target the lines must sum
   to. It is the sum of the lines, and the balance check against the daily target lives in the UI,
   which removes a field that could disagree with itself.
3. **`ITokenSource` sits between the server and Credential Manager.** Not in the spec. Without it
   every endpoint test would read and write the developer's real credential store.

## Verification checklist before calling this done

- `dotnet test tests/7PaceDesktop.Tests/7PaceDesktop.Tests.csproj` — green.
- `cd web && npm test` — green.
- `dotnet publish src/7PaceDesktop.Server -c Release -o publish` — succeeds, and the resulting
  executable runs with no dev server.
- Task 17's live check done, and the spec's unverified paragraph replaced with observed facts.
- A month with known logged time reads correctly against the 7Pace web UI.
- Registering into a partially logged day adds the shortfall and no more.
- With the network to 7Pace blocked, the calendar shows `okänt` and `Registrera` is disabled.
- The app is readable in light and dark, and at 1280 px wide nothing scrolls sideways.
- `grep -rn "token" src/7PaceDesktop.Server --include=*.cs` shows no path that writes a token
  into a response body.
