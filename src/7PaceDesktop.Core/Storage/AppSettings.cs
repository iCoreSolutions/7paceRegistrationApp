using System.Text.Json.Serialization;
using PaceDesktop.Core.Models;

namespace PaceDesktop.Core.Storage;

public enum ThemePreference { System, Light, Dark }

public sealed class AppSettings
{
    public string OrganizationName { get; set; } = "";
    public double LastDailyHours { get; set; } = 8;
    public Dictionary<int, List<Holiday>> HolidayCache { get; set; } = new();

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ThemePreference Theme { get; set; } = ThemePreference.System;
}
