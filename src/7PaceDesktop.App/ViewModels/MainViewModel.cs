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

    private const double BalanceEpsilon = 0.001;

    private readonly SwedishHolidayService _holidays;
    private IWorkLogClient _client;
    private readonly SettingsStore _settingsStore;

    // Originally-generated target hours per date, used to flag unbalanced split days.
    private readonly Dictionary<DateOnly, double> _dayTargets = new();

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

    /// <summary>Swap in a client rebuilt from updated org/token so settings changes take effect without an app restart.</summary>
    public void UpdateClient(IWorkLogClient client) => _client = client;

    private WorkItem Favorite => WorkItems.FirstOrDefault(w => w.IsFavorite) ?? WorkItems[0];

    [RelayCommand]
    private async Task GenerateAsync()
    {
        if (StartDate is not { } start || EndDate is not { } end || WorkItems.Count == 0) return;
        var favorite = Favorite;
        var from = DateOnly.FromDateTime(start);
        var to = DateOnly.FromDateTime(end);

        var lookup = await _holidays.GetHolidaysAsync(from.Year, to.AddDays(1).Year);
        Warning = lookup.IsIncomplete
            ? "Kunde inte hämta röda dagar — alla dagar behandlas som vanliga arbetsdagar."
            : null;

        foreach (var existing in Entries) existing.PropertyChanged -= OnRowPropertyChanged;
        Entries.Clear();
        _dayTargets.Clear();
        foreach (var e in TimeEntryGenerator.Generate(from, to, HoursPerDay, lookup.Dates, favorite.Id))
        {
            _dayTargets[e.Date] = e.Hours;
            var row = new EntryRowViewModel(e.Date, e.Hours, favorite, e.HitZeroFloor);
            row.PropertyChanged += OnRowPropertyChanged;
            Entries.Add(row);
        }
        RecalculateTotal();
        RecalculateBalance();

        var settings = _settingsStore.Load();
        settings.LastDailyHours = HoursPerDay;
        _settingsStore.Save(settings);
    }

    /// <summary>Add another work item to a day: inserts a 0h sibling row for the same date.</summary>
    [RelayCommand]
    private void SplitRow(EntryRowViewModel row)
    {
        var newRow = new EntryRowViewModel(row.Date, 0, Favorite, hitZeroFloor: false);
        newRow.PropertyChanged += OnRowPropertyChanged;

        var lastIndexForDate = -1;
        for (var i = 0; i < Entries.Count; i++)
            if (Entries[i].Date == row.Date) lastIndexForDate = i;
        Entries.Insert(lastIndexForDate + 1, newRow);

        RecalculateTotal();
        RecalculateBalance();
    }

    private void OnRowPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(EntryRowViewModel.Hours)) return;
        RecalculateTotal();
        RecalculateBalance();
    }

    public void RecalculateTotal() => TotalHours = Entries.Sum(r => r.Hours);

    // Flag days that have been split (2+ rows) whose hours don't sum to the day's target.
    // Single-row days are never flagged — a lone edited value is treated as intentional.
    public void RecalculateBalance()
    {
        foreach (var group in Entries.GroupBy(r => r.Date))
        {
            var rows = group.ToList();
            var unbalanced = rows.Count >= 2
                && _dayTargets.TryGetValue(group.Key, out var target)
                && Math.Abs(rows.Sum(r => r.Hours) - target) > BalanceEpsilon;
            foreach (var r in rows) r.IsDayUnbalanced = unbalanced;
        }
    }

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
        // These scalar property mutations may run on threadpool continuations after the
        // awaited submit; WPF auto-marshals scalar PropertyChanged for data-bound properties,
        // and Entries is never mutated off the UI thread, so this is intentional —
        // do NOT wrap these in Dispatcher.Invoke (that would risk deadlock).
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
        row.PropertyChanged -= OnRowPropertyChanged;
        Entries.Remove(row);
        RecalculateTotal();
        RecalculateBalance();
    }
}
