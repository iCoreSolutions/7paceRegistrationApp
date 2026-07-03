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

    public TimeEntry ToEntry() => new(Date, Hours, SelectedWorkItem.Id, HitZeroFloor);
}
