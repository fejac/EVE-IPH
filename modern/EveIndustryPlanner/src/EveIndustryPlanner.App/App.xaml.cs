using System.Windows;
using EveIndustryPlanner.App.ViewModels;

namespace EveIndustryPlanner.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var loginDialog = new LoginDialog();
        if (loginDialog.ShowDialog() != true || loginDialog.AuthenticatedAccount is null)
        {
            Shutdown();
            return;
        }

        var mainWindow = new MainWindow(new MainWindowViewModel(loginDialog.AuthenticatedAccount));
        MainWindow = mainWindow;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        mainWindow.Show();
    }
}
