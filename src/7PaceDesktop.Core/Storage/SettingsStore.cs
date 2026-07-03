using System.Text.Json;

namespace PaceDesktop.Core.Storage;

public static class AppPaths
{
    public static string DefaultBaseDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "7PaceDesktop");
}

public sealed class SettingsStore(string baseDir)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private string FilePath => Path.Combine(baseDir, "settings.json");

    public AppSettings Load()
    {
        if (!File.Exists(FilePath)) return new AppSettings();
        return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options) ?? new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(baseDir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, Options));
    }
}
