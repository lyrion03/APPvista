using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using APPvista.Desktop.ViewModels;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace APPvista.Desktop;

public partial class HistoryComparisonWindow : Window
{
    private const double ParallelChartHoverDistance = 16d;
    private bool _isSelectingApplications;

    public HistoryComparisonWindow(object viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void ParallelChartSeriesElement_OnMouseEnter(object sender, WpfMouseEventArgs e)
    {
        if (DataContext is not HistoryComparisonViewModel viewModel)
        {
            return;
        }

        if ((sender as FrameworkElement)?.DataContext is HistoryComparisonViewModel.HistoryComparisonParallelSeriesViewModel series)
        {
            viewModel.HighlightParallelSeries(series.DisplayName);
        }
    }

    private void ParallelChartSeriesElement_OnMouseLeave(object sender, WpfMouseEventArgs e)
    {
        if (DataContext is HistoryComparisonViewModel viewModel)
        {
            viewModel.ClearParallelSeriesHighlight();
        }
    }

    private void ParallelChartSurface_OnMouseMove(object sender, WpfMouseEventArgs e)
    {
        if (DataContext is not HistoryComparisonViewModel viewModel || sender is not FrameworkElement element)
        {
            return;
        }

        viewModel.HighlightNearestParallelSeries(e.GetPosition(element), ParallelChartHoverDistance);
    }

    private void ParallelChartSurface_OnMouseLeave(object sender, WpfMouseEventArgs e)
    {
        if (DataContext is HistoryComparisonViewModel viewModel)
        {
            viewModel.ClearParallelSeriesHighlight();
        }
    }

    private void ParallelChartScrollViewer_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            return;
        }

        e.Handled = true;
        if (e.Delta > 0)
        {
            ComparisonContentScrollViewer.LineUp();
            return;
        }

        ComparisonContentScrollViewer.LineDown();
    }

    private void ApplicationSelectorToggle_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ToggleButton toggleButton ||
            toggleButton.DataContext is not HistoryComparisonViewModel.HistoryComparisonSelectableApplicationViewModel application)
        {
            return;
        }

        _isSelectingApplications = true;
        application.IsSelected = true;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnPreviewMouseMove(WpfMouseEventArgs e)
    {
        base.OnPreviewMouseMove(e);

        if (!_isSelectingApplications)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            EndApplicationSelectionDrag();
            return;
        }

        var element = InputHitTest(e.GetPosition(this)) as DependencyObject;
        var toggleButton = FindAncestor<ToggleButton>(element);
        if (toggleButton?.DataContext is HistoryComparisonViewModel.HistoryComparisonSelectableApplicationViewModel application)
        {
            application.IsSelected = true;
        }
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonUp(e);
        EndApplicationSelectionDrag();
    }

    private void EndApplicationSelectionDrag()
    {
        if (!_isSelectingApplications)
        {
            return;
        }

        _isSelectingApplications = false;
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }
    }

    private static T? FindAncestor<T>(DependencyObject? dependencyObject) where T : DependencyObject
    {
        while (dependencyObject is not null)
        {
            if (dependencyObject is T target)
            {
                return target;
            }

            dependencyObject = VisualTreeHelper.GetParent(dependencyObject);
        }

        return null;
    }
}
