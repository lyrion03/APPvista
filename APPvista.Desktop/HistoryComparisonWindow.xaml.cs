using System.Windows;
using System.Windows.Controls;
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
    private bool _applicationSelectionTarget;
    private HistoryComparisonViewModel.HistoryComparisonMetric? _draggingParallelAxisMetric;

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
        if (_draggingParallelAxisMetric is not null)
        {
            return;
        }

        if (DataContext is HistoryComparisonViewModel viewModel)
        {
            viewModel.ClearParallelSeriesHighlight();
        }
    }

    private void ParallelChartAxisHandle_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not HistoryComparisonViewModel.HistoryComparisonParallelAxisViewModel axis)
        {
            return;
        }

        _draggingParallelAxisMetric = axis.Metric;
        CaptureMouse();
        e.Handled = true;
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
            WindowScrollViewer.LineUp();
            return;
        }

        WindowScrollViewer.LineDown();
    }

    private void ParallelChartScrollViewer_OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateParallelChartViewportWidth(sender as ScrollViewer);
    }

    private void ParallelChartScrollViewer_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateParallelChartViewportWidth(sender as ScrollViewer);
    }

    private void ParallelChartScrollViewer_OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (Math.Abs(e.ViewportWidthChange) < double.Epsilon)
        {
            return;
        }

        UpdateParallelChartViewportWidth(sender as ScrollViewer);
    }

    private void ApplicationSelectorToggle_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ToggleButton toggleButton ||
            toggleButton.DataContext is not HistoryComparisonViewModel.HistoryComparisonSelectableApplicationViewModel application)
        {
            return;
        }

        _isSelectingApplications = true;
        _applicationSelectionTarget = !application.IsSelected;
        application.IsSelected = _applicationSelectionTarget;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnPreviewMouseMove(WpfMouseEventArgs e)
    {
        base.OnPreviewMouseMove(e);

        if (_draggingParallelAxisMetric is HistoryComparisonViewModel.HistoryComparisonMetric metric)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                EndParallelAxisDrag();
                return;
            }

            if (DataContext is HistoryComparisonViewModel viewModel)
            {
                var position = e.GetPosition(ParallelChartSurface);
                viewModel.MoveParallelChartAxis(metric, position.X);
            }

            return;
        }

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
            application.IsSelected = _applicationSelectionTarget;
        }
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonUp(e);
        EndParallelAxisDrag();
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

    private void EndParallelAxisDrag()
    {
        if (_draggingParallelAxisMetric is null)
        {
            return;
        }

        _draggingParallelAxisMetric = null;
        if (!_isSelectingApplications && IsMouseCaptured)
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

    private void UpdateParallelChartViewportWidth(ScrollViewer? scrollViewer)
    {
        if (scrollViewer is null || DataContext is not HistoryComparisonViewModel viewModel)
        {
            return;
        }

        var viewportWidth = scrollViewer.ViewportWidth;
        if (double.IsNaN(viewportWidth) || double.IsInfinity(viewportWidth) || viewportWidth <= 0d)
        {
            viewportWidth = Math.Max(0d, scrollViewer.ActualWidth - 4d);
        }

        viewModel.ParallelChartViewportWidth = viewportWidth;
    }
}
