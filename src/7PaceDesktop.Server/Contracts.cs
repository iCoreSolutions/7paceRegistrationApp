namespace PaceDesktop.Server;

/// <summary>Wire types. None of these ever carries the 7Pace token.</summary>
public sealed record ConfigDto(bool Configured, string Organization, double DailyHours, string Theme, bool HasToken);

public sealed record ConfigUpdateDto(string Organization, string? Token, double DailyHours, string Theme);

public sealed record WorkItemDto(int Id, string Name, bool IsFavorite);
