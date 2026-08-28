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
