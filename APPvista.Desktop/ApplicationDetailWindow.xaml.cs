using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using APPvista.Desktop.ViewModels;

namespace APPvista.Desktop;

public partial class ApplicationDetailWindow : Window
{
    private const double DetailSwitchOffset = 112d;
    private const double WindowMargin = 24d;
    private const double HistoryChartScrollHintThreshold = 1d;
    private ScrollViewer? _draggingHistoryChartScrollViewer;
    private System.Windows.Point _historyChartDragStart;
    private double _historyChartDragStartOffset;
    private bool _hasAppliedInitialBounds;

    public static readonly DependencyProperty ShowHistoryChartLeftHintProperty =
        DependencyProperty.Register(
            nameof(ShowHistoryChartLeftHint),
            typeof(bool),
            typeof(ApplicationDetailWindow),
            new PropertyMetadata(false));

    public static readonly DependencyProperty ShowHistoryChartRightHintProperty =
        DependencyProperty.Register(
            nameof(ShowHistoryChartRightHint),
            typeof(bool),
            typeof(ApplicationDetailWindow),
            new PropertyMetadata(false));

    public ApplicationDetailWindow(object viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += OnLoaded;
        DataContextChanged += OnDataContextChanged;
        Activated += OnActivated;
        Deactivated += OnDeactivated;
        SourceInitialized += OnSourceInitialized;
    }

    public bool ShowHistoryChartLeftHint
    {
        get => (bool)GetValue(ShowHistoryChartLeftHintProperty);
        set => SetValue(ShowHistoryChartLeftHintProperty, value);
    }

    public bool ShowHistoryChartRightHint
    {
        get => (bool)GetValue(ShowHistoryChartRightHintProperty);
        set => SetValue(ShowHistoryChartRightHintProperty, value);
    }

    protected override void OnClosed(EventArgs e)
    {
        Loaded -= OnLoaded;
        DataContextChanged -= OnDataContextChanged;
        Activated -= OnActivated;
        Deactivated -= OnDeactivated;
        SourceInitialized -= OnSourceInitialized;

        if (DataContext is IDisposable disposable)
        {
            disposable.Dispose();
        }

        DataContext = null;
        ReleaseHistoryChartDrag();
        base.OnClosed(e);
        ScheduleCleanupCollection();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachViewModel(DataContext);
        if (DataContext is ApplicationDetailViewModel viewModel)
        {
            viewModel.SetWindowRenderingActive(IsActive);
            UpdateHistoryChartViewportWidth();
        }

        UpdateDetailSwitchIndicator(animated: false);
        UpdateHistoryChartScrollHints();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        ApplyInitialBounds();
    }

    private void CurrentDataSwitchButton_OnClick(object sender, RoutedEventArgs e)
    {
        AnimateDetailSwitchIndicator(0d);
    }

    private void HistoryDataSwitchButton_OnClick(object sender, RoutedEventArgs e)
    {
        AnimateDetailSwitchIndicator(DetailSwitchOffset);
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is INotifyPropertyChanged oldNotify)
        {
            oldNotify.PropertyChanged -= OnViewModelPropertyChanged;
        }

        AttachViewModel(e.NewValue);
        if (e.NewValue is ApplicationDetailViewModel viewModel)
        {
            viewModel.SetWindowRenderingActive(IsActive);
            UpdateHistoryChartViewportWidth();
        }

