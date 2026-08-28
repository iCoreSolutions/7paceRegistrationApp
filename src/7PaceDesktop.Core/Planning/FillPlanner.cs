using PaceDesktop.Core.Models;

namespace PaceDesktop.Core.Planning;

/// <summary>One work item and the hours it takes out of a full day's target.</summary>
public sealed record FillLine(int WorkItemId, double Hours);

/// <summary>How a full day's hours are split across work items.</summary>
public sealed record FillSpec(IReadOnlyList<FillLine> Lines)
{
    /// <summary>The full-day total the lines describe. The UI requires this to equal the daily target.</summary>
    public double Target => Lines.Sum(l => l.Hours);
}

public sealed record FillSummary(int EmptyDays, int PartialDays, int SkippedDays, double TotalHours);

/// <summary>
/// Turns a set of selected dates plus a fill spec into the entries to post. This is the only
/// place the split and rounding rules exist — never reimplement them in the front end.
/// </summary>
public static class FillPlanner
{
    private const double Epsilon = MonthPlan.Epsilon;

    public static IReadOnlyList<TimeEntry> Plan(
        IReadOnlySet<DateOnly> selection, MonthPlan plan, FillSpec spec)
    {
        var entries = new List<TimeEntry>();
        if (spec.Target <= Epsilon) return entries;

        foreach (var date in selection.OrderBy(d => d))
        {
            if (Classify(plan, date, out var day, out var remaining) != DayGate.Fillable) continue;

            var scale = remaining / spec.Target;
            var hours = spec.Lines.Select(l => Math.Round(l.Hours * scale, 2)).ToArray();

            // Put the rounding residual on the largest line so the day sums to exactly `remaining`.
            var residual = remaining - hours.Sum();
            if (Math.Abs(residual) > Epsilon / 2)
            {
                var largest = 0;
                for (var i = 1; i < hours.Length; i++)
                    if (hours[i] > hours[largest]) largest = i;
                hours[largest] = Math.Round(hours[largest] + residual, 2);
            }

            for (var i = 0; i < spec.Lines.Count; i++)
                if (hours[i] > Epsilon)
                    entries.Add(new TimeEntry(date, hours[i], spec.Lines[i].WorkItemId, day.HitZeroFloor));
        }

        return entries;
    }

    public static FillSummary Summarize(
        IReadOnlySet<DateOnly> selection, MonthPlan plan, FillSpec spec)
    {
        // A zero-target spec would post nothing, so the preview must report nothing too.
        if (spec.Target <= Epsilon) return new FillSummary(0, 0, 0, 0);

        int empty = 0, partial = 0, skipped = 0;
        double total = 0;

        foreach (var date in selection)
        {
            var gate = Classify(plan, date, out var day, out var remaining);
            if (gate == DayGate.Ineligible) continue;
            if (gate == DayGate.AlreadySatisfied) { skipped++; continue; }

            if (day.Status == DayStatus.Empty) empty++; else partial++;
            total += remaining;
        }

        return new FillSummary(empty, partial, skipped, Math.Round(total, 2));
    }

    private enum DayGate { Ineligible, AlreadySatisfied, Fillable }

    /// <summary>
    /// The single home for the per-day skip rule shared by <see cref="Plan"/> and
    /// <see cref="Summarize"/>: a date absent from the plan or on a NonWorking/Unknown day is
    /// <see cref="DayGate.Ineligible"/> (never counted at all); a day already at or over target is
    /// <see cref="DayGate.AlreadySatisfied"/> (counted as skipped); anything else is
    /// <see cref="DayGate.Fillable"/>, with <paramref name="day"/> and <paramref name="remaining"/>
    /// populated.
    /// </summary>
    private static DayGate Classify(MonthPlan plan, DateOnly date, out DayPlan day, out double remaining)
    {
        day = default!;
        remaining = 0;

        if (plan.Day(date) is not { } found) return DayGate.Ineligible;
        if (found.Status is DayStatus.NonWorking or DayStatus.Unknown) return DayGate.Ineligible;

        day = found;
        remaining = found.Remaining;
        return remaining <= Epsilon ? DayGate.AlreadySatisfied : DayGate.Fillable;
    }
}
