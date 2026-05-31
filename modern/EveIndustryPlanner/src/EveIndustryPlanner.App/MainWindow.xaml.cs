using System.Windows;
using EveIndustryPlanner.App.ViewModels;

namespace EveIndustryPlanner.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel();
    }
}
