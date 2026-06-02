using System.Windows;
using EveIndustryPlanner.App.ViewModels;

namespace EveIndustryPlanner.App;

public partial class MainWindow : Window
{
    public MainWindow()
        : this(new MainWindowViewModel())
    {
    }

    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
