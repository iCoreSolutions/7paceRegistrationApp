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
    public async Task Register_PartiallyFailingDay_ReportsPartialWithThePlannedHoursAndTheFailingWorkItem()
    {
        using var server = Configured();
        server.Pace.SubmitThrows = entry => entry.WorkItemId == Support
            ? new PaceApiException(500, "7Pace API error 500: boom")
            : null;

        var result = await (await server.Client.PostAsJsonAsync("/api/register",
            Request([22], lines: [new FillLineDto(Sprint, 6), new FillLineDto(Support, 2)])))
            .Content.ReadFromJsonAsync<RegisterResponseDto>();

        // One line for the day posted, the other did not: the day itself is neither fully "ok"
        // nor fully "failed", and Hours still reports the full planned total either way.
        Assert.Equal(1, result!.PostedEntries);
        Assert.Equal(1, result.FailedEntries);
        var day = Assert.Single(result.Days);
        Assert.Equal("partial", day.Status);
        Assert.Equal(8, day.Hours);
        Assert.Contains(Support.ToString(), day.Error);
        Assert.Contains("500", day.Error);
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
    public async Task Register_SpanRejectionMessageDescribesTheDayCountItActuallyChecks()
    {
        // The guard is a raw 62-day count, which can legitimately span THREE calendar months
        // (20 January to 22 March is 62 days), so the message must not claim "two months" -
        // that describes a check that isn't the one actually being enforced.
        using var server = Configured();

        var wide = await server.Client.PostAsJsonAsync("/api/register",
            new RegisterRequestDto(["2026-01-20", "2026-03-23"], [new FillLineDto(Sprint, 8)], false));
        var body = await wide.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, wide.StatusCode);
        Assert.DoesNotContain("två månader", body);
        Assert.Contains("62", body);
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
