using System.Windows;
using EveIndustryPlanner.App.ViewModels;

namespace EveIndustryPlanner.App;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var loginDialog = new LoginDialog();
        if (loginDialog.ShowDialog() != true || loginDialog.AuthenticatedAccount is null)
        {
            Shutdown();
            return;
        }

        var loadingWindow = new LoadingWindow();
        loadingWindow.SetStatus("Preparing planner workspace");
        loadingWindow.Show();
        await Task.Delay(75);

        loadingWindow.SetStatus("Loading local SDE and saved planner data");
        await Task.Delay(75);

        await SdeDownloadService.EnsureAvailableAsync(new Progress<string>(loadingWindow.SetStatus));
        await Task.Delay(75);

        var viewModel = new MainWindowViewModel(loginDialog.AuthenticatedAccount);
        await viewModel.InitializeAsync(new Progress<string>(loadingWindow.SetStatus));

        loadingWindow.SetStatus("Opening workspace");
        await Task.Delay(75);

        var mainWindow = new MainWindow(viewModel);
        MainWindow = mainWindow;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        mainWindow.Show();
        loadingWindow.Close();
    }
}
