using System.Net.Http.Json;
using System.Text.Json.Serialization;
using PaceDesktop.Core.Models;
using PaceDesktop.Core.Storage;

namespace PaceDesktop.Core.Services;

public sealed record HolidayLookup(
    IReadOnlySet<DateOnly> Dates,
    bool IsIncomplete,
    IReadOnlyDictionary<DateOnly, string>? Names = null);

public sealed class SwedishHolidayService(HttpClient http, SettingsStore store)
{
    private sealed record NagerHoliday(
        [property: JsonPropertyName("date")] DateOnly Date,
        [property: JsonPropertyName("localName")] string LocalName);

    public async Task<HolidayLookup> GetHolidaysAsync(int fromYear, int toYear, CancellationToken ct = default)
    {
        var settings = store.Load();
        var dates = new HashSet<DateOnly>();
        var names = new Dictionary<DateOnly, string>();
        var incomplete = false;
        var cacheChanged = false;

        for (var year = fromYear; year <= toYear; year++)
        {
            if (settings.HolidayCache.TryGetValue(year, out var cached))
            {
                foreach (var h in cached) { dates.Add(h.Date); names[h.Date] = h.Name; }
                continue;
            }
            try
            {
                var fetched = await http.GetFromJsonAsync<List<NagerHoliday>>(
                    $"https://date.nager.at/api/v3/publicholidays/{year}/SE", ct) ?? [];
                var holidays = fetched.Select(n => new Holiday(n.Date, n.LocalName)).ToList();
                settings.HolidayCache[year] = holidays;
                cacheChanged = true;
                foreach (var h in holidays) { dates.Add(h.Date); names[h.Date] = h.Name; }
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
            {
                incomplete = true;
            }
        }

        if (cacheChanged) store.Save(settings);
        return new HolidayLookup(dates, incomplete, names);
    }
}
