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
            DailyHours = 7.5,
            HolidayCache = { [2026] = [new Holiday(new DateOnly(2026, 6, 24), "Midsommarafton")] }
        };
        store.Save(settings);

        var loaded = new SettingsStore(_dir).Load();
        Assert.Equal("icore", loaded.OrganizationName);
        Assert.Equal(7.5, loaded.DailyHours);
        Assert.Equal("Midsommarafton", loaded.HolidayCache[2026][0].Name);
    }

    [Fact]
    public void SettingsStore_Load_WhenNoFile_ReturnsDefaults()
    {
        var loaded = new SettingsStore(_dir).Load();
        Assert.Equal("", loaded.OrganizationName);
        Assert.Equal(8, loaded.DailyHours);
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

    [Fact]
    public void Settings_DefaultDailyHoursIsEight()
    {
        Assert.Equal(8, new AppSettings().DailyHours);
    }

    [Fact]
    public void Settings_MigratesLastDailyHoursFromAnOlderFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "7pace-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "settings.json"),
            """{"OrganizationName":"icore","LastDailyHours":6,"Theme":"Dark"}""");

        var settings = new SettingsStore(dir).Load();

        Assert.Equal(6, settings.DailyHours);
        Assert.Equal("icore", settings.OrganizationName);
        Assert.Equal(ThemePreference.Dark, settings.Theme);

        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Settings_DoesNotWriteTheLegacyProperty()
    {
        var dir = Path.Combine(Path.GetTempPath(), "7pace-tests", Guid.NewGuid().ToString("N"));
        var store = new SettingsStore(dir);

        store.Save(new AppSettings { OrganizationName = "icore", DailyHours = 7 });
        var json = File.ReadAllText(Path.Combine(dir, "settings.json"));

        Assert.Contains("\"DailyHours\": 7", json);
        Assert.DoesNotContain("LastDailyHours", json);

        Directory.Delete(dir, recursive: true);
    }
}
