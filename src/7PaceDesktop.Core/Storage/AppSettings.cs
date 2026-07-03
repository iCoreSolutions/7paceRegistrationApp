using PaceDesktop.Core.Models;

namespace PaceDesktop.Core.Storage;

public sealed class AppSettings
{
    public string OrganizationName { get; set; } = "";
    public double LastDailyHours { get; set; } = 8;
    public Dictionary<int, List<Holiday>> HolidayCache { get; set; } = new();
}
