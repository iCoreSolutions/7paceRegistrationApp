using System.Text.Json.Serialization;
using PaceDesktop.Core.Models;

namespace PaceDesktop.Core.Storage;

public enum ThemePreference { System, Light, Dark }

public sealed class AppSettings
{
    public string OrganizationName { get; set; } = "";

    /// <summary>The user's daily target, applied to every workday. Persisted, not a remembered input.</summary>
    public double DailyHours { get; set; } = 8;

    public Dictionary<int, List<Holiday>> HolidayCache { get; set; } = new();

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ThemePreference Theme { get; set; } = ThemePreference.System;

    /// <summary>
    /// Migration shim for settings files written before DailyHours existed. Read-only: the getter
    /// returns null so the property is never written back, and the setter forwards to DailyHours.
    /// </summary>
    [JsonPropertyName("LastDailyHours")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? LastDailyHours
    {
        get => null;
        set { if (value is { } hours && hours > 0) DailyHours = hours; }
    }
}
