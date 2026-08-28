namespace PaceDesktop.Server;

/// <summary>Wire types. None of these ever carries the 7Pace token.</summary>
public sealed record ConfigDto(bool Configured, string Organization, double DailyHours, string Theme, bool HasToken);

public sealed record ConfigUpdateDto(string Organization, string? Token, double DailyHours, string Theme);

public sealed record WorkItemDto(int Id, string Name, bool IsFavorite);

public sealed record ExistingWorkLogDto(string Id, double Hours, int WorkItemId, string? WorkItemName, string? Comment);

public sealed record DayDto(
    string Date,
    double Expected,
    double Logged,
    double Remaining,
    string Status,
    bool HitZeroFloor,
    int IsoWeek,
    bool InMonth,
    string? HolidayName,
    IReadOnlyList<ExistingWorkLogDto> Existing);

public sealed record TotalsDto(double Expected, double Logged, double Missing);

public sealed record MonthDto(
    int Year,
    int Month,
    string From,
    string To,
    string LoadState,
    string? Error,
    string? HolidayWarning,
    DateTimeOffset FetchedAt,
    double DailyHours,
    TotalsDto Totals,
    IReadOnlyList<DayDto> Days);

public sealed record FillLineDto(int WorkItemId, double Hours);

public sealed record RegisterRequestDto(
    IReadOnlyList<string> Dates,
    IReadOnlyList<FillLineDto> Lines,
    bool Simulate);

/// <summary>
/// <paramref name="Status"/> is "ok" (every line for the day posted), "failed" (none did), or
/// "partial" (some did and some did not — the day must not be treated as nothing landed).
/// <paramref name="Hours"/> is always the planned hours for the day, in both real and simulate
/// runs, regardless of what actually posted. <paramref name="Error"/> names the failing work
/// item; if more than one line fails, only the first message is kept.
/// </summary>
public sealed record DayResultDto(string Date, double Hours, string Status, string? Error);

public sealed record RegisterResponseDto(
    int PostedEntries,
    int FailedEntries,
    int SkippedDays,
    double TotalHours,
    IReadOnlyList<DayResultDto> Days);
