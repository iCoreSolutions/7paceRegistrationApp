namespace PaceDesktop.Core.Models;

public sealed record TimeEntry(DateOnly Date, double Hours, int WorkItemId, bool HitZeroFloor = false);
