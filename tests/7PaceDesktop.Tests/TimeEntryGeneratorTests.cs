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
