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
