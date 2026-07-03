using System.Windows;
using PaceDesktop.App.ViewModels;

namespace PaceDesktop.App.Views;

public partial class SetupWindow : Window
{
    private readonly SetupViewModel _vm;

    public SetupWindow(SetupViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    public Visibility WorkItemSectionVisibility =>
        _vm.RequireWorkItem ? Visibility.Visible : Visibility.Collapsed;

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (_vm.TrySave()) { DialogResult = true; Close(); }
    }
}
