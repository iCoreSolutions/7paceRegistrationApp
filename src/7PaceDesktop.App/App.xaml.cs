using System.Net.Http;
using System.Windows;
using PaceDesktop.App.ViewModels;
using PaceDesktop.App.Views;
using PaceDesktop.Core.Services;
using PaceDesktop.Core.Storage;

namespace PaceDesktop.App;

public partial class App : Application
{
    private static readonly HttpClient Http = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var baseDir = AppPaths.DefaultBaseDir;
        var settingsStore = new SettingsStore(baseDir);
        var workItemStore = new WorkItemStore(baseDir);
        var credentials = new CredentialStore();

        var settings = settingsStore.Load();
        var configured = !string.IsNullOrWhiteSpace(settings.OrganizationName)
                         && credentials.LoadToken(settings.OrganizationName) is not null
                         && workItemStore.Load().Count > 0;

        if (!configured)
        {
            var setup = new SetupWindow(new SetupViewModel(settingsStore, workItemStore, credentials));
            if (setup.ShowDialog() != true) { Shutdown(); return; }
            settings = settingsStore.Load();
        }

        var token = credentials.LoadToken(settings.OrganizationName)!;
        var client = new PaceApiClient(Http, settings.OrganizationName, token);
        var holidays = new SwedishHolidayService(Http, settingsStore);
        var vm = new MainViewModel(holidays, client, workItemStore, settingsStore);

        ShutdownMode = ShutdownMode.OnMainWindowClose;
        MainWindow = new MainWindow(vm, settingsStore, workItemStore, credentials);
        MainWindow.Show();
    }
}
