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

            var dto = await BuildMonth(year, month, settingsStore, workItemStore, holidayService, clients, time, ct);
            return Results.Ok(dto);
        });
    }

    private static async Task<MonthDto> BuildMonth(
        int year,
        int month,
        SettingsStore settingsStore,
        WorkItemStore workItemStore,
        SwedishHolidayService holidayService,
        IPaceClientFactory clients,
        TimeProvider time,
        CancellationToken ct)
    {
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

        return new MonthDto(
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
            Days: days);
    }

    /// <summary>DayStatus as a camel-case wire string, e.g. NonWorking -> "nonWorking".</summary>
    private static string StatusName(DayStatus status) =>
        char.ToLowerInvariant(status.ToString()[0]) + status.ToString()[1..];
}
