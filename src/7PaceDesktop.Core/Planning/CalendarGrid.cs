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
