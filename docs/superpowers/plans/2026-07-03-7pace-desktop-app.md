# 7PaceDesktop Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A .NET 10 WPF desktop app that bulk-registers work hours into 7Pace Timetracker over a date range, skipping Swedish public holidays and shortening the day before a holiday by 3 hours.

**Architecture:** Three projects — `7PaceDesktop.Core` (models, business logic, services; no UI dependencies), `7PaceDesktop.App` (WPF, MVVM via CommunityToolkit.Mvvm), `7PaceDesktop.Tests` (xUnit against Core). All I/O services take injectable dependencies (`HttpMessageHandler`, base directories) so logic is testable without network/disk side effects.

**Tech Stack:** .NET 10, WPF, CommunityToolkit.Mvvm, xUnit, Meziantou.Framework.Win32.CredentialManager, System.Text.Json.

**Spec:** `docs/superpowers/specs/2026-07-03-7pace-desktop-app-design.md`

## Global Constraints

- Target framework: `net10.0-windows` (WPF projects), `net10.0-windows` for Core and Tests (Core has no WPF dependency but shares the TFM for simplicity).
- **No pre-seeded work items** — the app ships empty; first-run wizard requires the user to add at least one work item.
- 7Pace API token is stored **only** in Windows Credential Manager, never in a JSON/config file.
- Per-user state lives in `%AppData%\7PaceDesktop\` (`settings.json`, `workitems.json`).
- Holiday source: `https://date.nager.at/api/v3/publicholidays/{year}/SE`, cached per year inside `settings.json`.
- Business rule: skip Sat/Sun and holidays; if the next calendar day is a holiday, subtract 3 hours (floor 0, flag the row when floored).
- The generated entries carry **no comment text**.
- The 7Pace `workLogs` payload shape is UNVERIFIED — Task 6 starts by verifying it against the live instance's `api/rest/help` page before coding the client. Do not skip this.
- All git commits in this repo; commit after every green test cycle.

---

### Task 1: Solution scaffolding

**Files:**
- Create: `7PaceDesktop.sln`, `src/7PaceDesktop.Core/7PaceDesktop.Core.csproj`, `src/7PaceDesktop.App/7PaceDesktop.App.csproj`, `tests/7PaceDesktop.Tests/7PaceDesktop.Tests.csproj`, `.gitignore`

**Interfaces:**
- Produces: buildable empty solution all later tasks add code into.

- [ ] **Step 1: Scaffold projects**

```powershell
cd C:\Users\solchr\source\repos\7PaceDesktop
dotnet new gitignore
dotnet new sln -n 7PaceDesktop
dotnet new classlib -n 7PaceDesktop.Core -o src/7PaceDesktop.Core -f net10.0
dotnet new wpf -n 7PaceDesktop.App -o src/7PaceDesktop.App -f net10.0
dotnet new xunit -n 7PaceDesktop.Tests -o tests/7PaceDesktop.Tests -f net10.0
dotnet sln add src/7PaceDesktop.Core src/7PaceDesktop.App tests/7PaceDesktop.Tests
dotnet add src/7PaceDesktop.App reference src/7PaceDesktop.Core
dotnet add tests/7PaceDesktop.Tests reference src/7PaceDesktop.Core
dotnet add src/7PaceDesktop.App package CommunityToolkit.Mvvm
dotnet add src/7PaceDesktop.Core package Meziantou.Framework.Win32.CredentialManager
```

Then edit `src/7PaceDesktop.Core/7PaceDesktop.Core.csproj` and `tests/7PaceDesktop.Tests/7PaceDesktop.Tests.csproj` to set `<TargetFramework>net10.0-windows</TargetFramework>` (CredentialManager needs Windows).

Delete the scaffolded `Class1.cs` and `UnitTest1.cs`.

- [ ] **Step 2: Verify build**

Run: `dotnet build`
Expected: `Build succeeded` with 0 errors.

- [ ] **Step 3: Commit**

```bash
git add -A && git commit -m "chore: scaffold solution (Core, App, Tests)"
```

---

### Task 2: Models + TimeEntryGenerator (core business rule)

**Files:**
- Create: `src/7PaceDesktop.Core/Models/WorkItem.cs`, `src/7PaceDesktop.Core/Models/Holiday.cs`, `src/7PaceDesktop.Core/Models/TimeEntry.cs`, `src/7PaceDesktop.Core/TimeEntryGenerator.cs`
- Test: `tests/7PaceDesktop.Tests/TimeEntryGeneratorTests.cs`

**Interfaces:**
- Produces:
  - `record WorkItem(int Id, string Name, bool IsFavorite)`
  - `record Holiday(DateOnly Date, string Name)`
  - `record TimeEntry(DateOnly Date, double Hours, int WorkItemId, bool HitZeroFloor)`
  - `static IReadOnlyList<TimeEntry> TimeEntryGenerator.Generate(DateOnly start, DateOnly end, double hoursPerDay, IReadOnlySet<DateOnly> holidays, int workItemId)`

- [ ] **Step 1: Write the models**

```csharp
// src/7PaceDesktop.Core/Models/WorkItem.cs
namespace PaceDesktop.Core.Models;
public sealed record WorkItem(int Id, string Name, bool IsFavorite);

// src/7PaceDesktop.Core/Models/Holiday.cs
namespace PaceDesktop.Core.Models;
public sealed record Holiday(DateOnly Date, string Name);

// src/7PaceDesktop.Core/Models/TimeEntry.cs
namespace PaceDesktop.Core.Models;
public sealed record TimeEntry(DateOnly Date, double Hours, int WorkItemId, bool HitZeroFloor = false);
```

