using System.Text.Json.Serialization;
using PaceDesktop.Core.Models;

namespace PaceDesktop.Core.Storage;

public enum ThemePreference { System, Light, Dark }

public sealed class AppSettings : IJsonOnDeserialized
{
    private double _dailyHours = 8;
    private bool _dailyHoursExplicitlySet = false;
    private double? _lastDailyHoursStashed;

    public string OrganizationName { get; set; } = "";

    /// <summary>The user's daily target, applied to every workday. Persisted, not a remembered input.</summary>
    public double DailyHours
    {
        get => _dailyHours;
        set
        {
            _dailyHours = value;
            _dailyHoursExplicitlySet = true;
        }
    }

    public Dictionary<int, List<Holiday>> HolidayCache { get; set; } = new();

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ThemePreference Theme { get; set; } = ThemePreference.System;

    /// <summary>
    /// Migration shim for settings files written before DailyHours existed. One-way read-time migration:
    /// when deserialized from JSON containing this property, its value is forwarded to DailyHours only if
    /// DailyHours was not explicitly present in the JSON and the value is positive. Never serialized to disk.
    /// This can be removed once no installation can still hold a settings file written before this change.
    /// </summary>
    [JsonPropertyName("LastDailyHours")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? LastDailyHours
    {
        get => null;
        set { _lastDailyHoursStashed = value; }
    }

    void IJsonOnDeserialized.OnDeserialized()
    {
        if (!_dailyHoursExplicitlySet && _lastDailyHoursStashed is { } hours && hours > 0)
        {
            _dailyHours = hours;
        }
    }
}
