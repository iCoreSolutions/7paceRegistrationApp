using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PaceDesktop.Core.Models;
using PaceDesktop.Core.Storage;

namespace PaceDesktop.App.ViewModels;

public partial class WorkItemsViewModel : ObservableObject
{
    private readonly WorkItemStore _store;

    public ObservableCollection<WorkItem> Items { get; }

    [ObservableProperty] private string _newIdText = "";
    [ObservableProperty] private string _newName = "";

    public WorkItemsViewModel(WorkItemStore store)
    {
        _store = store;
        Items = new ObservableCollection<WorkItem>(store.Load());
    }

    private void Persist() => _store.Save(Items);

    [RelayCommand]
    private void Add()
    {
        if (!int.TryParse(NewIdText, out var id) || id <= 0 || string.IsNullOrWhiteSpace(NewName)) return;
        if (Items.Any(i => i.Id == id)) return;
        Items.Add(new WorkItem(id, NewName.Trim(), IsFavorite: Items.Count == 0));
        NewIdText = "";
        NewName = "";
        Persist();
    }

    [RelayCommand]
    private void Remove(WorkItem item)
    {
        if (Items.Count <= 1) return;
        var wasFavorite = item.IsFavorite;
        Items.Remove(item);
        if (wasFavorite && Items.Count > 0)
        {
            var promoted = Items[0] with { IsFavorite = true };
            Items[0] = promoted;
        }
        Persist();
    }

    [RelayCommand]
    private void SetFavorite(WorkItem item)
    {
        for (var i = 0; i < Items.Count; i++)
            Items[i] = Items[i] with { IsFavorite = Items[i].Id == item.Id };
        Persist();
    }
}
