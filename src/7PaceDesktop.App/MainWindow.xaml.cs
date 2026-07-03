using System.Windows;
using PaceDesktop.App.ViewModels;
using PaceDesktop.App.Views;
using PaceDesktop.Core.Services;
using PaceDesktop.Core.Storage;

namespace PaceDesktop.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly SettingsStore _settingsStore;
    private readonly WorkItemStore _workItemStore;
    private readonly CredentialStore _credentials;

    public MainWindow(MainViewModel vm, SettingsStore settingsStore,
        WorkItemStore workItemStore, CredentialStore credentials)
    {
        InitializeComponent();
        _vm = vm;
        _settingsStore = settingsStore;
        _workItemStore = workItemStore;
        _credentials = credentials;
        DataContext = vm;
    }

    private void OnOpenSettings(object sender, RoutedEventArgs e)
    {
        var vm = new SetupViewModel(_settingsStore, _workItemStore, _credentials) { RequireWorkItem = false };
        vm.OrganizationName = _settingsStore.Load().OrganizationName;
        new SetupWindow(vm) { Owner = this }.ShowDialog();
    }

    private void OnOpenWorkItems(object sender, RoutedEventArgs e)
    {
        var dialog = new WorkItemsWindow(new WorkItemsViewModel(_workItemStore)) { Owner = this };
        dialog.ShowDialog();
        _vm.WorkItems.Clear();
        foreach (var wi in _workItemStore.Load()) _vm.WorkItems.Add(wi);
    }

    private void OnRemoveRow(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is EntryRowViewModel row)
            _vm.RemoveRow(row);
    }
}
