namespace APPvista.Desktop;

public partial class HistoryPageView : System.Windows.Controls.UserControl
{
    public HistoryPageView()
    {
        InitializeComponent();
    }

    public void ResetScrollPosition()
    {
        HistoryContentScrollViewer.ScrollToHome();
        HistoryContentScrollViewer.UpdateLayout();
    }
}
