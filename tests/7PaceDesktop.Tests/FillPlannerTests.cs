using PaceDesktop.Core.Models;
using PaceDesktop.Core.Planning;

namespace PaceDesktop.Tests;

public class FillPlannerTests
{
    private const int Sprint = 12345;
    private const int Support = 12401;

    private static MonthPlan JunePlan(params ExistingWorkLog[] logs)
    {
        var schedule = new WorkSchedule(8, new HashSet<DateOnly> { new(2026, 6, 19) });
        var (from, to) = CalendarGrid.RangeFor(2026, 6);
        return MonthPlan.Build(from, to, schedule, logs);
    }

    private static ExistingWorkLog Log(int day, double hours) =>
        new($"w{day}", new DateOnly(2026, 6, day), hours, Sprint, null);

    private static IReadOnlySet<DateOnly> Days(params int[] days) =>
        new HashSet<DateOnly>(days.Select(d => new DateOnly(2026, 6, d)));

    private static FillSpec Single(double hours = 8) => new([new FillLine(Sprint, hours)]);

    [Fact]
    public void EmptyDay_IsFilledToTheTarget()
    {
        var entries = FillPlanner.Plan(Days(22), JunePlan(), Single());

        var entry = Assert.Single(entries);
        Assert.Equal(new DateOnly(2026, 6, 22), entry.Date);
        Assert.Equal(8, entry.Hours);
        Assert.Equal(Sprint, entry.WorkItemId);
    }

    [Fact]
    public void PartialDay_IsToppedUpByTheShortfallOnly()
    {
        var entries = FillPlanner.Plan(Days(24), JunePlan(Log(24, 3)), Single());

        Assert.Equal(5, Assert.Single(entries).Hours);
    }

    [Fact]
    public void CompleteAndOverDays_ProduceNothing()
    {
        var plan = JunePlan(Log(25, 8), Log(17, 9));

        Assert.Empty(FillPlanner.Plan(Days(25, 17), plan, Single()));
    }

    [Fact]
    public void NonWorkingAndUnknownDays_ProduceNothing()
    {
        // 6 June is a Saturday, 19 June is a holiday.
        Assert.Empty(FillPlanner.Plan(Days(6, 19), JunePlan(), Single()));

        var schedule = new WorkSchedule(8, new HashSet<DateOnly>());
        var (from, to) = CalendarGrid.RangeFor(2026, 6);
        Assert.Empty(FillPlanner.Plan(Days(22), MonthPlan.Unknown(from, to, schedule), Single()));
    }

    [Fact]
    public void PreHolidayDay_IsFilledToItsShortenedTarget()
    {
        // 18 June expects 5h because 19 June is a holiday; the spec target is still 8.
        var entries = FillPlanner.Plan(Days(18), JunePlan(), Single());

        var entry = Assert.Single(entries);
        Assert.Equal(5, entry.Hours);
        Assert.False(entry.HitZeroFloor);
    }

    [Fact]
    public void SplitLines_AreEmittedPerWorkItemOnAFullDay()
    {
        var spec = new FillSpec([new FillLine(Sprint, 6), new FillLine(Support, 2)]);

        var entries = FillPlanner.Plan(Days(22), JunePlan(), spec);

        Assert.Equal(2, entries.Count);
        Assert.Equal(6, entries.Single(e => e.WorkItemId == Sprint).Hours);
        Assert.Equal(2, entries.Single(e => e.WorkItemId == Support).Hours);
    }

    [Fact]
    public void SplitLines_ScaleProportionallyOnAPartialDay()
    {
        // 3h already logged, 5h remaining, split 6/2 -> 3.75 / 1.25.
        var spec = new FillSpec([new FillLine(Sprint, 6), new FillLine(Support, 2)]);

        var entries = FillPlanner.Plan(Days(24), JunePlan(Log(24, 3)), spec);

        Assert.Equal(3.75, entries.Single(e => e.WorkItemId == Sprint).Hours);
        Assert.Equal(1.25, entries.Single(e => e.WorkItemId == Support).Hours);
        Assert.Equal(5, entries.Sum(e => e.Hours));
    }

    [Fact]
    public void RoundingResidual_LandsOnTheLargestLineSoTheDaySumsExactly()
    {
        // 1h already logged leaves 7h; a three-way even split does not divide cleanly.
        var spec = new FillSpec([
            new FillLine(Sprint, 3),
            new FillLine(Support, 3),
            new FillLine(999, 3)
        ]);

        var entries = FillPlanner.Plan(Days(24), JunePlan(Log(24, 1)), spec);

        Assert.Equal(3, entries.Count);
        Assert.Equal(7, Math.Round(entries.Sum(e => e.Hours), 10));
        Assert.All(entries, e => Assert.True(e.Hours > 0));
    }

    [Fact]
    public void ZeroTargetSpec_ProducesNothing()
    {
        var spec = new FillSpec([new FillLine(Sprint, 0)]);

        Assert.Empty(FillPlanner.Plan(Days(22), JunePlan(), spec));
    }

    [Fact]
    public void ManyDays_AreAllPlanned_OrderedByDate()
    {
        var entries = FillPlanner.Plan(Days(26, 22, 23), JunePlan(), Single());

        Assert.Equal(3, entries.Count);
        Assert.Equal([22, 23, 26], entries.Select(e => e.Date.Day));
        Assert.Equal(24, entries.Sum(e => e.Hours));
    }

    [Fact]
    public void Summarize_CountsEachDayKindAndTotalsTheHours()
    {
        // 22, 23, 26 empty; 24 partial with 3h logged; 25 already complete.
        var plan = JunePlan(Log(24, 3), Log(25, 8));

        var summary = FillPlanner.Summarize(Days(22, 23, 24, 25, 26), plan, Single());

        Assert.Equal(3, summary.EmptyDays);
        Assert.Equal(1, summary.PartialDays);
        Assert.Equal(1, summary.SkippedDays);
        Assert.Equal(29, summary.TotalHours);
    }

    [Fact]
    public void Summarize_TotalMatchesWhatPlanWouldPost()
    {
        var plan = JunePlan(Log(24, 3), Log(25, 8));
        var spec = new FillSpec([new FillLine(Sprint, 6), new FillLine(Support, 2)]);
        var selection = Days(22, 23, 24, 25, 26);

        var summary = FillPlanner.Summarize(selection, plan, spec);
        var planned = FillPlanner.Plan(selection, plan, spec);

        Assert.Equal(summary.TotalHours, Math.Round(planned.Sum(e => e.Hours), 2));
    }

    [Fact]
    public void EmptySelection_ProducesNothing()
    {
        var plan = JunePlan();

        Assert.Empty(FillPlanner.Plan(Days(), plan, Single()));
        Assert.Equal(new FillSummary(0, 0, 0, 0), FillPlanner.Summarize(Days(), plan, Single()));
    }

    [Fact]
    public void DateOutsideThePlanRange_IsIgnored()
    {
        // 1 August 2026 is outside the grid MonthPlan.Build was given (June's grid ends 5 July).
        var selection = new HashSet<DateOnly> { new(2026, 8, 1) };
        var plan = JunePlan();

        Assert.Empty(FillPlanner.Plan(selection, plan, Single()));
        Assert.Equal(new FillSummary(0, 0, 0, 0), FillPlanner.Summarize(selection, plan, Single()));
    }
}