(Namespace root is `PaceDesktop` because C# namespaces cannot start with a digit — set `<RootNamespace>PaceDesktop.Core</RootNamespace>` in the Core csproj and `<RootNamespace>PaceDesktop.App</RootNamespace>` in the App csproj.)

- [ ] **Step 2: Write the failing tests**

```csharp
// tests/7PaceDesktop.Tests/TimeEntryGeneratorTests.cs
using PaceDesktop.Core;
using PaceDesktop.Core.Models;

namespace PaceDesktop.Tests;

public class TimeEntryGeneratorTests
{
    private static readonly IReadOnlySet<DateOnly> NoHolidays = new HashSet<DateOnly>();

    [Fact]
    public void PlainWeek_GeneratesFiveEntries_SkippingWeekend()
    {
        // Mon 2026-07-06 .. Sun 2026-07-12
        var result = TimeEntryGenerator.Generate(
            new DateOnly(2026, 7, 6), new DateOnly(2026, 7, 12), 8, NoHolidays, 42);

        Assert.Equal(5, result.Count);
        Assert.All(result, e => Assert.Equal(8, e.Hours));
        Assert.All(result, e => Assert.Equal(42, e.WorkItemId));
        Assert.DoesNotContain(result, e => e.Date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday);
    }

    [Fact]
    public void HolidayOnWeekday_IsSkipped_AndDayBeforeShortenedBy3()
    {
        // Wed 2026-06-24 is a holiday; Tue 2026-06-23 should be 5h.
        var holidays = new HashSet<DateOnly> { new(2026, 6, 24) };
        var result = TimeEntryGenerator.Generate(
            new DateOnly(2026, 6, 22), new DateOnly(2026, 6, 26), 8, holidays, 1);

        Assert.DoesNotContain(result, e => e.Date == new DateOnly(2026, 6, 24));
        var tuesday = Assert.Single(result, e => e.Date == new DateOnly(2026, 6, 23));
        Assert.Equal(5, tuesday.Hours);
        Assert.False(tuesday.HitZeroFloor);
    }

    [Fact]
    public void HolidayOnMonday_DoesNotShortenFriday()
    {
        // Mon 2026-07-13 holiday. Sunday is not logged; Friday 2026-07-10 stays 8h.
        var holidays = new HashSet<DateOnly> { new(2026, 7, 13) };
        var result = TimeEntryGenerator.Generate(
            new DateOnly(2026, 7, 6), new DateOnly(2026, 7, 17), 8, holidays, 1);

        var friday = Assert.Single(result, e => e.Date == new DateOnly(2026, 7, 10));
        Assert.Equal(8, friday.Hours);
        Assert.DoesNotContain(result, e => e.Date == new DateOnly(2026, 7, 13));
    }

    [Fact]
    public void ConsecutiveHolidays_BothSkipped_DayBeforeFirstShortened()
    {
        var holidays = new HashSet<DateOnly> { new(2026, 4, 2), new(2026, 4, 3) }; // Thu+Fri
        var result = TimeEntryGenerator.Generate(
            new DateOnly(2026, 3, 30), new DateOnly(2026, 4, 3), 8, holidays, 1);

        Assert.Equal(3, result.Count); // Mon, Tue, Wed
        var wednesday = Assert.Single(result, e => e.Date == new DateOnly(2026, 4, 1));
        Assert.Equal(5, wednesday.Hours);
    }

    [Fact]
    public void ShorteningBelowZero_FloorsAtZero_AndFlags()
    {
        var holidays = new HashSet<DateOnly> { new(2026, 6, 24) };
        var result = TimeEntryGenerator.Generate(
            new DateOnly(2026, 6, 23), new DateOnly(2026, 6, 23), 2, holidays, 1);

        var entry = Assert.Single(result);
        Assert.Equal(0, entry.Hours);
        Assert.True(entry.HitZeroFloor);
    }

    [Fact]
    public void EndBeforeStart_ReturnsEmpty()
    {
        var result = TimeEntryGenerator.Generate(
            new DateOnly(2026, 7, 10), new DateOnly(2026, 7, 6), 8, NoHolidays, 1);
        Assert.Empty(result);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test`
Expected: compile error `TimeEntryGenerator` not found.

- [ ] **Step 4: Implement the generator**

```csharp
// src/7PaceDesktop.Core/TimeEntryGenerator.cs
using PaceDesktop.Core.Models;

namespace PaceDesktop.Core;

public static class TimeEntryGenerator
{
    private const double PreHolidayReduction = 3;

    public static IReadOnlyList<TimeEntry> Generate(
        DateOnly start, DateOnly end, double hoursPerDay,
        IReadOnlySet<DateOnly> holidays, int workItemId)
    {
        var entries = new List<TimeEntry>();
        for (var date = start; date <= end; date = date.AddDays(1))
        {
            if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
            if (holidays.Contains(date)) continue;

            var hours = hoursPerDay;
            var hitFloor = false;
            if (holidays.Contains(date.AddDays(1)))
            {
                hours -= PreHolidayReduction;
                if (hours <= 0) { hours = 0; hitFloor = true; }
            }
            entries.Add(new TimeEntry(date, hours, workItemId, hitFloor));
        }
        return entries;
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test`
Expected: all 6 tests PASS.

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat: models and TimeEntryGenerator with holiday shortening rule"
```

---

### Task 3: Settings & work item persistence

**Files:**
- Create: `src/7PaceDesktop.Core/Storage/AppSettings.cs`, `src/7PaceDesktop.Core/Storage/SettingsStore.cs`, `src/7PaceDesktop.Core/Storage/WorkItemStore.cs`
- Test: `tests/7PaceDesktop.Tests/StorageTests.cs`

**Interfaces:**
- Consumes: `WorkItem`, `Holiday` from Task 2.
- Produces:
  - `class AppSettings { string OrganizationName; double LastDailyHours; Dictionary<int, List<Holiday>> HolidayCache; }`
  - `class SettingsStore { SettingsStore(string baseDir); AppSettings Load(); void Save(AppSettings s); }`
  - `class WorkItemStore { WorkItemStore(string baseDir); IReadOnlyList<WorkItem> Load(); void Save(IEnumerable<WorkItem> items); }`
  - `static string AppPaths.DefaultBaseDir` → `%AppData%\7PaceDesktop`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/7PaceDesktop.Tests/StorageTests.cs
using PaceDesktop.Core.Models;
using PaceDesktop.Core.Storage;

namespace PaceDesktop.Tests;

public class StorageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "7PaceDesktopTests", Guid.NewGuid().ToString("N"));

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void SettingsStore_RoundTrips()
    {
        var store = new SettingsStore(_dir);
        var settings = new AppSettings
        {
            OrganizationName = "icore",
            LastDailyHours = 7.5,
            HolidayCache = { [2026] = [new Holiday(new DateOnly(2026, 6, 24), "Midsommarafton")] }
        };
        store.Save(settings);

        var loaded = new SettingsStore(_dir).Load();
        Assert.Equal("icore", loaded.OrganizationName);
        Assert.Equal(7.5, loaded.LastDailyHours);
        Assert.Equal("Midsommarafton", loaded.HolidayCache[2026][0].Name);
    }

    [Fact]
    public void SettingsStore_Load_WhenNoFile_ReturnsDefaults()
    {
        var loaded = new SettingsStore(_dir).Load();
        Assert.Equal("", loaded.OrganizationName);
        Assert.Equal(8, loaded.LastDailyHours);
        Assert.Empty(loaded.HolidayCache);
    }

    [Fact]
    public void WorkItemStore_RoundTrips_AndDefaultsEmpty()
    {
        var store = new WorkItemStore(_dir);
        Assert.Empty(store.Load());

        store.Save([new WorkItem(79023, "Product Development", true)]);
        var loaded = new WorkItemStore(_dir).Load();
        var item = Assert.Single(loaded);
        Assert.Equal(79023, item.Id);
        Assert.True(item.IsFavorite);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test`
Expected: compile errors for missing types.

- [ ] **Step 3: Implement storage**

```csharp
// src/7PaceDesktop.Core/Storage/AppSettings.cs
using PaceDesktop.Core.Models;

namespace PaceDesktop.Core.Storage;

public sealed class AppSettings
{
    public string OrganizationName { get; set; } = "";
    public double LastDailyHours { get; set; } = 8;
    public Dictionary<int, List<Holiday>> HolidayCache { get; set; } = new();
}

// src/7PaceDesktop.Core/Storage/SettingsStore.cs
using System.Text.Json;

namespace PaceDesktop.Core.Storage;

public static class AppPaths
{
    public static string DefaultBaseDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "7PaceDesktop");
}

public sealed class SettingsStore(string baseDir)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private string FilePath => Path.Combine(baseDir, "settings.json");

    public AppSettings Load()
    {
        if (!File.Exists(FilePath)) return new AppSettings();
        return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options) ?? new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(baseDir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, Options));
    }
}

// src/7PaceDesktop.Core/Storage/WorkItemStore.cs
using System.Text.Json;
using PaceDesktop.Core.Models;

namespace PaceDesktop.Core.Storage;

public sealed class WorkItemStore(string baseDir)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private string FilePath => Path.Combine(baseDir, "workitems.json");

    public IReadOnlyList<WorkItem> Load()
    {
        if (!File.Exists(FilePath)) return [];
        return JsonSerializer.Deserialize<List<WorkItem>>(File.ReadAllText(FilePath), Options) ?? [];
    }

    public void Save(IEnumerable<WorkItem> items)
    {
        Directory.CreateDirectory(baseDir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(items.ToList(), Options));
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test`
Expected: all tests PASS.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: settings and work item JSON persistence"
```

---

### Task 4: SwedishHolidayService (Nager.Date + cache + fallback)

**Files:**
- Create: `src/7PaceDesktop.Core/Services/SwedishHolidayService.cs`
- Test: `tests/7PaceDesktop.Tests/SwedishHolidayServiceTests.cs`

**Interfaces:**
- Consumes: `SettingsStore`, `AppSettings`, `Holiday` from Task 3.
- Produces:
  - `record HolidayLookup(IReadOnlySet<DateOnly> Dates, bool IsIncomplete)` — `IsIncomplete=true` means fetch failed and no cache existed for at least one requested year (UI must warn).
  - `class SwedishHolidayService { SwedishHolidayService(HttpClient http, SettingsStore store); Task<HolidayLookup> GetHolidaysAsync(int fromYear, int toYear, CancellationToken ct = default); }`

- [ ] **Step 1: Write the failing tests** (fake `HttpMessageHandler`, temp settings dir)

```csharp
// tests/7PaceDesktop.Tests/SwedishHolidayServiceTests.cs
using System.Net;
using PaceDesktop.Core.Services;
using PaceDesktop.Core.Storage;

namespace PaceDesktop.Tests;

public class SwedishHolidayServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "7PaceDesktopTests", Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Calls++; return Task.FromResult(responder(request)); }
    }

    private const string NagerJson =
        """[{"date":"2026-06-24","localName":"Midsommarafton","name":"Midsummer Eve"},{"date":"2026-12-25","localName":"Juldagen","name":"Christmas Day"}]""";

    [Fact]
    public async Task Fetch_ParsesAndCaches()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(NagerJson) });
        var store = new SettingsStore(_dir);
        var service = new SwedishHolidayService(new HttpClient(handler), store);

        var result = await service.GetHolidaysAsync(2026, 2026);

        Assert.False(result.IsIncomplete);
        Assert.Contains(new DateOnly(2026, 6, 24), result.Dates);
        Assert.Contains(new DateOnly(2026, 12, 25), result.Dates);
        Assert.True(store.Load().HolidayCache.ContainsKey(2026));
    }

    [Fact]
    public async Task SecondCall_UsesCache_NoSecondHttpCall()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(NagerJson) });
        var store = new SettingsStore(_dir);
        var service = new SwedishHolidayService(new HttpClient(handler), store);

        await service.GetHolidaysAsync(2026, 2026);
        await service.GetHolidaysAsync(2026, 2026);

        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task FetchFails_NoCache_ReturnsIncompleteEmpty()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var service = new SwedishHolidayService(new HttpClient(handler), new SettingsStore(_dir));

        var result = await service.GetHolidaysAsync(2026, 2026);

        Assert.True(result.IsIncomplete);
        Assert.Empty(result.Dates);
    }

    [Fact]
    public async Task YearBoundary_FetchesBothYears()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(NagerJson) });
        var service = new SwedishHolidayService(new HttpClient(handler), new SettingsStore(_dir));

        await service.GetHolidaysAsync(2026, 2027);

        Assert.Equal(2, handler.Calls);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test`
Expected: compile errors.

- [ ] **Step 3: Implement the service**

```csharp
// src/7PaceDesktop.Core/Services/SwedishHolidayService.cs
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using PaceDesktop.Core.Models;
using PaceDesktop.Core.Storage;

