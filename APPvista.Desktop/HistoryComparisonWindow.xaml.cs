using System.Windows;

namespace APPvista.Desktop;

public partial class HistoryComparisonWindow : Window
{
    public HistoryComparisonWindow(object viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
