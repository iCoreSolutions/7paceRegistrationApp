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

            // Per-entry outcome, indexed the same as `entries`. Each index is written by exactly
            // one task below, so plain array writes need no locking. Null means that entry either
            // posted successfully or was never attempted (simulate mode).
            var entryErrors = new string?[entries.Count];
            var posted = 0;
            var failed = 0;

            if (!body.Simulate)
            {
                var client = clients.CreateClient();
                using var gate = new SemaphoreSlim(MaxConcurrentSubmits);
                await Task.WhenAll(entries.Select(async (entry, index) =>
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
                        // Name the failing work item so a "partial" day's Error is attributable
                        // even though the DTO only carries one message per day.
                        entryErrors[index] = $"Arbetsobjekt {entry.WorkItemId}: {ex.Message}";
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

            // Status reflects the actual per-entry outcomes for the day, not just whether any
            // entry failed: a day with multiple work-item lines can have some post and some not,
            // and reporting that as flatly "failed" would contradict the entry-level
            // PostedEntries/FailedEntries counts above. Hours is always the planned total for the
            // day, in both real and simulate runs, regardless of what actually posted.
            var days = entries
                .Select((entry, index) => (entry, error: entryErrors[index]))
                .GroupBy(x => x.entry.Date)
                .OrderBy(g => g.Key)
                .Select(g =>
                {
                    var dayFailed = g.Count(x => x.error is not null);
                    var status = dayFailed == 0 ? "ok" : dayFailed == g.Count() ? "failed" : "partial";
                    return new DayResultDto(
                        Date: g.Key.ToString("yyyy-MM-dd"),
                        Hours: Math.Round(g.Sum(x => x.entry.Hours), 2),
                        Status: status,
                        Error: g.Select(x => x.error).FirstOrDefault(e => e is not null));
                })
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
