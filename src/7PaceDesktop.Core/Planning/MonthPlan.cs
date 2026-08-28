using PaceDesktop.Core.Models;

namespace PaceDesktop.Core.Planning;

public enum DayStatus { NonWorking, Empty, Partial, Complete, Over, Unknown }

public sealed record DayPlan(
    DateOnly Date,
    double Expected,
    double Logged,
    IReadOnlyList<ExistingWorkLog> Existing,
    DayStatus Status,
    bool HitZeroFloor)
{
    /// <summary>Hours still needed to reach the day's target. Never negative.</summary>
    public double Remaining => Math.Max(0, Expected - Logged);
}

public sealed record PlanTotals(double Expected, double Logged, double Missing);

/// <summary>
/// The merge of a working schedule with the worklogs actually registered in 7Pace, over the
/// whole displayed grid range. Pure: no I/O, no UI.
/// </summary>
public sealed class MonthPlan
{
    public const double Epsilon = 0.001;

    private readonly Dictionary<DateOnly, DayPlan> _byDate;

    public IReadOnlyList<DayPlan> Days { get; }
    public bool IsUnknown { get; }

    private MonthPlan(List<DayPlan> days, bool isUnknown)
    {
        Days = days;
        IsUnknown = isUnknown;
        _byDate = days.ToDictionary(d => d.Date);
    }

    public DayPlan? Day(DateOnly date) => _byDate.GetValueOrDefault(date);

    public static MonthPlan Build(DateOnly from, DateOnly to, WorkSchedule schedule,
        IReadOnlyList<ExistingWorkLog> logs)
    {
        var grouped = logs.GroupBy(l => l.Date).ToDictionary(g => g.Key, g => (IReadOnlyList<ExistingWorkLog>)g.ToList());
        var days = new List<DayPlan>();

        for (var date = from; date <= to; date = date.AddDays(1))
        {
            var scheduled = schedule.Expected(date);
            IReadOnlyList<ExistingWorkLog> existing = grouped.GetValueOrDefault(date) ?? [];
            var logged = existing.Sum(e => e.Hours);
            days.Add(new DayPlan(date, scheduled.Hours, logged, existing,
                Classify(scheduled.Hours, logged, unknown: false), scheduled.HitZeroFloor));
        }

        return new MonthPlan(days, false);
    }

    /// <summary>
    /// A plan for a period whose worklogs could not be fetched. Working days are Unknown rather
    /// than empty, because treating them as empty would double-log real time.
    /// </summary>
    public static MonthPlan Unknown(DateOnly from, DateOnly to, WorkSchedule schedule)
    {
        var days = new List<DayPlan>();
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            var scheduled = schedule.Expected(date);
            days.Add(new DayPlan(date, scheduled.Hours, 0, [],
                Classify(scheduled.Hours, 0, unknown: true), scheduled.HitZeroFloor));
        }
        return new MonthPlan(days, true);
    }

    private static DayStatus Classify(double expected, double logged, bool unknown)
    {
        // NonWorking wins over Unknown: the schedule is known locally even when the fetch failed.
        if (expected <= Epsilon) return DayStatus.NonWorking;
        if (unknown) return DayStatus.Unknown;
        if (logged <= Epsilon) return DayStatus.Empty;
        if (logged > expected + Epsilon) return DayStatus.Over;
        if (logged >= expected - Epsilon) return DayStatus.Complete;
        return DayStatus.Partial;
    }

    /// <summary>
    /// Totals for one calendar month, excluding the grid's neighbouring-month days. Hours logged
    /// on a NonWorking day (a weekend or holiday) still count towards Logged: 7Pace's own totals
    /// include time logged outside the schedule, so ours must match. Only Unknown days are
    /// excluded from Missing, since a day whose worklogs failed to fetch cannot be reported as
    /// missing without risking a double-log.
    /// </summary>
    public PlanTotals TotalsForMonth(int year, int month)
    {
        var monthDays = Days.Where(d => d.Date.Year == year && d.Date.Month == month).ToList();

        return new PlanTotals(
            monthDays.Sum(d => d.Expected),
            monthDays.Sum(d => d.Logged),
            monthDays.Where(d => d.Status != DayStatus.Unknown).Sum(d => d.Remaining));
    }
}
