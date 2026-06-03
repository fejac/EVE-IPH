using System.Windows;

namespace EveIndustryPlanner.App;

public partial class LoadingWindow : Window
{
    public LoadingWindow()
    {
        InitializeComponent();
    }

    public void SetStatus(string status)
    {
        StatusTextBlock.Text = status;
    }
}
