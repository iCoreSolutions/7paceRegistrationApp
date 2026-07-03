using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PaceDesktop.Core;
using PaceDesktop.Core.Models;
using PaceDesktop.Core.Services;
using PaceDesktop.Core.Storage;

namespace PaceDesktop.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private const int MaxConcurrentSubmits = 4;

    private readonly SwedishHolidayService _holidays;
    private readonly IWorkLogClient _client;
    private readonly SettingsStore _settingsStore;

    public ObservableCollection<WorkItem> WorkItems { get; } = [];
    public ObservableCollection<EntryRowViewModel> Entries { get; } = [];

    [ObservableProperty] private DateTime? _startDate;
    [ObservableProperty] private DateTime? _endDate;
    [ObservableProperty] private double _hoursPerDay;
    [ObservableProperty] private bool _simulate;
    [ObservableProperty] private double _totalHours;
    [ObservableProperty] private string? _warning;

    public MainViewModel(SwedishHolidayService holidays, IWorkLogClient client,
        WorkItemStore workItemStore, SettingsStore settingsStore)
    {
        _holidays = holidays;
        _client = client;
        _settingsStore = settingsStore;
        foreach (var wi in workItemStore.Load()) WorkItems.Add(wi);
        HoursPerDay = settingsStore.Load().LastDailyHours;
    }

    private WorkItem Favorite => WorkItems.FirstOrDefault(w => w.IsFavorite) ?? WorkItems[0];

    [RelayCommand]
    private async Task GenerateAsync()
    {
        if (StartDate is not { } start || EndDate is not { } end || WorkItems.Count == 0) return;
        var from = DateOnly.FromDateTime(start);
        var to = DateOnly.FromDateTime(end);

        var lookup = await _holidays.GetHolidaysAsync(from.Year, to.AddDays(1).Year);
        Warning = lookup.IsIncomplete
            ? "Kunde inte hämta röda dagar — alla dagar behandlas som vanliga arbetsdagar."
            : null;

        Entries.Clear();
        foreach (var e in TimeEntryGenerator.Generate(from, to, HoursPerDay, lookup.Dates, Favorite.Id))
            Entries.Add(new EntryRowViewModel(e.Date, e.Hours, Favorite, e.HitZeroFloor));
        RecalculateTotal();

        var settings = _settingsStore.Load();
        settings.LastDailyHours = HoursPerDay;
        _settingsStore.Save(settings);
    }

    public void RecalculateTotal() => TotalHours = Entries.Sum(r => r.Hours);

    [RelayCommand]
    private async Task RegisterAsync()
    {
        using var gate = new SemaphoreSlim(MaxConcurrentSubmits);
        var tasks = Entries.Where(r => r.Status != RowStatus.Ok).Select(async row =>
        {
            await gate.WaitAsync();
            try { await SubmitRowAsync(row); }
            finally { gate.Release(); }
        });
        await Task.WhenAll(tasks);
    }

    [RelayCommand]
    private Task RetryRowAsync(EntryRowViewModel row) => SubmitRowAsync(row);

    private async Task SubmitRowAsync(EntryRowViewModel row)
    {
        row.Status = RowStatus.Sending;
        row.Error = null;
        if (Simulate)
        {
            row.Status = RowStatus.Ok;
            return;
        }
        try
        {
            await _client.SubmitAsync(row.ToEntry());
            row.Status = RowStatus.Ok;
        }
        catch (Exception ex)
        {
            row.Status = RowStatus.Failed;
            row.Error = ex.Message;
        }
    }

    public void RemoveRow(EntryRowViewModel row)
    {
        Entries.Remove(row);
        RecalculateTotal();
    }
}
