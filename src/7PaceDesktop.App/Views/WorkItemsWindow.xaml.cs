using System.Windows;
using PaceDesktop.App.ViewModels;

namespace PaceDesktop.App.Views;

public partial class WorkItemsWindow : Window
{
    public WorkItemsWindow(WorkItemsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }
}
