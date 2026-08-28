using PaceDesktop.Core.Models;
using PaceDesktop.Core.Planning;

namespace PaceDesktop.Tests;

public class MonthPlanTests
{
    private static readonly WorkSchedule Plain = new(8, new HashSet<DateOnly>());

    private static ExistingWorkLog Log(int day, double hours, int workItemId = 42) =>
        new($"w{day}-{hours}", new DateOnly(2026, 6, day), hours, workItemId, null);

    [Theory]
    // June 2026 starts on a Monday and has 30 days, so the grid is exactly 5 weeks.
    [InlineData(2026, 6, "2026-06-01", "2026-07-05")]
    // August 2026 starts on a Saturday, so the grid needs 6 weeks and starts in July.
    [InlineData(2026, 8, "2026-07-27", "2026-09-06")]
    // February 2027 starts on a Monday with 28 days: exactly 4 weeks.
    [InlineData(2027, 2, "2027-02-01", "2027-02-28")]
    public void RangeFor_CoversWholeWeeksMondayFirst(int year, int month, string from, string to)
    {
        var (actualFrom, actualTo) = CalendarGrid.RangeFor(year, month);

        Assert.Equal(DateOnly.Parse(from), actualFrom);
        Assert.Equal(DateOnly.Parse(to), actualTo);
        Assert.Equal(DayOfWeek.Monday, actualFrom.DayOfWeek);
        Assert.Equal(DayOfWeek.Sunday, actualTo.DayOfWeek);
        Assert.Equal(0, (actualTo.DayNumber - actualFrom.DayNumber + 1) % 7);
    }

    [Fact]
    public void IsoWeek_MatchesTheIsoCalendar()
    {
        Assert.Equal(23, CalendarGrid.IsoWeek(new DateOnly(2026, 6, 1)));
        Assert.Equal(27, CalendarGrid.IsoWeek(new DateOnly(2026, 6, 29)));
    }

    [Fact]
    public void Build_ClassifiesEveryStatus()
    {
        var holidays = new HashSet<DateOnly> { new(2026, 6, 19) };
        var schedule = new WorkSchedule(8, holidays);
        var (from, to) = CalendarGrid.RangeFor(2026, 6);

        var plan = MonthPlan.Build(from, to, schedule,
        [
            Log(3, 6),            // partial
            Log(4, 8),            // complete
            Log(17, 5), Log(17, 4) // 9h on an 8h day -> over
        ]);

        Assert.Equal(DayStatus.Empty, plan.Day(new DateOnly(2026, 6, 5))!.Status);
        Assert.Equal(DayStatus.Partial, plan.Day(new DateOnly(2026, 6, 3))!.Status);
        Assert.Equal(DayStatus.Complete, plan.Day(new DateOnly(2026, 6, 4))!.Status);
        Assert.Equal(DayStatus.Over, plan.Day(new DateOnly(2026, 6, 17))!.Status);
        Assert.Equal(DayStatus.NonWorking, plan.Day(new DateOnly(2026, 6, 6))!.Status);   // Saturday
        Assert.Equal(DayStatus.NonWorking, plan.Day(new DateOnly(2026, 6, 19))!.Status);  // holiday
        Assert.False(plan.IsUnknown);
    }

    [Fact]
    public void Build_SumsAndKeepsTheDaysExistingWorklogs()
    {
        var (from, to) = CalendarGrid.RangeFor(2026, 6);

        var plan = MonthPlan.Build(from, to, Plain, [Log(17, 5, 100), Log(17, 4, 200)]);

        var day = plan.Day(new DateOnly(2026, 6, 17))!;
        Assert.Equal(9, day.Logged);
        Assert.Equal(8, day.Expected);
        Assert.Equal(0, day.Remaining);                       // never negative
        Assert.Equal([100, 200], day.Existing.Select(e => e.WorkItemId));
    }

    [Fact]
    public void Build_PreHolidayDayCarriesItsShortenedTargetAndRemaining()
    {
        var schedule = new WorkSchedule(8, new HashSet<DateOnly> { new(2026, 6, 19) });
        var (from, to) = CalendarGrid.RangeFor(2026, 6);

        var plan = MonthPlan.Build(from, to, schedule, [Log(18, 2)]);

        var day = plan.Day(new DateOnly(2026, 6, 18))!;
        Assert.Equal(5, day.Expected);
        Assert.Equal(3, day.Remaining);
        Assert.Equal(DayStatus.Partial, day.Status);
    }

    [Fact]
    public void Unknown_MarksWorkdaysUnknownButKeepsWeekendsNonWorking()
    {
        var (from, to) = CalendarGrid.RangeFor(2026, 6);

        var plan = MonthPlan.Unknown(from, to, Plain);

        Assert.True(plan.IsUnknown);
        Assert.Equal(DayStatus.Unknown, plan.Day(new DateOnly(2026, 6, 1))!.Status);
        // The schedule is known locally, so a weekend stays a weekend during a failed fetch.
        Assert.Equal(DayStatus.NonWorking, plan.Day(new DateOnly(2026, 6, 6))!.Status);
    }

    [Fact]
    public void TotalsForMonth_IgnoresAdjacentMonthDaysAndNonWorkingDays()
    {
        var schedule = new WorkSchedule(8, new HashSet<DateOnly>());
        var (from, to) = CalendarGrid.RangeFor(2026, 8);   // grid starts 2026-07-27
        var logs = new List<ExistingWorkLog>
        {
            new("july", new DateOnly(2026, 7, 28), 8, 1, null),   // outside August, must not count
            new("aug", new DateOnly(2026, 8, 3), 6, 1, null)
        };

        var totals = MonthPlan.Build(from, to, schedule, logs).TotalsForMonth(2026, 8);

        // August 2026 has 21 weekdays and no holidays: 21 * 8 = 168 expected.
        Assert.Equal(168, totals.Expected);
        Assert.Equal(6, totals.Logged);
        Assert.Equal(162, totals.Missing);
    }

    [Fact]
    public void TotalsForMonth_ReportsNoMissingHoursWhenTheMonthIsUnknown()
    {
        var (from, to) = CalendarGrid.RangeFor(2026, 6);

        var totals = MonthPlan.Unknown(from, to, Plain).TotalsForMonth(2026, 6);

        // June 2026 has 22 weekdays (starts Monday, 30 days: 4 full 5-day weeks + Mon/Tue).
        Assert.Equal(176, totals.Expected);
        Assert.Equal(0, totals.Logged);
        Assert.Equal(0, totals.Missing);   // unknown days cannot be reported as missing
    }
}
