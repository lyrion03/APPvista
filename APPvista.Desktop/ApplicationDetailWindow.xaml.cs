using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
    private const double DetailContentSwitchOffset = 132d;
    private const double DetailContentSwitchDurationMilliseconds = 280d;
    private const double WindowMargin = 24d;
    private const double HistoryDatePickerPopupGap = 12d;
    private const double HistoryDatePickerPopupScreenMargin = 12d;
    private const double HistoryChartScrollHintThreshold = 1d;
    private ScrollViewer? _draggingHistoryChartScrollViewer;
    private System.Windows.Point _historyChartDragStart;
    private double _historyChartDragStartOffset;
    private bool _isDraggingHistoryCustomSelection;
    private bool _historyCustomSelectionTarget;
    private bool _hasAppliedInitialBounds;
    private bool? _isHistoryContentVisible;

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
        StateChanged += OnStateChanged;
        IsVisibleChanged += OnIsVisibleChanged;
        LocationChanged += OnWindowBoundsChanged;
        SizeChanged += OnWindowBoundsChanged;
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
        StateChanged -= OnStateChanged;
        IsVisibleChanged -= OnIsVisibleChanged;
        LocationChanged -= OnWindowBoundsChanged;
        SizeChanged -= OnWindowBoundsChanged;
        SourceInitialized -= OnSourceInitialized;

        if (DataContext is IDisposable disposable)
        {
            disposable.Dispose();
        }

        DataContext = null;
        ReleaseHistoryChartDrag();
        base.OnClosed(e);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachViewModel(DataContext);
        if (DataContext is ApplicationDetailViewModel viewModel)
        {
            viewModel.SetWindowRenderingActive(ShouldRenderWindow());
            UpdateHistoryChartViewportWidth();
        }

        UpdateDetailSwitchIndicator(animated: false);
        UpdateDetailContentState(animated: false);
        UpdateHistoryChartScrollHints();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        ApplyInitialBounds();
    }

    private void OnWindowBoundsChanged(object? sender, EventArgs e)
    {
        RefreshHistoryDatePickerPopupPlacement();
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
        CloseHistoryDatePicker();

        if (e.OldValue is INotifyPropertyChanged oldNotify)
        {
            oldNotify.PropertyChanged -= OnViewModelPropertyChanged;
        }

        AttachViewModel(e.NewValue);
        if (e.NewValue is ApplicationDetailViewModel viewModel)
        {
            viewModel.SetWindowRenderingActive(ShouldRenderWindow());
            UpdateHistoryChartViewportWidth();
        }

        UpdateDetailSwitchIndicator(animated: false);
        UpdateDetailContentState(animated: false);
        UpdateHistoryChartScrollHints();
    }

    private void OnActivated(object? sender, EventArgs e)
    {
        UpdateRenderingActiveState();
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        CloseHistoryDatePicker();
        UpdateRenderingActiveState();
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        UpdateRenderingActiveState();
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        UpdateRenderingActiveState();
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
            if (DataContext is ApplicationDetailViewModel modeViewModel && !modeViewModel.IsHistoryDataMode)
            {
                CloseHistoryDatePicker();
            }

            Dispatcher.InvokeAsync(() =>
            {
                UpdateDetailSwitchIndicator(animated: true);
                UpdateDetailContentState(animated: true);
            }, DispatcherPriority.Render);
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

    private void HistoryCalendarDayButton_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not ApplicationDetailViewModel viewModel ||
            !viewModel.IsHistoryCustomDimension ||
            sender is not System.Windows.Controls.Button button ||
            button.DataContext is not ApplicationDetailViewModel.ApplicationHistoryCalendarDayViewModel day ||
            !day.IsSelectable)
        {
            return;
        }

        _isDraggingHistoryCustomSelection = true;
        _historyCustomSelectionTarget = !day.IsSelected;
        viewModel.SetHistoryCustomDateSelection(day.Date, _historyCustomSelectionTarget);
        HistoryDatePickerPopupRoot.CaptureMouse();
        e.Handled = true;
    }

    private void HistoryDatePickerPopupRoot_OnPreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isDraggingHistoryCustomSelection)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            EndHistoryCustomSelectionDrag();
            return;
        }

        if (DataContext is not ApplicationDetailViewModel viewModel)
        {
            return;
        }

        var element = HistoryDatePickerPopupRoot.InputHitTest(e.GetPosition(HistoryDatePickerPopupRoot)) as DependencyObject;
        var button = FindAncestor<System.Windows.Controls.Button>(element);
        if (button?.DataContext is ApplicationDetailViewModel.ApplicationHistoryCalendarDayViewModel day && day.IsSelectable)
        {
            viewModel.SetHistoryCustomDateSelection(day.Date, _historyCustomSelectionTarget);
        }
    }

    private void HistoryDatePickerPopupRoot_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        EndHistoryCustomSelectionDrag();
    }

    private CustomPopupPlacement[] HistoryDatePickerPopup_OnCustomPopupPlacement(System.Windows.Size popupSize, System.Windows.Size targetSize, System.Windows.Point offset)
    {
        var screen = Forms.Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        var area = screen.WorkingArea;
        var targetTopLeft = HistoryDatePickerButton.PointToScreen(new System.Windows.Point(0d, 0d));
        var desiredX = targetTopLeft.X + targetSize.Width + HistoryDatePickerPopupGap;
        var desiredY = targetTopLeft.Y;
        var maxX = area.Right - popupSize.Width - HistoryDatePickerPopupScreenMargin;
        var maxY = area.Bottom - popupSize.Height - HistoryDatePickerPopupScreenMargin;

        desiredX = Math.Max(area.Left + HistoryDatePickerPopupScreenMargin, Math.Min(desiredX, maxX));
        desiredY = Math.Max(area.Top + HistoryDatePickerPopupScreenMargin, Math.Min(desiredY, maxY));

        return
        [
            new CustomPopupPlacement(
                new System.Windows.Point(desiredX - targetTopLeft.X, desiredY - targetTopLeft.Y),
                PopupPrimaryAxis.Horizontal)
        ];
    }

    private void RefreshHistoryDatePickerPopupPlacement()
    {
        if (!HistoryDatePickerPopup.IsOpen)
        {
            return;
        }

        var horizontalOffset = HistoryDatePickerPopup.HorizontalOffset;
        HistoryDatePickerPopup.HorizontalOffset = horizontalOffset + 0.1d;
        HistoryDatePickerPopup.HorizontalOffset = horizontalOffset;
    }

    private void CloseHistoryDatePicker()
    {
        if (DataContext is ApplicationDetailViewModel viewModel && viewModel.IsHistoryDatePickerOpen)
        {
            viewModel.IsHistoryDatePickerOpen = false;
        }
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);

        if (DataContext is not ApplicationDetailViewModel viewModel || !viewModel.IsHistoryDatePickerOpen)
        {
            return;
        }

        var source = e.OriginalSource as DependencyObject;
        if (IsWithinElement(source, HistoryDatePickerPopupRoot) ||
            IsWithinElement(source, HistoryDatePickerButton) ||
            IsWithinElement(source, HistoryDayDimensionButton) ||
            IsWithinElement(source, HistoryWeekDimensionButton) ||
            IsWithinElement(source, HistoryMonthDimensionButton) ||
            IsWithinElement(source, HistoryCustomDimensionButton))
        {
            return;
        }

        viewModel.IsHistoryDatePickerOpen = false;
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

    private void UpdateDetailContentState(bool animated)
    {
        if (DataContext is not ApplicationDetailViewModel viewModel)
        {
            return;
        }

        var showHistory = viewModel.IsHistoryDataMode;
        ResetDetailContentScrollPosition(showHistory);

        if (_isHistoryContentVisible is null || !animated)
        {
            ApplyDetailContentState(showHistory);
            _isHistoryContentVisible = showHistory;
            return;
        }

        if (_isHistoryContentVisible == showHistory)
        {
            return;
        }

        AnimateDetailContentTransition(showHistory);
        _isHistoryContentVisible = showHistory;
    }

    private void ApplyDetailContentState(bool showHistory)
    {
        var visiblePanel = showHistory ? HistoryDataPanel : CurrentDataPanel;
        var hiddenPanel = showHistory ? CurrentDataPanel : HistoryDataPanel;

        StopDetailContentAnimations(CurrentDataPanel);
        StopDetailContentAnimations(HistoryDataPanel);

        hiddenPanel.Visibility = Visibility.Collapsed;
        hiddenPanel.Opacity = 0d;
        hiddenPanel.IsHitTestVisible = false;
        System.Windows.Controls.Panel.SetZIndex(hiddenPanel, 0);
        SetPanelOffset(hiddenPanel, 0d);

        visiblePanel.Visibility = Visibility.Visible;
        visiblePanel.Opacity = 1d;
        visiblePanel.IsHitTestVisible = true;
        System.Windows.Controls.Panel.SetZIndex(visiblePanel, 1);
        SetPanelOffset(visiblePanel, 0d);
    }

    private void AnimateDetailContentTransition(bool showHistory)
    {
        var incomingPanel = showHistory ? HistoryDataPanel : CurrentDataPanel;
        var outgoingPanel = showHistory ? CurrentDataPanel : HistoryDataPanel;
        var direction = showHistory ? 1d : -1d;
        var offset = ResolveDetailContentOffset();
        var frozenHostMinWidth = Math.Max(0d, DetailContentHost.ActualWidth);
        var frozenHostMinHeight = Math.Max(CurrentDataPanel.ActualHeight, HistoryDataPanel.ActualHeight);

        StopDetailContentAnimations(incomingPanel);
        StopDetailContentAnimations(outgoingPanel);
        FreezeDetailContentHost(frozenHostMinWidth, frozenHostMinHeight);

        incomingPanel.Visibility = Visibility.Visible;
        incomingPanel.IsHitTestVisible = false;
        incomingPanel.Opacity = 0d;
        System.Windows.Controls.Panel.SetZIndex(incomingPanel, 2);
        SetPanelOffset(incomingPanel, direction * offset);

        outgoingPanel.Visibility = Visibility.Visible;
        outgoingPanel.IsHitTestVisible = false;
        outgoingPanel.Opacity = 1d;
        System.Windows.Controls.Panel.SetZIndex(outgoingPanel, 1);
        SetPanelOffset(outgoingPanel, 0d);

        var duration = TimeSpan.FromMilliseconds(DetailContentSwitchDurationMilliseconds);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var incomingOffsetAnimation = new DoubleAnimation(direction * offset, 0d, duration) { EasingFunction = easing };
        var outgoingOffsetAnimation = new DoubleAnimation(0d, -direction * offset, duration) { EasingFunction = easing };
        var incomingOpacityAnimation = new DoubleAnimation(0d, 1d, duration) { EasingFunction = easing };
        var outgoingOpacityAnimation = new DoubleAnimation(1d, 0d, duration) { EasingFunction = easing };

        outgoingOpacityAnimation.Completed += (_, _) =>
        {
            outgoingPanel.Visibility = Visibility.Collapsed;
            outgoingPanel.Opacity = 0d;
            outgoingPanel.IsHitTestVisible = false;
            System.Windows.Controls.Panel.SetZIndex(outgoingPanel, 0);
            SetPanelOffset(outgoingPanel, 0d);

            incomingPanel.Visibility = Visibility.Visible;
            incomingPanel.Opacity = 1d;
            incomingPanel.IsHitTestVisible = true;
            System.Windows.Controls.Panel.SetZIndex(incomingPanel, 1);
            SetPanelOffset(incomingPanel, 0d);
            ReleaseDetailContentHostFreeze();
        };

        GetPanelTransform(incomingPanel).BeginAnimation(TranslateTransform.XProperty, incomingOffsetAnimation, HandoffBehavior.SnapshotAndReplace);
        GetPanelTransform(outgoingPanel).BeginAnimation(TranslateTransform.XProperty, outgoingOffsetAnimation, HandoffBehavior.SnapshotAndReplace);
        incomingPanel.BeginAnimation(OpacityProperty, incomingOpacityAnimation, HandoffBehavior.SnapshotAndReplace);
        outgoingPanel.BeginAnimation(OpacityProperty, outgoingOpacityAnimation, HandoffBehavior.SnapshotAndReplace);
    }

    private static void StopDetailContentAnimations(FrameworkElement panel)
    {
        panel.BeginAnimation(OpacityProperty, null);
        GetPanelTransform(panel).BeginAnimation(TranslateTransform.XProperty, null);
    }

    private static TranslateTransform GetPanelTransform(UIElement panel)
    {
        if (panel.RenderTransform is not TranslateTransform transform)
        {
            transform = new TranslateTransform();
            panel.RenderTransform = transform;
        }

        return transform;
    }

    private static void SetPanelOffset(UIElement panel, double offset)
    {
        GetPanelTransform(panel).X = offset;
    }

    private double ResolveDetailContentOffset()
    {
        var hostWidth = DetailContentHost.ActualWidth;
        return Math.Max(DetailContentSwitchOffset, hostWidth * 0.12d);
    }

    private void FreezeDetailContentHost(double minWidth, double minHeight)
    {
        if (minWidth > 0d)
        {
            DetailContentHost.MinWidth = minWidth;
        }

        if (minHeight > 0d)
        {
            DetailContentHost.MinHeight = minHeight;
        }
    }

    private void ReleaseDetailContentHostFreeze()
    {
        DetailContentHost.MinWidth = 0d;
        DetailContentHost.MinHeight = 0d;
    }

    private void ResetDetailContentScrollPosition(bool showHistory)
    {
        if (!showHistory)
        {
            return;
        }

        HistoryDataPanel.ScrollToHome();
        HistoryDataPanel.UpdateLayout();
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
        Top = area.Top;
    }

    private void UpdateRenderingActiveState()
    {
        if (DataContext is ApplicationDetailViewModel viewModel)
        {
            viewModel.SetWindowRenderingActive(ShouldRenderWindow());
        }
    }

    protected override void OnPreviewMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        base.OnPreviewMouseMove(e);

        if (!_isDraggingHistoryCustomSelection)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            EndHistoryCustomSelectionDrag();
            return;
        }

        if (DataContext is not ApplicationDetailViewModel viewModel)
        {
            return;
        }

        var element = InputHitTest(e.GetPosition(this)) as DependencyObject;
        var button = FindAncestor<System.Windows.Controls.Button>(element);
        if (button?.DataContext is ApplicationDetailViewModel.ApplicationHistoryCalendarDayViewModel day && day.IsSelectable)
        {
            viewModel.SetHistoryCustomDateSelection(day.Date, _historyCustomSelectionTarget);
        }
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonUp(e);
        EndHistoryCustomSelectionDrag();
    }

    private void EndHistoryCustomSelectionDrag()
    {
        if (!_isDraggingHistoryCustomSelection)
        {
            return;
        }

        _isDraggingHistoryCustomSelection = false;
        if (HistoryDatePickerPopupRoot.IsMouseCaptured)
        {
            HistoryDatePickerPopupRoot.ReleaseMouseCapture();
        }
        else if (IsMouseCaptured)
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

    private static bool IsWithinElement(DependencyObject? source, DependencyObject element)
    {
        while (source is not null)
        {
            if (ReferenceEquals(source, element))
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private bool ShouldRenderWindow() => IsVisible && WindowState != WindowState.Minimized;
}
