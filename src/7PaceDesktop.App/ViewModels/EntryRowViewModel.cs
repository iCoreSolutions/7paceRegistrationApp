using CommunityToolkit.Mvvm.ComponentModel;
using PaceDesktop.Core.Models;

namespace PaceDesktop.App.ViewModels;

public enum RowStatus { Pending, Sending, Ok, Failed }

public partial class EntryRowViewModel(DateOnly date, double hours, WorkItem workItem, bool hitZeroFloor) : ObservableObject
{
    public DateOnly Date { get; } = date;
    public bool HitZeroFloor { get; } = hitZeroFloor;

    [ObservableProperty] private double _hours = hours;
    [ObservableProperty] private WorkItem _selectedWorkItem = workItem;
    [ObservableProperty] private RowStatus _status = RowStatus.Pending;
    [ObservableProperty] private string? _error;

    // True when this row's date has been split (2+ rows) and the day's rows
    // don't sum to the originally-generated target. Bound to a grid highlight.
    [ObservableProperty] private bool _isDayUnbalanced;

    public TimeEntry ToEntry() => new(Date, Hours, SelectedWorkItem.Id, HitZeroFloor);
}
