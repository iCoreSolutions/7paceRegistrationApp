using CommunityToolkit.Mvvm.ComponentModel;
using PaceDesktop.Core.Models;
using PaceDesktop.Core.Services;
using PaceDesktop.Core.Storage;

namespace PaceDesktop.App.ViewModels;

public partial class SetupViewModel(SettingsStore settingsStore, WorkItemStore workItemStore, CredentialStore credentials)
    : ObservableObject
{
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanSave))] private string _organizationName = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanSave))] private string _token = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanSave))] private string _workItemIdText = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanSave))] private string _workItemName = "";

    /// <summary>False when reused as the settings dialog (work items already exist).</summary>
    public bool RequireWorkItem { get; init; } = true;

    public bool CanSave =>
        !string.IsNullOrWhiteSpace(OrganizationName) &&
        !string.IsNullOrWhiteSpace(Token) &&
        (!RequireWorkItem ||
         (int.TryParse(WorkItemIdText, out var id) && id > 0 && !string.IsNullOrWhiteSpace(WorkItemName)));

    public bool TrySave()
    {
        if (!CanSave) return false;

        var settings = settingsStore.Load();
        settings.OrganizationName = OrganizationName.Trim();
        settingsStore.Save(settings);
        credentials.SaveToken(settings.OrganizationName, Token.Trim());

        if (RequireWorkItem)
            workItemStore.Save([new WorkItem(int.Parse(WorkItemIdText), WorkItemName.Trim(), IsFavorite: true)]);
        return true;
    }
}
