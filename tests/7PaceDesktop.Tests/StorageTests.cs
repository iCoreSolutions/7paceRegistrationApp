using PaceDesktop.Core.Models;
using PaceDesktop.Core.Storage;

namespace PaceDesktop.Tests;

public class StorageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "7PaceDesktopTests", Guid.NewGuid().ToString("N"));

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void SettingsStore_RoundTrips()
    {
        var store = new SettingsStore(_dir);
        var settings = new AppSettings
        {
            OrganizationName = "icore",
            LastDailyHours = 7.5,
            HolidayCache = { [2026] = [new Holiday(new DateOnly(2026, 6, 24), "Midsommarafton")] }
        };
        store.Save(settings);

        var loaded = new SettingsStore(_dir).Load();
        Assert.Equal("icore", loaded.OrganizationName);
        Assert.Equal(7.5, loaded.LastDailyHours);
        Assert.Equal("Midsommarafton", loaded.HolidayCache[2026][0].Name);
    }

    [Fact]
    public void SettingsStore_Load_WhenNoFile_ReturnsDefaults()
    {
        var loaded = new SettingsStore(_dir).Load();
        Assert.Equal("", loaded.OrganizationName);
        Assert.Equal(8, loaded.LastDailyHours);
        Assert.Empty(loaded.HolidayCache);
    }

    [Fact]
    public void WorkItemStore_RoundTrips_AndDefaultsEmpty()
    {
        var store = new WorkItemStore(_dir);
        Assert.Empty(store.Load());

        store.Save([new WorkItem(79023, "Product Development", true)]);
        var loaded = new WorkItemStore(_dir).Load();
        var item = Assert.Single(loaded);
        Assert.Equal(79023, item.Id);
        Assert.True(item.IsFavorite);
    }
}
