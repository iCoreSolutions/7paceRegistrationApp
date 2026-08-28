namespace PaceDesktop.Core.Models;

/// <summary>A worklog that already exists in 7Pace. Read-only: the app never edits or deletes these.</summary>
public sealed record ExistingWorkLog(string Id, DateOnly Date, double Hours, int WorkItemId, string? Comment);
