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
            if (!TryRemaining(plan, date, out var day, out var remaining)) continue;

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
        int empty = 0, partial = 0, skipped = 0;
        double total = 0;

        foreach (var date in selection)
        {
            if (plan.Day(date) is not { } day) continue;
            if (day.Status is DayStatus.NonWorking or DayStatus.Unknown) continue;

            if (day.Remaining <= Epsilon) { skipped++; continue; }

            if (day.Status == DayStatus.Empty) empty++; else partial++;
            total += day.Remaining;
        }

        return new FillSummary(empty, partial, skipped, Math.Round(total, 2));
    }

    private static bool TryRemaining(MonthPlan plan, DateOnly date, out DayPlan day, out double remaining)
    {
        day = default!;
        remaining = 0;

        if (plan.Day(date) is not { } found) return false;
        if (found.Status is DayStatus.NonWorking or DayStatus.Unknown) return false;
        if (found.Remaining <= Epsilon) return false;

        day = found;
        remaining = found.Remaining;
        return true;
    }
}
