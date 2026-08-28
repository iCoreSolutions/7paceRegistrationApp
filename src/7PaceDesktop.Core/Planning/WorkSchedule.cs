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
