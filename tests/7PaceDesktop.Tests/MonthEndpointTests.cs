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
        server.Settings.Save(new AppSettings
        {
            OrganizationName = "icore",
            DailyHours = dailyHours,
            // No test in this file relies on the internet: seed empty holiday caches for the
            // grid years these tests ever touch so SwedishHolidayService never has to fetch.
            HolidayCache = new Dictionary<int, List<Holiday>> { [2026] = [], [2027] = [] },
        });
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