namespace PaceDesktop.Core.Services;

public sealed record HolidayLookup(IReadOnlySet<DateOnly> Dates, bool IsIncomplete);

public sealed class SwedishHolidayService(HttpClient http, SettingsStore store)
{
    private sealed record NagerHoliday(
        [property: JsonPropertyName("date")] DateOnly Date,
        [property: JsonPropertyName("localName")] string LocalName);

    public async Task<HolidayLookup> GetHolidaysAsync(int fromYear, int toYear, CancellationToken ct = default)
    {
        var settings = store.Load();
        var dates = new HashSet<DateOnly>();
        var incomplete = false;
        var cacheChanged = false;

        for (var year = fromYear; year <= toYear; year++)
        {
            if (settings.HolidayCache.TryGetValue(year, out var cached))
            {
                foreach (var h in cached) dates.Add(h.Date);
                continue;
            }
            try
            {
                var fetched = await http.GetFromJsonAsync<List<NagerHoliday>>(
                    $"https://date.nager.at/api/v3/publicholidays/{year}/SE", ct) ?? [];
                var holidays = fetched.Select(n => new Holiday(n.Date, n.LocalName)).ToList();
                settings.HolidayCache[year] = holidays;
                cacheChanged = true;
                foreach (var h in holidays) dates.Add(h.Date);
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
            {
                incomplete = true;
            }
        }

        if (cacheChanged) store.Save(settings);
        return new HolidayLookup(dates, incomplete);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test`
Expected: all tests PASS.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: Swedish holiday service with per-year cache and offline fallback"
```

---

### Task 5: CredentialStore (Windows Credential Manager)

**Files:**
- Create: `src/7PaceDesktop.Core/Services/CredentialStore.cs`
- Test: `tests/7PaceDesktop.Tests/CredentialStoreTests.cs`

**Interfaces:**
- Produces: `class CredentialStore { void SaveToken(string organization, string token); string? LoadToken(string organization); void DeleteToken(string organization); }`

- [ ] **Step 1: Write the failing test** (real Credential Manager, unique test key, cleaned up)

```csharp
// tests/7PaceDesktop.Tests/CredentialStoreTests.cs
using PaceDesktop.Core.Services;

namespace PaceDesktop.Tests;

public class CredentialStoreTests
{
    [Fact]
    public void SaveLoadDelete_RoundTrips()
    {
        var store = new CredentialStore();
        var org = "unittest-" + Guid.NewGuid().ToString("N");
        try
        {
            Assert.Null(store.LoadToken(org));
            store.SaveToken(org, "secret-token-123");
            Assert.Equal("secret-token-123", store.LoadToken(org));
        }
        finally
        {
            store.DeleteToken(org);
        }
        Assert.Null(store.LoadToken(org));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter CredentialStoreTests`
Expected: compile error.

- [ ] **Step 3: Implement**

```csharp
// src/7PaceDesktop.Core/Services/CredentialStore.cs
using Meziantou.Framework.Win32;

namespace PaceDesktop.Core.Services;

public sealed class CredentialStore
{
    private static string Key(string organization) => $"7PaceDesktop:{organization}";

    public void SaveToken(string organization, string token) =>
        CredentialManager.WriteCredential(Key(organization), "token", token,
            comment: "7Pace Timetracker API token", CredentialPersistence.LocalMachine);

    public string? LoadToken(string organization) =>
        CredentialManager.ReadCredential(Key(organization))?.Password;

    public void DeleteToken(string organization)
    {
        if (CredentialManager.ReadCredential(Key(organization)) is not null)
            CredentialManager.DeleteCredential(Key(organization));
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter CredentialStoreTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: API token storage in Windows Credential Manager"
```

---

### Task 6: PaceApiClient (verify contract FIRST, then implement)

**Files:**
- Create: `src/7PaceDesktop.Core/Services/PaceApiClient.cs`, `src/7PaceDesktop.Core/Services/IWorkLogClient.cs`
- Test: `tests/7PaceDesktop.Tests/PaceApiClientTests.cs`

**Interfaces:**
- Consumes: `TimeEntry` from Task 2.
- Produces:
  - `interface IWorkLogClient { Task SubmitAsync(TimeEntry entry, CancellationToken ct = default); }`
  - `class PaceApiClient(HttpClient http, string organization, string token) : IWorkLogClient`
  - `class PaceApiException(int statusCode, string message) : Exception` with `int StatusCode` property.

- [ ] **Step 1: VERIFY THE API CONTRACT (manual, blocking)**

Open `https://<org>.timetracker.7pace.com/api/rest/help` (Swagger) for the real organization, authenticated as the user. Confirm for the work-log create endpoint:
1. Exact path and required `api-version` query parameter.
2. Auth scheme (`Authorization: Bearer <token>` is 7Pace's documented scheme for API tokens — confirm).
3. Field names and types for: work item id, date/timestamp, duration (seconds vs minutes), comment — and whether `comment` may be omitted.

Record the confirmed contract as a comment block at the top of `PaceApiClient.cs`. **If the contract differs from the code below, update the code and tests to match the real contract — the Swagger page wins.** The values below are the best-effort default:
`POST https://{org}.timetracker.7pace.com/api/rest/workLogs?api-version=3.2` with body `{"workItemId": int, "timestamp": "yyyy-MM-ddTHH:mm:ss", "length": seconds}` and header `Authorization: Bearer {token}`.

- [ ] **Step 2: Write the failing tests**

```csharp
// tests/7PaceDesktop.Tests/PaceApiClientTests.cs
using System.Net;
using System.Text.Json;
using PaceDesktop.Core.Models;
using PaceDesktop.Core.Services;

namespace PaceDesktop.Tests;

public class PaceApiClientTests
{
    private sealed class CapturingHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public HttpRequestMessage? Request;
        public string? Body;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(status) { Content = new StringContent("{}") };
        }
    }

    [Fact]
    public async Task Submit_SendsExpectedRequest()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK);
        var client = new PaceApiClient(new HttpClient(handler), "icore", "tok123");

        await client.SubmitAsync(new TimeEntry(new DateOnly(2026, 7, 1), 7.5, 79023));

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.StartsWith("https://icore.timetracker.7pace.com/api/rest/workLogs", handler.Request.RequestUri!.ToString());
        Assert.Equal("Bearer", handler.Request.Headers.Authorization!.Scheme);
        Assert.Equal("tok123", handler.Request.Headers.Authorization.Parameter);

        using var doc = JsonDocument.Parse(handler.Body!);
        Assert.Equal(79023, doc.RootElement.GetProperty("workItemId").GetInt32());
        Assert.Equal(27000, doc.RootElement.GetProperty("length").GetInt32()); // 7.5h in seconds
        Assert.StartsWith("2026-07-01T", doc.RootElement.GetProperty("timestamp").GetString());
    }

    [Fact]
    public async Task Submit_NonSuccess_ThrowsWithStatusCode()
    {
        var handler = new CapturingHandler(HttpStatusCode.Unauthorized);
        var client = new PaceApiClient(new HttpClient(handler), "icore", "bad");

        var ex = await Assert.ThrowsAsync<PaceApiException>(() =>
            client.SubmitAsync(new TimeEntry(new DateOnly(2026, 7, 1), 8, 79023)));
        Assert.Equal(401, ex.StatusCode);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test --filter PaceApiClientTests`
Expected: compile errors.

- [ ] **Step 4: Implement**

```csharp
// src/7PaceDesktop.Core/Services/IWorkLogClient.cs
using PaceDesktop.Core.Models;

namespace PaceDesktop.Core.Services;

public interface IWorkLogClient
{
    Task SubmitAsync(TimeEntry entry, CancellationToken ct = default);
}

// src/7PaceDesktop.Core/Services/PaceApiClient.cs
// CONTRACT: verified against https://<org>.timetracker.7pace.com/api/rest/help on <date>.
// <paste confirmed endpoint, auth scheme, and field list here in Task 6 Step 1>
using System.Net.Http.Headers;
using System.Net.Http.Json;
using PaceDesktop.Core.Models;

namespace PaceDesktop.Core.Services;

public sealed class PaceApiException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public sealed class PaceApiClient(HttpClient http, string organization, string token) : IWorkLogClient
{
    public async Task SubmitAsync(TimeEntry entry, CancellationToken ct = default)
    {
        var url = $"https://{organization}.timetracker.7pace.com/api/rest/workLogs?api-version=3.2";
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(new
            {
                workItemId = entry.WorkItemId,
                timestamp = entry.Date.ToDateTime(new TimeOnly(9, 0)).ToString("yyyy-MM-ddTHH:mm:ss"),
                length = (int)(entry.Hours * 3600)
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new PaceApiException((int)response.StatusCode,
                $"7Pace API error {(int)response.StatusCode}: {body}");
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test`
Expected: all tests PASS.

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat: 7Pace work log API client"
```

---

### Task 7: MainViewModel — generate & submit flow

**Files:**
- Create: `src/7PaceDesktop.App/ViewModels/EntryRowViewModel.cs`, `src/7PaceDesktop.App/ViewModels/MainViewModel.cs`
- Test: `tests/7PaceDesktop.Tests/MainViewModelTests.cs` (add `<ProjectReference>` from Tests to App; App must set `<UseWPF>true</UseWPF>` which is already the case)

**Interfaces:**
- Consumes: `TimeEntryGenerator`, `SwedishHolidayService`, `IWorkLogClient`, `WorkItemStore`, `SettingsStore`.
- Produces:
  - `enum RowStatus { Pending, Sending, Ok, Failed }`
  - `class EntryRowViewModel { DateOnly Date; double Hours; WorkItem SelectedWorkItem; bool HitZeroFloor; RowStatus Status; string? Error; }`
  - `class MainViewModel { DateTime? StartDate; DateTime? EndDate; double HoursPerDay; bool Simulate; ObservableCollection<EntryRowViewModel> Entries; ObservableCollection<WorkItem> WorkItems; double TotalHours; string? Warning; IAsyncRelayCommand GenerateCommand; IAsyncRelayCommand RegisterCommand; IAsyncRelayCommand<EntryRowViewModel> RetryRowCommand; }`
  - `MainViewModel` constructor: `(SwedishHolidayService holidays, IWorkLogClient client, WorkItemStore workItems, SettingsStore settings)`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/7PaceDesktop.Tests/MainViewModelTests.cs
using System.Net;
using PaceDesktop.App.ViewModels;
using PaceDesktop.Core.Models;
using PaceDesktop.Core.Services;
using PaceDesktop.Core.Storage;

namespace PaceDesktop.Tests;

public class MainViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "7PaceDesktopTests", Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private sealed class FakeWorkLogClient : IWorkLogClient
    {
        public List<TimeEntry> Submitted = [];
        public Func<TimeEntry, Exception?>? FailWhen;
        public Task SubmitAsync(TimeEntry entry, CancellationToken ct = default)
        {
            if (FailWhen?.Invoke(entry) is { } ex) throw ex;
            Submitted.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class EmptyHolidayHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") });
    }

    private MainViewModel CreateVm(FakeWorkLogClient client)
    {
        var settingsStore = new SettingsStore(_dir);
        var workItemStore = new WorkItemStore(_dir);
        workItemStore.Save([new WorkItem(79023, "Product Development", true), new WorkItem(79055, "Admin & internal", false)]);
        var holidays = new SwedishHolidayService(new HttpClient(new EmptyHolidayHandler()), settingsStore);
        return new MainViewModel(holidays, client, workItemStore, settingsStore);
    }

    [Fact]
    public async Task Generate_PopulatesRows_WithFavoriteWorkItem_AndTotal()
    {
        var vm = CreateVm(new FakeWorkLogClient());
        vm.StartDate = new DateTime(2026, 7, 6);
        vm.EndDate = new DateTime(2026, 7, 10);
        vm.HoursPerDay = 8;

        await vm.GenerateCommand.ExecuteAsync(null);

        Assert.Equal(5, vm.Entries.Count);
        Assert.All(vm.Entries, r => Assert.Equal(79023, r.SelectedWorkItem.Id));
        Assert.Equal(40, vm.TotalHours);
    }

    [Fact]
    public async Task Register_Simulate_SubmitsNothing_MarksOk()
    {
        var client = new FakeWorkLogClient();
        var vm = CreateVm(client);
        vm.StartDate = new DateTime(2026, 7, 6);
        vm.EndDate = new DateTime(2026, 7, 6);
        vm.HoursPerDay = 8;
        await vm.GenerateCommand.ExecuteAsync(null);

        vm.Simulate = true;
        await vm.RegisterCommand.ExecuteAsync(null);

        Assert.Empty(client.Submitted);
        Assert.All(vm.Entries, r => Assert.Equal(RowStatus.Ok, r.Status));
    }

    [Fact]
    public async Task Register_SubmitsAllRows_AndMarksStatus()
    {
        var client = new FakeWorkLogClient();
        var vm = CreateVm(client);
        vm.StartDate = new DateTime(2026, 7, 6);
        vm.EndDate = new DateTime(2026, 7, 10);
        vm.HoursPerDay = 8;
        await vm.GenerateCommand.ExecuteAsync(null);

        await vm.RegisterCommand.ExecuteAsync(null);

        Assert.Equal(5, client.Submitted.Count);
        Assert.All(vm.Entries, r => Assert.Equal(RowStatus.Ok, r.Status));
    }

    [Fact]
    public async Task Register_FailedRow_IsMarkedFailed_AndRetryable()
    {
        var client = new FakeWorkLogClient();
        var failDate = new DateOnly(2026, 7, 8);
        client.FailWhen = e => e.Date == failDate ? new PaceApiException(500, "boom") : null;
        var vm = CreateVm(client);
        vm.StartDate = new DateTime(2026, 7, 6);
        vm.EndDate = new DateTime(2026, 7, 10);
        vm.HoursPerDay = 8;
        await vm.GenerateCommand.ExecuteAsync(null);

        await vm.RegisterCommand.ExecuteAsync(null);

        var failed = Assert.Single(vm.Entries, r => r.Status == RowStatus.Failed);
        Assert.Equal(failDate, failed.Date);
        Assert.Equal(4, client.Submitted.Count);

        client.FailWhen = null;
        await vm.RetryRowCommand.ExecuteAsync(failed);
        Assert.Equal(RowStatus.Ok, failed.Status);
        Assert.Equal(5, client.Submitted.Count);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter MainViewModelTests`
Expected: compile errors. (First: `dotnet add tests/7PaceDesktop.Tests reference src/7PaceDesktop.App` and set `<EnableWindowsTargeting>` not needed since already net10.0-windows.)

- [ ] **Step 3: Implement the view models**

```csharp
// src/7PaceDesktop.App/ViewModels/EntryRowViewModel.cs
using CommunityToolkit.Mvvm.ComponentModel;
using PaceDesktop.Core.Models;

namespace PaceDesktop.App.ViewModels;

public enum RowStatus { Pending, Sending, Ok, Failed }

public partial class EntryRowViewModel(DateOnly date, double hours, WorkItem workItem, bool hitZeroFloor) : ObservableObject
{
    public DateOnly Date { get; } = date;
    public bool HitZeroFloor { get; } = hitZeroFloor;

    [ObservableProperty] private double _hours = hours;
    [ObservableProperty] private WorkItem _selectedWorkItem = workItem;
    [ObservableProperty] private RowStatus _status = RowStatus.Pending;
    [ObservableProperty] private string? _error;

    public TimeEntry ToEntry() => new(Date, Hours, SelectedWorkItem.Id, HitZeroFloor);
}

// src/7PaceDesktop.App/ViewModels/MainViewModel.cs
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PaceDesktop.Core;
using PaceDesktop.Core.Models;
using PaceDesktop.Core.Services;
using PaceDesktop.Core.Storage;

namespace PaceDesktop.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private const int MaxConcurrentSubmits = 4;

    private readonly SwedishHolidayService _holidays;
    private readonly IWorkLogClient _client;
    private readonly SettingsStore _settingsStore;

    public ObservableCollection<WorkItem> WorkItems { get; } = [];
    public ObservableCollection<EntryRowViewModel> Entries { get; } = [];

    [ObservableProperty] private DateTime? _startDate;
    [ObservableProperty] private DateTime? _endDate;
    [ObservableProperty] private double _hoursPerDay;
    [ObservableProperty] private bool _simulate;
    [ObservableProperty] private double _totalHours;
    [ObservableProperty] private string? _warning;

    public MainViewModel(SwedishHolidayService holidays, IWorkLogClient client,
        WorkItemStore workItemStore, SettingsStore settingsStore)
    {
        _holidays = holidays;
        _client = client;
        _settingsStore = settingsStore;
        foreach (var wi in workItemStore.Load()) WorkItems.Add(wi);
        HoursPerDay = settingsStore.Load().LastDailyHours;
    }

    private WorkItem Favorite => WorkItems.FirstOrDefault(w => w.IsFavorite) ?? WorkItems[0];

    [RelayCommand]
    private async Task GenerateAsync()
    {
        if (StartDate is not { } start || EndDate is not { } end || WorkItems.Count == 0) return;
        var from = DateOnly.FromDateTime(start);
        var to = DateOnly.FromDateTime(end);

        var lookup = await _holidays.GetHolidaysAsync(from.Year, to.AddDays(1).Year);
        Warning = lookup.IsIncomplete
            ? "Kunde inte hämta röda dagar — alla dagar behandlas som vanliga arbetsdagar."
            : null;

        Entries.Clear();
        foreach (var e in TimeEntryGenerator.Generate(from, to, HoursPerDay, lookup.Dates, Favorite.Id))
            Entries.Add(new EntryRowViewModel(e.Date, e.Hours, Favorite, e.HitZeroFloor));
        RecalculateTotal();

        var settings = _settingsStore.Load();
        settings.LastDailyHours = HoursPerDay;
        _settingsStore.Save(settings);
    }

    public void RecalculateTotal() => TotalHours = Entries.Sum(r => r.Hours);

    [RelayCommand]
    private async Task RegisterAsync()
    {
        using var gate = new SemaphoreSlim(MaxConcurrentSubmits);
        var tasks = Entries.Where(r => r.Status != RowStatus.Ok).Select(async row =>
        {
            await gate.WaitAsync();
            try { await SubmitRowAsync(row); }
            finally { gate.Release(); }
        });
        await Task.WhenAll(tasks);
    }

    [RelayCommand]
    private Task RetryRowAsync(EntryRowViewModel row) => SubmitRowAsync(row);

    private async Task SubmitRowAsync(EntryRowViewModel row)
    {
        row.Status = RowStatus.Sending;
        row.Error = null;
        if (Simulate)
        {
            row.Status = RowStatus.Ok;
            return;
        }
        try
        {
            await _client.SubmitAsync(row.ToEntry());
            row.Status = RowStatus.Ok;
        }
        catch (Exception ex)
        {
            row.Status = RowStatus.Failed;
            row.Error = ex.Message;
        }
    }

    public void RemoveRow(EntryRowViewModel row)
    {
        Entries.Remove(row);
        RecalculateTotal();
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test`
Expected: all tests PASS.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: main view model with generate, simulate, submit, and retry"
```

---

### Task 8: First-run wizard, settings dialog, work item management

**Files:**
- Create: `src/7PaceDesktop.App/ViewModels/SetupViewModel.cs`, `src/7PaceDesktop.App/Views/SetupWindow.xaml(+.cs)`, `src/7PaceDesktop.App/ViewModels/WorkItemsViewModel.cs`, `src/7PaceDesktop.App/Views/WorkItemsWindow.xaml(+.cs)`
- Test: `tests/7PaceDesktop.Tests/SetupViewModelTests.cs`, `tests/7PaceDesktop.Tests/WorkItemsViewModelTests.cs`

**Interfaces:**
- Consumes: `SettingsStore`, `WorkItemStore`, `CredentialStore`.
- Produces:
  - `class SetupViewModel { string OrganizationName; string Token; string WorkItemIdText; string WorkItemName; bool CanSave; bool TrySave(); }` — validates all fields non-empty and work item id numeric; on save persists settings, token, and the first work item (as favorite). Also reused by the Settings dialog with `RequireWorkItem = false`.
  - `class WorkItemsViewModel { ObservableCollection<WorkItem> Items; string NewIdText; string NewName; IRelayCommand AddCommand; IRelayCommand<WorkItem> RemoveCommand; IRelayCommand<WorkItem> SetFavoriteCommand; }` — persists via `WorkItemStore` on every change; enforces exactly one favorite and at least one item (Remove disabled on last item).

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/7PaceDesktop.Tests/SetupViewModelTests.cs
using PaceDesktop.App.ViewModels;
using PaceDesktop.Core.Services;
using PaceDesktop.Core.Storage;

namespace PaceDesktop.Tests;

public class SetupViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "7PaceDesktopTests", Guid.NewGuid().ToString("N"));
    private readonly CredentialStore _creds = new();
    private string? _createdOrg;

    public void Dispose()
    {
        if (_createdOrg is not null) _creds.DeleteToken(_createdOrg);
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void CanSave_FalseUntilAllFieldsValid()
    {
        var vm = new SetupViewModel(new SettingsStore(_dir), new WorkItemStore(_dir), _creds);
        Assert.False(vm.CanSave);
        vm.OrganizationName = "unittest-org";
        vm.Token = "tok";
        vm.WorkItemIdText = "not-a-number";
        vm.WorkItemName = "PD";
        Assert.False(vm.CanSave);
        vm.WorkItemIdText = "79023";
        Assert.True(vm.CanSave);
    }

    [Fact]
    public void TrySave_PersistsEverything()
    {
        _createdOrg = "unittest-" + Guid.NewGuid().ToString("N");
        var vm = new SetupViewModel(new SettingsStore(_dir), new WorkItemStore(_dir), _creds)
        {
            OrganizationName = _createdOrg,
            Token = "tok123",
            WorkItemIdText = "79023",
            WorkItemName = "Product Development"
        };

        Assert.True(vm.TrySave());

        Assert.Equal(_createdOrg, new SettingsStore(_dir).Load().OrganizationName);
        Assert.Equal("tok123", _creds.LoadToken(_createdOrg));
        var item = Assert.Single(new WorkItemStore(_dir).Load());
        Assert.True(item.IsFavorite);
        Assert.Equal(79023, item.Id);
    }
}

// tests/7PaceDesktop.Tests/WorkItemsViewModelTests.cs
using PaceDesktop.App.ViewModels;
using PaceDesktop.Core.Models;
using PaceDesktop.Core.Storage;

namespace PaceDesktop.Tests;

public class WorkItemsViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "7PaceDesktopTests", Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private WorkItemsViewModel CreateVm()
    {
        var store = new WorkItemStore(_dir);
        store.Save([new WorkItem(1, "First", true)]);
        return new WorkItemsViewModel(store);
    }

    [Fact]
    public void Add_AppendsAndPersists()
    {
        var vm = CreateVm();
        vm.NewIdText = "2";
        vm.NewName = "Second";
        vm.AddCommand.Execute(null);

        Assert.Equal(2, vm.Items.Count);
        Assert.Equal(2, new WorkItemStore(_dir).Load().Count);
    }

    [Fact]
    public void SetFavorite_MovesFavoriteExclusively()
    {
        var vm = CreateVm();
        vm.NewIdText = "2"; vm.NewName = "Second"; vm.AddCommand.Execute(null);

        vm.SetFavoriteCommand.Execute(vm.Items.First(i => i.Id == 2));

        Assert.True(vm.Items.Single(i => i.Id == 2).IsFavorite);
        Assert.False(vm.Items.Single(i => i.Id == 1).IsFavorite);
        Assert.Single(new WorkItemStore(_dir).Load(), i => i.IsFavorite);
    }

    [Fact]
    public void Remove_LastItem_IsBlocked()
    {
        var vm = CreateVm();
        vm.RemoveCommand.Execute(vm.Items[0]);
        Assert.Single(vm.Items); // still there
    }

    [Fact]
    public void Remove_FavoriteItem_PromotesAnother()
    {
        var vm = CreateVm();
        vm.NewIdText = "2"; vm.NewName = "Second"; vm.AddCommand.Execute(null);

        vm.RemoveCommand.Execute(vm.Items.Single(i => i.Id == 1)); // remove the favorite

        var remaining = Assert.Single(vm.Items);
        Assert.True(remaining.IsFavorite);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "SetupViewModelTests|WorkItemsViewModelTests"`
Expected: compile errors.

- [ ] **Step 3: Implement the view models**

```csharp
// src/7PaceDesktop.App/ViewModels/SetupViewModel.cs
using CommunityToolkit.Mvvm.ComponentModel;
using PaceDesktop.Core.Models;
using PaceDesktop.Core.Services;
using PaceDesktop.Core.Storage;

namespace PaceDesktop.App.ViewModels;

public partial class SetupViewModel(SettingsStore settingsStore, WorkItemStore workItemStore, CredentialStore credentials)
    : ObservableObject
{
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanSave))] private string _organizationName = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanSave))] private string _token = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanSave))] private string _workItemIdText = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanSave))] private string _workItemName = "";

    /// <summary>False when reused as the settings dialog (work items already exist).</summary>
    public bool RequireWorkItem { get; init; } = true;

    public bool CanSave =>
        !string.IsNullOrWhiteSpace(OrganizationName) &&
        !string.IsNullOrWhiteSpace(Token) &&
        (!RequireWorkItem ||
         (int.TryParse(WorkItemIdText, out var id) && id > 0 && !string.IsNullOrWhiteSpace(WorkItemName)));

    public bool TrySave()
    {
        if (!CanSave) return false;

        var settings = settingsStore.Load();
        settings.OrganizationName = OrganizationName.Trim();
        settingsStore.Save(settings);
        credentials.SaveToken(settings.OrganizationName, Token.Trim());

        if (RequireWorkItem)
            workItemStore.Save([new WorkItem(int.Parse(WorkItemIdText), WorkItemName.Trim(), IsFavorite: true)]);
        return true;
    }
}

// src/7PaceDesktop.App/ViewModels/WorkItemsViewModel.cs
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PaceDesktop.Core.Models;
using PaceDesktop.Core.Storage;

namespace PaceDesktop.App.ViewModels;

public partial class WorkItemsViewModel : ObservableObject
{
    private readonly WorkItemStore _store;

    public ObservableCollection<WorkItem> Items { get; }

    [ObservableProperty] private string _newIdText = "";
    [ObservableProperty] private string _newName = "";

    public WorkItemsViewModel(WorkItemStore store)
    {
        _store = store;
        Items = new ObservableCollection<WorkItem>(store.Load());
    }

    private void Persist() => _store.Save(Items);

    [RelayCommand]
    private void Add()
    {
        if (!int.TryParse(NewIdText, out var id) || id <= 0 || string.IsNullOrWhiteSpace(NewName)) return;
        if (Items.Any(i => i.Id == id)) return;
        Items.Add(new WorkItem(id, NewName.Trim(), IsFavorite: Items.Count == 0));
        NewIdText = "";
        NewName = "";
        Persist();
    }

    [RelayCommand]
    private void Remove(WorkItem item)
    {
        if (Items.Count <= 1) return;
        var wasFavorite = item.IsFavorite;
        Items.Remove(item);
        if (wasFavorite && Items.Count > 0)
        {
            var promoted = Items[0] with { IsFavorite = true };
            Items[0] = promoted;
        }
        Persist();
    }

    [RelayCommand]
    private void SetFavorite(WorkItem item)
    {
        for (var i = 0; i < Items.Count; i++)
            Items[i] = Items[i] with { IsFavorite = Items[i].Id == item.Id };
        Persist();
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test`
Expected: all tests PASS.

- [ ] **Step 5: Build the XAML windows**

```xml
<!-- src/7PaceDesktop.App/Views/SetupWindow.xaml -->
<Window x:Class="PaceDesktop.App.Views.SetupWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="7PaceDesktop — Konfiguration" Width="420" SizeToContent="Height"
        WindowStartupLocation="CenterScreen" ResizeMode="NoResize">
    <StackPanel Margin="16">
        <TextBlock Text="7Pace-organisation (namnet i {org}.timetracker.7pace.com):"/>
        <TextBox Text="{Binding OrganizationName, UpdateSourceTrigger=PropertyChanged}" Margin="0,4,0,12"/>
        <TextBlock Text="7Pace API-token:"/>
        <TextBox Text="{Binding Token, UpdateSourceTrigger=PropertyChanged}" Margin="0,4,0,12"/>
        <StackPanel Visibility="{Binding WorkItemSectionVisibility}">
            <TextBlock Text="Work item-ID att logga tid mot:"/>
            <TextBox Text="{Binding WorkItemIdText, UpdateSourceTrigger=PropertyChanged}" Margin="0,4,0,12"/>
            <TextBlock Text="Visningsnamn för work item:"/>
            <TextBox Text="{Binding WorkItemName, UpdateSourceTrigger=PropertyChanged}" Margin="0,4,0,12"/>
        </StackPanel>
        <Button Content="Spara" IsEnabled="{Binding CanSave}" Click="OnSaveClick"
                HorizontalAlignment="Right" Padding="24,6"/>
    </StackPanel>
</Window>
```

```csharp
// src/7PaceDesktop.App/Views/SetupWindow.xaml.cs
using System.Windows;
using PaceDesktop.App.ViewModels;

namespace PaceDesktop.App.Views;

public partial class SetupWindow : Window
{
    private readonly SetupViewModel _vm;

    public SetupWindow(SetupViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    public Visibility WorkItemSectionVisibility =>
        _vm.RequireWorkItem ? Visibility.Visible : Visibility.Collapsed;

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (_vm.TrySave()) { DialogResult = true; Close(); }
    }
}
```

(Note: bind `WorkItemSectionVisibility` via the window — set `Visibility="{Binding WorkItemSectionVisibility, RelativeSource={RelativeSource AncestorType=Window}}"` in the XAML above.)

```xml
<!-- src/7PaceDesktop.App/Views/WorkItemsWindow.xaml -->
<Window x:Class="PaceDesktop.App.Views.WorkItemsWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Hantera work items" Width="480" Height="360" WindowStartupLocation="CenterOwner">
    <DockPanel Margin="12">
        <StackPanel DockPanel.Dock="Bottom" Orientation="Horizontal" Margin="0,8,0,0">
            <TextBox Width="80" Text="{Binding NewIdText, UpdateSourceTrigger=PropertyChanged}"
                     ToolTip="Work item-ID"/>
            <TextBox Width="220" Margin="8,0" Text="{Binding NewName, UpdateSourceTrigger=PropertyChanged}"
                     ToolTip="Visningsnamn"/>
            <Button Content="Lägg till" Command="{Binding AddCommand}" Padding="12,2"/>
        </StackPanel>
        <DataGrid ItemsSource="{Binding Items}" AutoGenerateColumns="False" CanUserAddRows="False"
                  IsReadOnly="True">
            <DataGrid.Columns>
                <DataGridTextColumn Header="ID" Binding="{Binding Id}" Width="80"/>
                <DataGridTextColumn Header="Namn" Binding="{Binding Name}" Width="*"/>
                <DataGridTemplateColumn Header="Favorit" Width="70">
                    <DataGridTemplateColumn.CellTemplate>
                        <DataTemplate>
                            <Button Content="★" FontWeight="{Binding IsFavorite, Converter={StaticResource FavoriteWeightConverter}}"
                                    Command="{Binding DataContext.SetFavoriteCommand, RelativeSource={RelativeSource AncestorType=Window}}"
                                    CommandParameter="{Binding}"/>
                        </DataTemplate>
                    </DataGridTemplateColumn.CellTemplate>
                </DataGridTemplateColumn>
                <DataGridTemplateColumn Width="70">
                    <DataGridTemplateColumn.CellTemplate>
                        <DataTemplate>
                            <Button Content="Ta bort"
                                    Command="{Binding DataContext.RemoveCommand, RelativeSource={RelativeSource AncestorType=Window}}"
                                    CommandParameter="{Binding}"/>
                        </DataTemplate>
                    </DataGridTemplateColumn.CellTemplate>
                </DataGridTemplateColumn>
            </DataGrid.Columns>
        </DataGrid>
    </DockPanel>
</Window>
```

```csharp
// src/7PaceDesktop.App/Views/WorkItemsWindow.xaml.cs
using System.Windows;
using PaceDesktop.App.ViewModels;

namespace PaceDesktop.App.Views;

public partial class WorkItemsWindow : Window
{
    public WorkItemsWindow(WorkItemsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }
}
```

Add a `FavoriteWeightConverter` (`IsFavorite ? FontWeights.Bold : FontWeights.Normal`) in `src/7PaceDesktop.App/Converters/FavoriteWeightConverter.cs` and register it in `App.xaml` resources:

```csharp
// src/7PaceDesktop.App/Converters/FavoriteWeightConverter.cs
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PaceDesktop.App.Converters;

public sealed class FavoriteWeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? FontWeights.Bold : FontWeights.Normal;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
```

- [ ] **Step 6: Verify build**

Run: `dotnet build`
Expected: `Build succeeded`.

- [ ] **Step 7: Commit**

```bash
git add -A && git commit -m "feat: setup wizard, settings dialog, and work item management"
```

---

### Task 9: MainWindow UI + app startup wiring

**Files:**
- Modify: `src/7PaceDesktop.App/MainWindow.xaml(+.cs)`, `src/7PaceDesktop.App/App.xaml(+.cs)`

**Interfaces:**
- Consumes: everything above.
- Produces: the runnable app.

- [ ] **Step 1: App startup logic**

```csharp
// src/7PaceDesktop.App/App.xaml.cs
using System.Net.Http;
using System.Windows;
using PaceDesktop.App.ViewModels;
using PaceDesktop.App.Views;
using PaceDesktop.Core.Services;
using PaceDesktop.Core.Storage;

namespace PaceDesktop.App;

public partial class App : Application
{
    private static readonly HttpClient Http = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var baseDir = AppPaths.DefaultBaseDir;
        var settingsStore = new SettingsStore(baseDir);
        var workItemStore = new WorkItemStore(baseDir);
        var credentials = new CredentialStore();

        var settings = settingsStore.Load();
        var configured = !string.IsNullOrWhiteSpace(settings.OrganizationName)
                         && credentials.LoadToken(settings.OrganizationName) is not null
                         && workItemStore.Load().Count > 0;

        if (!configured)
        {
            var setup = new SetupWindow(new SetupViewModel(settingsStore, workItemStore, credentials));
            if (setup.ShowDialog() != true) { Shutdown(); return; }
            settings = settingsStore.Load();
        }

        var token = credentials.LoadToken(settings.OrganizationName)!;
        var client = new PaceApiClient(Http, settings.OrganizationName, token);
        var holidays = new SwedishHolidayService(Http, settingsStore);
        var vm = new MainViewModel(holidays, client, workItemStore, settingsStore);

        ShutdownMode = ShutdownMode.OnMainWindowClose;
        MainWindow = new MainWindow(vm, settingsStore, workItemStore, credentials);
        MainWindow.Show();
    }
}
```

Remove `StartupUri="MainWindow.xaml"` from `App.xaml` and add the converter resource:

```xml
<!-- src/7PaceDesktop.App/App.xaml -->
<Application x:Class="PaceDesktop.App.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:conv="clr-namespace:PaceDesktop.App.Converters">
    <Application.Resources>
        <conv:FavoriteWeightConverter x:Key="FavoriteWeightConverter"/>
    </Application.Resources>
</Application>
```

- [ ] **Step 2: MainWindow XAML**

```xml
<!-- src/7PaceDesktop.App/MainWindow.xaml -->
<Window x:Class="PaceDesktop.App.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="7PaceDesktop" Width="760" Height="560" WindowStartupLocation="CenterScreen">
    <DockPanel Margin="12">
        <Menu DockPanel.Dock="Top">
            <MenuItem Header="_Inställningar">
                <MenuItem Header="Organisation &amp; token..." Click="OnOpenSettings"/>
                <MenuItem Header="Hantera work items..." Click="OnOpenWorkItems"/>
            </MenuItem>
        </Menu>

        <StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Margin="0,8">
            <TextBlock Text="Från:" VerticalAlignment="Center"/>
            <DatePicker SelectedDate="{Binding StartDate}" Margin="4,0,12,0"/>
            <TextBlock Text="Till:" VerticalAlignment="Center"/>
            <DatePicker SelectedDate="{Binding EndDate}" Margin="4,0,12,0"/>
            <TextBlock Text="Timmar/dag:" VerticalAlignment="Center"/>
            <TextBox Width="50" Margin="4,0,12,0" Text="{Binding HoursPerDay, UpdateSourceTrigger=PropertyChanged}"/>
            <Button Content="Generera" Command="{Binding GenerateCommand}" Padding="16,4"/>
        </StackPanel>

        <TextBlock DockPanel.Dock="Top" Text="{Binding Warning}" Foreground="DarkOrange"
                   Visibility="{Binding Warning, Converter={StaticResource NullToCollapsedConverter}}"/>

        <StackPanel DockPanel.Dock="Bottom" Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,8,0,0">
            <TextBlock VerticalAlignment="Center" Margin="0,0,16,0">
                <Run Text="Totalt:"/><Run Text="{Binding TotalHours, Mode=OneWay}"/><Run Text="h"/>
            </TextBlock>
            <CheckBox Content="Simulera (skicka inget)" IsChecked="{Binding Simulate}"
                      VerticalAlignment="Center" Margin="0,0,16,0"/>
            <Button Content="Registrera" Command="{Binding RegisterCommand}" Padding="24,6"/>
        </StackPanel>

        <DataGrid ItemsSource="{Binding Entries}" AutoGenerateColumns="False" CanUserAddRows="False">
            <DataGrid.RowStyle>
                <Style TargetType="DataGridRow">
                    <Style.Triggers>
                        <DataTrigger Binding="{Binding HitZeroFloor}" Value="True">
                            <Setter Property="Background" Value="#FFF3CD"/>
                        </DataTrigger>
                    </Style.Triggers>
                </Style>
            </DataGrid.RowStyle>
            <DataGrid.Columns>
                <DataGridTextColumn Header="Datum" Binding="{Binding Date, StringFormat=yyyy-MM-dd}" IsReadOnly="True" Width="100"/>
                <DataGridTextColumn Header="Timmar" Binding="{Binding Hours, UpdateSourceTrigger=PropertyChanged}" Width="70"/>
                <DataGridTemplateColumn Header="Work item" Width="*">
                    <DataGridTemplateColumn.CellTemplate>
                        <DataTemplate>
                            <ComboBox ItemsSource="{Binding DataContext.WorkItems, RelativeSource={RelativeSource AncestorType=Window}}"
                                      SelectedItem="{Binding SelectedWorkItem, UpdateSourceTrigger=PropertyChanged}"
                                      DisplayMemberPath="Name"/>
                        </DataTemplate>
                    </DataGridTemplateColumn.CellTemplate>
                </DataGridTemplateColumn>
                <DataGridTextColumn Header="Status" Binding="{Binding Status}" IsReadOnly="True" Width="70"/>
                <DataGridTextColumn Header="Fel" Binding="{Binding Error}" IsReadOnly="True" Width="140"/>
                <DataGridTemplateColumn Width="130">
                    <DataGridTemplateColumn.CellTemplate>
                        <DataTemplate>
                            <StackPanel Orientation="Horizontal">
                                <Button Content="Skicka om" Margin="0,0,4,0"
                                        Command="{Binding DataContext.RetryRowCommand, RelativeSource={RelativeSource AncestorType=Window}}"
                                        CommandParameter="{Binding}"/>
                                <Button Content="Ta bort" Click="OnRemoveRow"/>
                            </StackPanel>
                        </DataTemplate>
                    </DataGridTemplateColumn.CellTemplate>
                </DataGridTemplateColumn>
            </DataGrid.Columns>
        </DataGrid>
    </DockPanel>
</Window>
```

Add `NullToCollapsedConverter` next to the favorite converter and register in `App.xaml`:

```csharp
// src/7PaceDesktop.App/Converters/NullToCollapsedConverter.cs
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PaceDesktop.App.Converters;

public sealed class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
```

```csharp
// src/7PaceDesktop.App/MainWindow.xaml.cs
using System.Windows;
using PaceDesktop.App.ViewModels;
using PaceDesktop.App.Views;
using PaceDesktop.Core.Services;
using PaceDesktop.Core.Storage;

namespace PaceDesktop.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly SettingsStore _settingsStore;
    private readonly WorkItemStore _workItemStore;
    private readonly CredentialStore _credentials;

    public MainWindow(MainViewModel vm, SettingsStore settingsStore,
        WorkItemStore workItemStore, CredentialStore credentials)
    {
        InitializeComponent();
        _vm = vm;
        _settingsStore = settingsStore;
        _workItemStore = workItemStore;
        _credentials = credentials;
        DataContext = vm;
    }

    private void OnOpenSettings(object sender, RoutedEventArgs e)
    {
        var vm = new SetupViewModel(_settingsStore, _workItemStore, _credentials) { RequireWorkItem = false };
        vm.OrganizationName = _settingsStore.Load().OrganizationName;
        new SetupWindow(vm) { Owner = this }.ShowDialog();
    }

    private void OnOpenWorkItems(object sender, RoutedEventArgs e)
    {
        var dialog = new WorkItemsWindow(new WorkItemsViewModel(_workItemStore)) { Owner = this };
        dialog.ShowDialog();
        _vm.WorkItems.Clear();
        foreach (var wi in _workItemStore.Load()) _vm.WorkItems.Add(wi);
    }

    private void OnRemoveRow(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is EntryRowViewModel row)
            _vm.RemoveRow(row);
    }
}
```

Also delete the scaffolded empty `MainWindow` content and replace with the above. Note: token/org changes via the settings dialog take effect on next app start (the `PaceApiClient` is constructed at startup) — acceptable for v1; a 401 mid-session tells the user to restart after updating the token.

- [ ] **Step 3: Verify build + all tests**

Run: `dotnet build && dotnet test`
Expected: build succeeds, all tests PASS.

- [ ] **Step 4: Manual smoke test**

Run: `dotnet run --project src/7PaceDesktop.App`
Expected: first-run wizard appears (fresh `%AppData%\7PaceDesktop` — delete the folder to re-test), refuses to save until valid, then main window opens. Generate a week → 5 rows with favorite work item; check Simulate → Registrera → all rows `Ok`, nothing sent.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: main window UI and app startup wiring"
```

---

### Task 10: Live verification against 7Pace

**Files:** none (manual verification; fix whatever it uncovers)

- [ ] **Step 1: Simulate against a real week**

Run the app with the real org + token. Generate a small range containing a known Swedish holiday (verify skip + 3h shortening render correctly). Check Simulate → Registrera.

- [ ] **Step 2: Submit one real entry**

Uncheck Simulate, reduce the batch to a single row (remove the rest), Registrera. Then open 7Pace Timetracker in ADO and confirm the entry appears on the right work item, date, and duration. If the API rejects or the entry looks wrong (e.g. length interpreted as minutes), fix `PaceApiClient` + its tests to match reality, commit.

- [ ] **Step 3: Submit a real week and verify totals**

- [ ] **Step 4: Commit any fixes**

```bash
git add -A && git commit -m "fix: adjust 7Pace API contract after live verification"
```

---

## Self-Review Notes

- Spec coverage: first-run wizard (Task 9 startup + Task 8), no pre-seeding (WorkItemStore defaults empty, wizard requires one item), holiday fetch/cache/fallback (Task 4), 3h rule + floor flag (Task 2), preview grid with editable hours/work item dropdown/remove/total (Tasks 7+9), Simulate (Task 7), per-row status + retry (Task 7), concurrency ≤4 (Task 7), 401 handling is minimal (per-row error + settings dialog reachable from menu) — spec's "open settings dialog on 401" is simplified to showing the error and menu access; acceptable, noted here as a conscious deviation.
- Payload verification is a blocking first step of Task 6 and re-verified live in Task 10.
