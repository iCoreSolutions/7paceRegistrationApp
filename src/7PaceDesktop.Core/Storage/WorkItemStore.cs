using System.Text.Json;
using PaceDesktop.Core.Models;

namespace PaceDesktop.Core.Storage;

public sealed class WorkItemStore(string baseDir)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private string FilePath => Path.Combine(baseDir, "workitems.json");

    public IReadOnlyList<WorkItem> Load()
    {
        if (!File.Exists(FilePath)) return [];
        return JsonSerializer.Deserialize<List<WorkItem>>(File.ReadAllText(FilePath), Options) ?? [];
    }

    public void Save(IEnumerable<WorkItem> items)
    {
        Directory.CreateDirectory(baseDir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(items.ToList(), Options));
    }
}