        UpdateDetailSwitchIndicator(animated: false);
        UpdateHistoryChartScrollHints();
    }

    private void OnActivated(object? sender, EventArgs e)
    {
        if (DataContext is ApplicationDetailViewModel viewModel)
        {
            viewModel.SetWindowRenderingActive(true);
        }
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (DataContext is ApplicationDetailViewModel viewModel)
        {
            viewModel.SetWindowRenderingActive(false);
        }
    }

    private void AttachViewModel(object? dataContext)
    {
        if (dataContext is INotifyPropertyChanged notify)
        {
            notify.PropertyChanged -= OnViewModelPropertyChanged;
            notify.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ApplicationDetailViewModel.IsCurrentDataMode) ||
            e.PropertyName == nameof(ApplicationDetailViewModel.IsHistoryDataMode))
        {
            Dispatcher.InvokeAsync(() => UpdateDetailSwitchIndicator(animated: true), DispatcherPriority.Render);
            return;
        }

        if (e.PropertyName == nameof(ApplicationDetailViewModel.HistoryChartDisplayWidth))
        {
            Dispatcher.InvokeAsync(() =>
            {
                ClampHistoryChartScrollOffsets();
                UpdateHistoryChartScrollHints();
            }, DispatcherPriority.Render);
            return;
        }

        if (e.PropertyName == nameof(ApplicationDetailViewModel.IsHistoryMonthDimension) ||
            e.PropertyName == nameof(ApplicationDetailViewModel.IsHistoryDataMode))
        {
            Dispatcher.InvokeAsync(UpdateHistoryChartScrollHints, DispatcherPriority.Render);
        }
    }

    private void UpdateDetailSwitchIndicator(bool animated)
    {
        var targetX = DataContext is ApplicationDetailViewModel viewModel && viewModel.IsHistoryDataMode
            ? DetailSwitchOffset
            : 0d;

        if (animated)
        {
            AnimateDetailSwitchIndicator(targetX);
            return;
        }

        if (DetailDataSwitchIndicator.RenderTransform is not TranslateTransform transform)
        {
            transform = new TranslateTransform();
            DetailDataSwitchIndicator.RenderTransform = transform;
        }

        transform.BeginAnimation(TranslateTransform.XProperty, null);
        transform.X = targetX;
    }

    private void AnimateDetailSwitchIndicator(double targetX)
    {
        if (DetailDataSwitchIndicator.RenderTransform is not TranslateTransform transform)
        {
            transform = new TranslateTransform();
            DetailDataSwitchIndicator.RenderTransform = transform;
        }

        var animation = new DoubleAnimation
        {
            From = transform.X,
            To = targetX,
            Duration = TimeSpan.FromMilliseconds(240),
            EasingFunction = new CubicEase
            {
                EasingMode = EasingMode.EaseOut
            }
        };

        transform.BeginAnimation(TranslateTransform.XProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private void HistoryChartScrollViewer_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer || scrollViewer.ScrollableWidth <= 0)
        {
            return;
        }

        _draggingHistoryChartScrollViewer = scrollViewer;
        _historyChartDragStart = e.GetPosition(scrollViewer);
        _historyChartDragStartOffset = scrollViewer.HorizontalOffset;
        scrollViewer.CaptureMouse();
        System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.SizeWE;
        e.Handled = true;
    }

    private void HistoryChartScrollViewer_OnPreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_draggingHistoryChartScrollViewer is not ScrollViewer scrollViewer || !scrollViewer.IsMouseCaptured)
        {
            return;
        }

        var position = e.GetPosition(scrollViewer);
        var deltaX = position.X - _historyChartDragStart.X;
        var targetOffset = Math.Clamp(_historyChartDragStartOffset - deltaX, 0d, scrollViewer.ScrollableWidth);
        scrollViewer.ScrollToHorizontalOffset(targetOffset);
        SyncHistoryChartScrollOffsets(scrollViewer);
        e.Handled = true;
    }

    private void HistoryChartScrollViewer_OnPreviewMouseLeftButtonUp(object sender, System.Windows.Input.MouseEventArgs e)
    {
        ReleaseHistoryChartDrag();
    }

    private void HistoryChartScrollViewer_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateHistoryChartViewportWidth();
        UpdateHistoryChartScrollHints();
    }

    private void HistoryChartScrollViewer_OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.HorizontalChange == 0d && e.ViewportWidthChange == 0d && e.ExtentWidthChange == 0d)
        {
            return;
        }

        UpdateHistoryChartScrollHints();
    }

    private void SyncHistoryChartScrollOffsets()
    {
        SyncHistoryChartScrollOffsets(HistoryNetworkChartScrollViewer);
    }

    private void ResetHistoryChartScrollOffsets()
    {
        HistoryNetworkChartScrollViewer.ScrollToHorizontalOffset(0d);
        HistoryIoChartScrollViewer.ScrollToHorizontalOffset(0d);
        UpdateHistoryChartScrollHints();
    }

    private void ClampHistoryChartScrollOffsets()
    {
        var targetOffset = Math.Min(
            HistoryNetworkChartScrollViewer.HorizontalOffset,
            HistoryNetworkChartScrollViewer.ScrollableWidth);
        targetOffset = Math.Min(targetOffset, HistoryIoChartScrollViewer.ScrollableWidth);
        targetOffset = Math.Max(0d, targetOffset);

        HistoryNetworkChartScrollViewer.ScrollToHorizontalOffset(targetOffset);
        HistoryIoChartScrollViewer.ScrollToHorizontalOffset(targetOffset);
        UpdateHistoryChartScrollHints();
    }

    private void UpdateHistoryChartViewportWidth()
    {
        if (DataContext is not ApplicationDetailViewModel viewModel)
        {
            return;
        }

        var viewportWidth = Math.Max(
            HistoryNetworkChartScrollViewer.ViewportWidth,
            HistoryIoChartScrollViewer.ViewportWidth);

        if (viewportWidth <= 0d)
        {
            viewportWidth = Math.Max(
                HistoryNetworkChartScrollViewer.ActualWidth,
                HistoryIoChartScrollViewer.ActualWidth);
        }

        if (viewportWidth > 0d)
        {
            viewModel.SetHistoryChartViewportWidth(viewportWidth);
        }
    }

    private void SyncHistoryChartScrollOffsets(ScrollViewer activeScrollViewer)
    {
        if (activeScrollViewer == HistoryNetworkChartScrollViewer)
        {
            HistoryIoChartScrollViewer.ScrollToHorizontalOffset(Math.Min(activeScrollViewer.HorizontalOffset, HistoryIoChartScrollViewer.ScrollableWidth));
            UpdateHistoryChartScrollHints();
            return;
        }

        HistoryNetworkChartScrollViewer.ScrollToHorizontalOffset(Math.Min(activeScrollViewer.HorizontalOffset, HistoryNetworkChartScrollViewer.ScrollableWidth));
        UpdateHistoryChartScrollHints();
    }

    private void ReleaseHistoryChartDrag()
    {
        if (_draggingHistoryChartScrollViewer is ScrollViewer scrollViewer && scrollViewer.IsMouseCaptured)
        {
            scrollViewer.ReleaseMouseCapture();
        }

        _draggingHistoryChartScrollViewer = null;
        Mouse.OverrideCursor = null;
    }

    private void UpdateHistoryChartScrollHints()
    {
        if (DataContext is not ApplicationDetailViewModel viewModel ||
            !viewModel.IsHistoryDataMode ||
            !viewModel.IsHistoryMonthDimension)
        {
            ShowHistoryChartLeftHint = false;
            ShowHistoryChartRightHint = false;
            return;
        }

        var activeScrollViewer = HistoryNetworkChartScrollViewer.ScrollableWidth >= HistoryIoChartScrollViewer.ScrollableWidth
            ? HistoryNetworkChartScrollViewer
            : HistoryIoChartScrollViewer;
        var scrollableWidth = activeScrollViewer.ScrollableWidth;

        if (scrollableWidth <= HistoryChartScrollHintThreshold)
        {
            ShowHistoryChartLeftHint = false;
            ShowHistoryChartRightHint = false;
            return;
        }

        var offset = activeScrollViewer.HorizontalOffset;
        ShowHistoryChartLeftHint = offset > HistoryChartScrollHintThreshold;
        ShowHistoryChartRightHint = offset < scrollableWidth - HistoryChartScrollHintThreshold;
    }

    private void ScheduleCleanupCollection()
    {
        Dispatcher.BeginInvoke(() =>
        {
            var hasOtherDetailWindows = System.Windows.Application.Current.Windows
                .OfType<ApplicationDetailWindow>()
                .Any(window => window != this && window.IsLoaded);

            if (hasOtherDetailWindows)
            {
                return;
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }, DispatcherPriority.ApplicationIdle);
    }

    private void ApplyInitialBounds()
    {
        if (_hasAppliedInitialBounds)
        {
            return;
        }

        _hasAppliedInitialBounds = true;
        var screen = Forms.Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        var area = screen.WorkingArea;
        var maxWidth = Math.Max(MinWidth, area.Width - WindowMargin * 2);
        var maxHeight = Math.Max(MinHeight, area.Height - WindowMargin * 2);

        MaxWidth = maxWidth;
        MaxHeight = maxHeight;
        Width = Math.Min(Width, maxWidth);
        Height = Math.Min(Height, maxHeight);
        Left = area.Left + Math.Max(WindowMargin, (area.Width - Width) / 2d);
        Top = area.Top + Math.Max(WindowMargin, (area.Height - Height) / 2d);
    }
}
