using System.ComponentModel;
using System.Windows.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

namespace APPvista.Desktop;

public partial class MainWindow : Window
{
    private const double DashboardHeaderSwitchOffset = 88d;
    private const double DashboardHeaderContentSwitchOffset = 36d;
    private const double DashboardHeaderAuxiliarySwitchOffset = 44d;
    private const double DashboardPageSwitchOffset = 120d;
    private const double DashboardHeaderSwitchDurationMilliseconds = 280d;
    private const double DashboardPageSwitchDurationMilliseconds = 300d;

    private INotifyPropertyChanged? _notifyingContext;
    private bool? _lastHistoryPageActive;
    private bool? _lastHistoryNetworkSplitMode;
    private bool? _lastHistoryIoSplitMode;
    private bool _isDraggingHistoryCustomSelection;
    private bool _historyCustomSelectionTarget;
    private bool _isHistoryPageLoaded;
    private bool _hasAppliedInitialTopAlignment;
    private ImageSource? _pendingHeaderSnapshot;
    private double _pendingHeaderSnapshotHeight;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        DataContextChanged += OnDataContextChanged;
        Activated += OnActivated;
        Deactivated += OnDeactivated;
        StateChanged += OnStateChanged;
        IsVisibleChanged += OnIsVisibleChanged;
        Closing += OnClosing;
    }

    public MainWindow(object viewModel) : this()
    {
        DataContext = viewModel;
    }

    protected override void OnClosed(EventArgs e)
    {
        StateChanged -= OnStateChanged;
        IsVisibleChanged -= OnIsVisibleChanged;
        Closing -= OnClosing;
        ReleaseHistoryPage();

        if (_notifyingContext is not null)
        {
            _notifyingContext.PropertyChanged -= OnViewModelPropertyChanged;
            _notifyingContext = null;
        }

        if (System.Windows.Application.Current is App app && !app.IsExitRequested)
        {
            app.ReleaseMainWindow();
        }

        base.OnClosed(e);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyInitialTopAlignment();

        if (DataContext is ViewModels.DashboardViewModel viewModel)
        {
            viewModel.SetMainWindowRenderingActive(ShouldRenderWindow());
            UpdateApplicationCardViewportWidth();
            Dispatcher.BeginInvoke(
                viewModel.StartBackgroundInitialization,
                DispatcherPriority.Background);
        }

        var isHistoryActive = ReadHistoryPageState();
        var isHistoryNetworkSplitMode = ReadHistoryNetworkSplitMode();
        var isHistoryIoSplitMode = ReadHistoryIoSplitMode();
        _lastHistoryPageActive = isHistoryActive;
        _lastHistoryNetworkSplitMode = isHistoryNetworkSplitMode;
        _lastHistoryIoSplitMode = isHistoryIoSplitMode;
        ApplyOverviewModeMenuState(isHistoryActive, animate: false);
        ApplyRealtimeHeaderOverviewState(isHistoryActive, animate: false);
        ApplyHistoryCalendarState(isHistoryActive, animate: false);
        ApplyHeaderHistoryContentState(isHistoryActive, animate: false);
        ApplyPageAnimation(isHistoryActive, animate: false);
        ApplySwitchIndicatorAnimation(isHistoryActive, animate: false);
        ApplyHistoryNetworkSwitchIndicatorAnimation(isHistoryNetworkSplitMode, animate: false);
        ApplyHistoryIoSwitchIndicatorAnimation(isHistoryIoSplitMode, animate: false);
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_notifyingContext is not null)
        {
            _notifyingContext.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _notifyingContext = e.NewValue as INotifyPropertyChanged;
        if (_notifyingContext is not null)
        {
            _notifyingContext.PropertyChanged += OnViewModelPropertyChanged;
        }

        if (e.NewValue is ViewModels.DashboardViewModel viewModel)
        {
            viewModel.SetMainWindowRenderingActive(ShouldRenderWindow());
            UpdateApplicationCardViewportWidth();
        }

        if (_isHistoryPageLoaded && HistoryPageHost.Content is FrameworkElement historyView)
        {
            historyView.DataContext = e.NewValue;
        }

        var isHistoryActive = ReadHistoryPageState();
        var isHistoryNetworkSplitMode = ReadHistoryNetworkSplitMode();
        var isHistoryIoSplitMode = ReadHistoryIoSplitMode();
        _lastHistoryPageActive = isHistoryActive;
        _lastHistoryNetworkSplitMode = isHistoryNetworkSplitMode;
        _lastHistoryIoSplitMode = isHistoryIoSplitMode;
        ApplyOverviewModeMenuState(isHistoryActive, animate: false);
        ApplyRealtimeHeaderOverviewState(isHistoryActive, animate: false);
        ApplyHistoryCalendarState(isHistoryActive, animate: false);
        ApplyHeaderHistoryContentState(isHistoryActive, animate: false);
        ApplyPageAnimation(isHistoryActive, animate: false);
        ApplySwitchIndicatorAnimation(isHistoryActive, animate: false);
        ApplyHistoryNetworkSwitchIndicatorAnimation(isHistoryNetworkSplitMode, animate: false);
        ApplyHistoryIoSwitchIndicatorAnimation(isHistoryIoSplitMode, animate: false);
    }

    private void OnActivated(object? sender, EventArgs e)
    {
        UpdateRenderingActiveState();
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
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

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (System.Windows.Application.Current is not App app || app.IsExitRequested)
        {
            return;
        }

        if (DataContext is ViewModels.DashboardViewModel viewModel)
        {
            viewModel.PrepareForTrayClose();
        }

        ReleaseHistoryPage();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ViewModels.DashboardViewModel.IsHistoryPageActive) or nameof(ViewModels.DashboardViewModel.IsRealtimePageActive))
        {
            var isHistoryActive = ReadHistoryPageState();
            var shouldAnimate = _lastHistoryPageActive != isHistoryActive;
            if (shouldAnimate)
            {
                CaptureHeaderSnapshot();
            }

            Dispatcher.BeginInvoke(() =>
            {
                _lastHistoryPageActive = isHistoryActive;
                ApplyHeaderAnimation(isHistoryActive, animate: shouldAnimate);
                ApplyOverviewModeMenuState(isHistoryActive, animate: shouldAnimate);
                ApplyRealtimeHeaderOverviewState(isHistoryActive, animate: shouldAnimate);
                ApplyHistoryCalendarState(isHistoryActive, animate: shouldAnimate);
                ApplyHeaderHistoryContentState(isHistoryActive, animate: shouldAnimate);
                ApplyPageAnimation(isHistoryActive, animate: shouldAnimate);
            }, DispatcherPriority.Render);
            return;
        }

        if (e.PropertyName is nameof(ViewModels.DashboardViewModel.IsHistoryNetworkSplitMode) or nameof(ViewModels.DashboardViewModel.IsHistoryNetworkTotalMode))
        {
            Dispatcher.BeginInvoke(() =>
            {
                var isSplitMode = ReadHistoryNetworkSplitMode();
                var shouldAnimate = _lastHistoryNetworkSplitMode != isSplitMode;
                _lastHistoryNetworkSplitMode = isSplitMode;
                ApplyHistoryNetworkSwitchIndicatorAnimation(isSplitMode, animate: shouldAnimate);
            }, DispatcherPriority.Render);
            return;
        }

        if (e.PropertyName is nameof(ViewModels.DashboardViewModel.IsHistoryIoSplitMode) or nameof(ViewModels.DashboardViewModel.IsHistoryIoTotalMode))
        {
            Dispatcher.BeginInvoke(() =>
            {
                var isSplitMode = ReadHistoryIoSplitMode();
                var shouldAnimate = _lastHistoryIoSplitMode != isSplitMode;
                _lastHistoryIoSplitMode = isSplitMode;
                ApplyHistoryIoSwitchIndicatorAnimation(isSplitMode, animate: shouldAnimate);
            }, DispatcherPriority.Render);
        }
    }

    private void RealtimeSwitchButton_OnClick(object sender, RoutedEventArgs e)
    {
        ApplySwitchIndicatorAnimation(isHistoryActive: false, animate: true);
    }

    private void HistorySwitchButton_OnClick(object sender, RoutedEventArgs e)
    {
        EnsureHistoryPageLoaded();
        ApplySwitchIndicatorAnimation(isHistoryActive: true, animate: true);
    }

    private void HistoryNetworkTotalButton_OnClick(object sender, RoutedEventArgs e)
    {
        _lastHistoryNetworkSplitMode = false;
        ApplyHistoryNetworkSwitchIndicatorAnimation(isSplitMode: false, animate: true);
    }

    private void HistoryNetworkSplitButton_OnClick(object sender, RoutedEventArgs e)
    {
        _lastHistoryNetworkSplitMode = true;
        ApplyHistoryNetworkSwitchIndicatorAnimation(isSplitMode: true, animate: true);
    }

    private void HistoryIoTotalButton_OnClick(object sender, RoutedEventArgs e)
    {
        _lastHistoryIoSplitMode = false;
        ApplyHistoryIoSwitchIndicatorAnimation(isSplitMode: false, animate: true);
    }

    private void HistoryIoSplitButton_OnClick(object sender, RoutedEventArgs e)
    {
        _lastHistoryIoSplitMode = true;
        ApplyHistoryIoSwitchIndicatorAnimation(isSplitMode: true, animate: true);
    }

    private void ApplicationsList_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateApplicationCardViewportWidth();
    }

    private void HistoryCalendarDayButton_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not ViewModels.DashboardViewModel viewModel ||
            !viewModel.IsHistoryCustomDimension ||
            sender is not System.Windows.Controls.Button button ||
            button.DataContext is not ViewModels.HistoryCalendarDayViewModel day ||
            !day.IsSelectable)
        {
            return;
        }

        _isDraggingHistoryCustomSelection = true;
        _historyCustomSelectionTarget = !day.IsSelected;
        viewModel.SetHistoryCustomDateSelection(day.Date, _historyCustomSelectionTarget);
        CaptureMouse();
        e.Handled = true;
    }

    private void ApplyInitialTopAlignment()
    {
        if (_hasAppliedInitialTopAlignment)
        {
            return;
        }

        _hasAppliedInitialTopAlignment = true;
        var screen = Forms.Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        Top = screen.WorkingArea.Top;
    }

    private bool ReadHistoryPageState()
    {
        if (DataContext is ViewModels.DashboardViewModel viewModel)
        {
            return viewModel.IsHistoryPageActive;
        }

        return false;
    }

    private bool ReadHistoryNetworkSplitMode()
    {
        return DataContext is ViewModels.DashboardViewModel viewModel && viewModel.IsHistoryNetworkSplitMode;
    }

    private bool ReadHistoryIoSplitMode()
    {
        return DataContext is ViewModels.DashboardViewModel viewModel && viewModel.IsHistoryIoSplitMode;
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

        if (DataContext is not ViewModels.DashboardViewModel viewModel)
        {
            return;
        }

        var element = InputHitTest(e.GetPosition(this)) as DependencyObject;
        var button = FindAncestor<System.Windows.Controls.Button>(element);
        if (button?.DataContext is ViewModels.HistoryCalendarDayViewModel day && day.IsSelectable)
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

    private void UpdateApplicationCardViewportWidth()
    {
        if (DataContext is not ViewModels.DashboardViewModel viewModel)
        {
            return;
        }

        var applicationsList = GetApplicationsListBox();
        if (applicationsList is null)
        {
            return;
        }

        var viewportWidth = applicationsList.ActualWidth;
        if (viewportWidth > 0d)
        {
            viewModel.SetApplicationCardViewportWidth(viewportWidth);
        }
    }

    private System.Windows.Controls.ListBox? GetApplicationsListBox()
    {
        return FindName("ApplicationsList") as System.Windows.Controls.ListBox;
    }

    private void ApplyPageAnimation(bool isHistoryActive, bool animate)
    {
        if (isHistoryActive)
        {
            EnsureHistoryPageLoaded();
            ResetHistoryPageScrollPosition();
        }

        var enteringPage = isHistoryActive ? HistoryPageRoot : RealtimePageRoot;
        var exitingPage = isHistoryActive ? RealtimePageRoot : HistoryPageRoot;

        StopPageAnimation(RealtimePageRoot);
        StopPageAnimation(HistoryPageRoot);

        if (!animate)
        {
            ApplyPageState(enteringPage, isVisible: true, opacity: 1d, zIndex: 2, offsetY: 0d, isInteractive: true);
            ApplyPageState(exitingPage, isVisible: false, opacity: 0d, zIndex: 0, offsetY: 0d, isInteractive: false);
            return;
        }

        var pageOffset = ResolveDashboardPageOffset();
        var incomingOffset = isHistoryActive ? -pageOffset : pageOffset;
        var outgoingOffset = -incomingOffset;

        FreezeDashboardPageHost(
            Math.Max(RealtimePageRoot.ActualWidth, HistoryPageRoot.ActualWidth),
            Math.Max(RealtimePageRoot.ActualHeight, HistoryPageRoot.ActualHeight));

        ApplyPageState(enteringPage, isVisible: true, opacity: 0d, zIndex: 2, offsetY: incomingOffset, isInteractive: false);
        ApplyPageState(exitingPage, isVisible: true, opacity: 1d, zIndex: 1, offsetY: 0d, isInteractive: false);

        var duration = TimeSpan.FromMilliseconds(DashboardPageSwitchDurationMilliseconds);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var incomingOpacityAnimation = new DoubleAnimation(0d, 1d, duration) { EasingFunction = easing };
        var outgoingOpacityAnimation = new DoubleAnimation(1d, 0d, duration) { EasingFunction = easing };
        var incomingOffsetAnimation = new DoubleAnimation(GetTranslateY(enteringPage), 0d, duration) { EasingFunction = easing };
        var outgoingOffsetAnimation = new DoubleAnimation(0d, outgoingOffset, duration) { EasingFunction = easing };

        outgoingOpacityAnimation.Completed += (_, _) =>
        {
            ApplyPageState(exitingPage, isVisible: false, opacity: 0d, zIndex: 0, offsetY: 0d, isInteractive: false);
            ApplyPageState(enteringPage, isVisible: true, opacity: 1d, zIndex: 2, offsetY: 0d, isInteractive: true);
            ReleaseDashboardPageHostFreeze();
        };

        enteringPage.BeginAnimation(OpacityProperty, incomingOpacityAnimation, HandoffBehavior.SnapshotAndReplace);
        exitingPage.BeginAnimation(OpacityProperty, outgoingOpacityAnimation, HandoffBehavior.SnapshotAndReplace);
        GetTranslateTransform(enteringPage).BeginAnimation(TranslateTransform.YProperty, incomingOffsetAnimation, HandoffBehavior.SnapshotAndReplace);
        GetTranslateTransform(exitingPage).BeginAnimation(TranslateTransform.YProperty, outgoingOffsetAnimation, HandoffBehavior.SnapshotAndReplace);
    }

    private void ApplyHeaderAnimation(bool isHistoryActive, bool animate)
    {
        StopHeaderAnimation();
        StopHeaderContainerAnimation();

        if (!animate || _pendingHeaderSnapshot is null)
        {
            DashboardHeaderTransitionImage.Source = null;
            DashboardHeaderTransitionOverlay.Visibility = Visibility.Collapsed;
            DashboardHeaderTransitionOverlay.Opacity = 0d;
            SetTranslateY(DashboardHeaderTransitionOverlay, 0d);
            DashboardHeaderRoot.Opacity = 1d;
            SetTranslateY(DashboardHeaderRoot, 0d);
            DashboardHeaderHost.Height = double.NaN;
            ReleaseDashboardHeaderHostFreeze();
            _pendingHeaderSnapshot = null;
            _pendingHeaderSnapshotHeight = 0d;
            return;
        }

        var incomingOffset = isHistoryActive ? -DashboardHeaderSwitchOffset : DashboardHeaderSwitchOffset;
        var outgoingOffset = -incomingOffset;

        FreezeDashboardHeaderHost(Math.Max(_pendingHeaderSnapshotHeight, DashboardHeaderRoot.ActualHeight));

        DashboardHeaderTransitionImage.Source = _pendingHeaderSnapshot;
        DashboardHeaderTransitionOverlay.Visibility = Visibility.Visible;
        DashboardHeaderTransitionOverlay.Opacity = 1d;
        System.Windows.Controls.Panel.SetZIndex(DashboardHeaderTransitionOverlay, 10);
        SetTranslateY(DashboardHeaderTransitionOverlay, 0d);

        DashboardHeaderRoot.Opacity = 0d;
        SetTranslateY(DashboardHeaderRoot, incomingOffset);

        var duration = TimeSpan.FromMilliseconds(DashboardHeaderSwitchDurationMilliseconds);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var incomingOpacityAnimation = new DoubleAnimation(0d, 1d, duration) { EasingFunction = easing };
        var outgoingOpacityAnimation = new DoubleAnimation(1d, 0d, duration) { EasingFunction = easing };
        var incomingOffsetAnimation = new DoubleAnimation(incomingOffset, 0d, duration) { EasingFunction = easing };
        var outgoingOffsetAnimation = new DoubleAnimation(0d, outgoingOffset, duration) { EasingFunction = easing };

        outgoingOpacityAnimation.Completed += (_, _) =>
        {
            DashboardHeaderTransitionImage.Source = null;
            DashboardHeaderTransitionOverlay.Visibility = Visibility.Collapsed;
            DashboardHeaderTransitionOverlay.Opacity = 0d;
            SetTranslateY(DashboardHeaderTransitionOverlay, 0d);
            DashboardHeaderRoot.Opacity = 1d;
            SetTranslateY(DashboardHeaderRoot, 0d);
            ReleaseDashboardHeaderHostFreeze();
        };

        DashboardHeaderRoot.BeginAnimation(OpacityProperty, incomingOpacityAnimation, HandoffBehavior.SnapshotAndReplace);
        DashboardHeaderTransitionOverlay.BeginAnimation(OpacityProperty, outgoingOpacityAnimation, HandoffBehavior.SnapshotAndReplace);
        GetTranslateTransform(DashboardHeaderRoot).BeginAnimation(TranslateTransform.YProperty, incomingOffsetAnimation, HandoffBehavior.SnapshotAndReplace);
        GetTranslateTransform(DashboardHeaderTransitionOverlay).BeginAnimation(TranslateTransform.YProperty, outgoingOffsetAnimation, HandoffBehavior.SnapshotAndReplace);

        _pendingHeaderSnapshot = null;
        _pendingHeaderSnapshotHeight = 0d;
    }

    private void ApplyHeaderHistoryContentState(bool isHistoryActive, bool animate)
    {
        StopHeaderHistoryContentAnimation();

        if (!animate)
        {
            DashboardHistoryHeaderContent.Visibility = isHistoryActive ? Visibility.Visible : Visibility.Collapsed;
            DashboardHistoryHeaderContent.IsHitTestVisible = isHistoryActive;
            DashboardHistoryHeaderContent.Opacity = isHistoryActive ? 1d : 0d;
            DashboardHistoryHeaderContent.MaxHeight = isHistoryActive ? double.PositiveInfinity : 0d;
            DashboardHeaderHost.Height = double.NaN;
            SetTranslateY(DashboardHistoryHeaderContent, 0d);
            return;
        }

        var baseHeaderHeight = _pendingHeaderSnapshotHeight > 0d ? _pendingHeaderSnapshotHeight : DashboardHeaderHost.ActualHeight;
        var targetHeaderHeight = ResolveDashboardHeaderHeight(isHistoryActive);
        AnimateHeaderContainerHeight(baseHeaderHeight, targetHeaderHeight);

        if (isHistoryActive)
        {
            DashboardHistoryHeaderContent.Visibility = Visibility.Visible;
            DashboardHistoryHeaderContent.IsHitTestVisible = false;
            DashboardHistoryHeaderContent.Opacity = 0d;
            DashboardHistoryHeaderContent.MaxHeight = double.PositiveInfinity;
            DashboardHistoryHeaderContent.UpdateLayout();
            var targetHeight = ResolveHistoryHeaderContentExpandedHeight();
            DashboardHistoryHeaderContent.MaxHeight = 0d;
            SetTranslateY(DashboardHistoryHeaderContent, -DashboardHeaderContentSwitchOffset);

            var duration = TimeSpan.FromMilliseconds(DashboardHeaderSwitchDurationMilliseconds);
            var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
            var heightAnimation = new DoubleAnimation(0d, targetHeight, duration) { EasingFunction = easing };
            heightAnimation.Completed += (_, _) =>
            {
                DashboardHistoryHeaderContent.MaxHeight = double.PositiveInfinity;
                DashboardHistoryHeaderContent.IsHitTestVisible = true;
            };

            DashboardHistoryHeaderContent.BeginAnimation(
                FrameworkElement.MaxHeightProperty,
                heightAnimation,
                HandoffBehavior.SnapshotAndReplace);
            DashboardHistoryHeaderContent.BeginAnimation(
                OpacityProperty,
                new DoubleAnimation(0d, 1d, duration) { EasingFunction = easing },
                HandoffBehavior.SnapshotAndReplace);
            GetTranslateTransform(DashboardHistoryHeaderContent).BeginAnimation(
                TranslateTransform.YProperty,
                new DoubleAnimation(-DashboardHeaderContentSwitchOffset, 0d, duration) { EasingFunction = easing },
                HandoffBehavior.SnapshotAndReplace);
            return;
        }

        DashboardHistoryHeaderContent.Visibility = Visibility.Visible;
        DashboardHistoryHeaderContent.IsHitTestVisible = false;
        DashboardHistoryHeaderContent.Opacity = 1d;
        DashboardHistoryHeaderContent.MaxHeight = ResolveHistoryHeaderContentExpandedHeight();
        SetTranslateY(DashboardHistoryHeaderContent, 0d);

        var exitDuration = TimeSpan.FromMilliseconds(DashboardHeaderSwitchDurationMilliseconds);
        var exitEasing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var opacityAnimation = new DoubleAnimation(1d, 0d, exitDuration) { EasingFunction = exitEasing };
        DashboardHistoryHeaderContent.BeginAnimation(
            FrameworkElement.MaxHeightProperty,
            new DoubleAnimation(DashboardHistoryHeaderContent.MaxHeight, 0d, exitDuration) { EasingFunction = exitEasing },
            HandoffBehavior.SnapshotAndReplace);
        opacityAnimation.Completed += (_, _) =>
        {
            DashboardHistoryHeaderContent.Visibility = Visibility.Collapsed;
            DashboardHistoryHeaderContent.IsHitTestVisible = false;
            DashboardHistoryHeaderContent.Opacity = 0d;
            DashboardHistoryHeaderContent.MaxHeight = 0d;
            SetTranslateY(DashboardHistoryHeaderContent, 0d);
        };

        DashboardHistoryHeaderContent.BeginAnimation(OpacityProperty, opacityAnimation, HandoffBehavior.SnapshotAndReplace);
        GetTranslateTransform(DashboardHistoryHeaderContent).BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(0d, -DashboardHeaderContentSwitchOffset, exitDuration) { EasingFunction = exitEasing },
            HandoffBehavior.SnapshotAndReplace);
    }

    private void ApplyRealtimeHeaderOverviewState(bool isHistoryActive, bool animate)
    {
        StopAuxiliaryHeaderAnimation(RealtimeOverviewHeaderPanel);
        var shouldShow = !isHistoryActive;

        if (!animate)
        {
            ApplyHeaderAuxiliaryState(RealtimeOverviewHeaderPanel, shouldShow, 0d);
            return;
        }

        var incomingOffset = isHistoryActive ? -DashboardHeaderAuxiliarySwitchOffset : DashboardHeaderAuxiliarySwitchOffset;
        var outgoingOffset = -incomingOffset;
        AnimateHeaderAuxiliaryTransition(RealtimeOverviewHeaderPanel, shouldShow, incomingOffset, outgoingOffset);
    }

    private void ApplyOverviewModeMenuState(bool isHistoryActive, bool animate)
    {
        StopAuxiliaryHeaderAnimation(OverviewModeMenu);
        var shouldShow = !isHistoryActive;

        if (!animate)
        {
            ApplyHeaderAuxiliaryState(OverviewModeMenu, shouldShow, 0d);
            return;
        }

        AnimateHeaderMenuTransition(shouldShow);
    }

    private void ApplyHistoryCalendarState(bool isHistoryActive, bool animate)
    {
        StopAuxiliaryHeaderAnimation(HistoryCalendarCard);
        var shouldShow = isHistoryActive;

        if (!animate)
        {
            ApplyHeaderAuxiliaryState(HistoryCalendarCard, shouldShow, 0d);
            return;
        }

        var incomingOffset = isHistoryActive ? -DashboardHeaderAuxiliarySwitchOffset : DashboardHeaderAuxiliarySwitchOffset;
        var outgoingOffset = -incomingOffset;
        AnimateHeaderAuxiliaryTransition(HistoryCalendarCard, shouldShow, incomingOffset, outgoingOffset);
    }

    private void ApplySwitchIndicatorAnimation(bool isHistoryActive, bool animate)
    {
        if (DashboardSwitchIndicator.RenderTransform is not TranslateTransform transform)
        {
            return;
        }

        var currentX = transform.X;
        var targetX = isHistoryActive ? 164d : 0d;
        transform.BeginAnimation(TranslateTransform.XProperty, null);

        if (!animate)
        {
            transform.X = targetX;
            return;
        }

        var duration = TimeSpan.FromMilliseconds(240);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        transform.BeginAnimation(
            TranslateTransform.XProperty,
            new DoubleAnimation
            {
                From = currentX,
                To = targetX,
                Duration = duration,
                EasingFunction = easing
            });
    }

    private void ApplyHistoryNetworkSwitchIndicatorAnimation(bool isSplitMode, bool animate)
    {
        ApplyIndicatorAnimation(HistoryNetworkSwitchIndicator, isSplitMode ? 80d : 0d, animate);
    }

    private void ApplyHistoryIoSwitchIndicatorAnimation(bool isSplitMode, bool animate)
    {
        ApplyIndicatorAnimation(HistoryIoSwitchIndicator, isSplitMode ? 80d : 0d, animate);
    }

    private static void ApplyIndicatorAnimation(FrameworkElement indicator, double targetX, bool animate)
    {
        if (indicator.RenderTransform is not TranslateTransform transform)
        {
            return;
        }

        var currentX = transform.X;
        transform.BeginAnimation(TranslateTransform.XProperty, null);

        if (!animate)
        {
            transform.X = targetX;
            return;
        }

        var duration = TimeSpan.FromMilliseconds(220);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        transform.BeginAnimation(
            TranslateTransform.XProperty,
            new DoubleAnimation
            {
                From = currentX,
                To = targetX,
                Duration = duration,
                EasingFunction = easing
            });
    }

    private static void StopPageAnimation(UIElement element)
    {
        element.BeginAnimation(OpacityProperty, null);
        GetTranslateTransform(element).BeginAnimation(TranslateTransform.YProperty, null);
    }

    private static void ApplyPageState(UIElement element, bool isVisible, double opacity, int zIndex, double offsetY, bool isInteractive)
    {
        element.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        element.Opacity = opacity;
        element.IsHitTestVisible = isInteractive;
        System.Windows.Controls.Panel.SetZIndex(element, zIndex);
        SetTranslateY(element, offsetY);
    }

    private void CaptureHeaderSnapshot()
    {
        if (DashboardHeaderRoot.ActualWidth <= 0d || DashboardHeaderRoot.ActualHeight <= 0d)
        {
            _pendingHeaderSnapshot = null;
            _pendingHeaderSnapshotHeight = 0d;
            return;
        }

        DashboardHeaderRoot.UpdateLayout();
        _pendingHeaderSnapshot = RenderElementSnapshot(DashboardHeaderRoot);
        _pendingHeaderSnapshotHeight = DashboardHeaderRoot.ActualHeight;
    }

    private static ImageSource? RenderElementSnapshot(FrameworkElement element)
    {
        var width = Math.Max(1, (int)Math.Ceiling(element.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(element.ActualHeight));
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        var dpi = VisualTreeHelper.GetDpi(element);
        var bitmap = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(width * dpi.DpiScaleX)),
            Math.Max(1, (int)Math.Ceiling(height * dpi.DpiScaleY)),
            96d * dpi.DpiScaleX,
            96d * dpi.DpiScaleY,
            PixelFormats.Pbgra32);

        bitmap.Render(element);
        bitmap.Freeze();
        return bitmap;
    }

    private void FreezeDashboardHeaderHost(double minHeight)
    {
        if (minHeight > 0d)
        {
            DashboardHeaderHost.MinHeight = minHeight;
        }
    }

    private void ReleaseDashboardHeaderHostFreeze()
    {
        DashboardHeaderHost.MinHeight = 0d;
    }

    private void FreezeDashboardPageHost(double minWidth, double minHeight)
    {
        if (minWidth > 0d)
        {
            DashboardPageHost.MinWidth = minWidth;
        }

        if (minHeight > 0d)
        {
            DashboardPageHost.MinHeight = minHeight;
        }
    }

    private void ReleaseDashboardPageHostFreeze()
    {
        DashboardPageHost.MinWidth = 0d;
        DashboardPageHost.MinHeight = 0d;
    }

    private double ResolveDashboardPageOffset()
    {
        var hostHeight = DashboardPageHost.ActualHeight;
        return Math.Max(DashboardPageSwitchOffset, hostHeight * 0.12d);
    }

    private void StopHeaderAnimation()
    {
        DashboardHeaderRoot.BeginAnimation(OpacityProperty, null);
        DashboardHeaderTransitionOverlay.BeginAnimation(OpacityProperty, null);
        GetTranslateTransform(DashboardHeaderRoot).BeginAnimation(TranslateTransform.YProperty, null);
        GetTranslateTransform(DashboardHeaderTransitionOverlay).BeginAnimation(TranslateTransform.YProperty, null);
    }

    private void StopHeaderHistoryContentAnimation()
    {
        DashboardHistoryHeaderContent.BeginAnimation(OpacityProperty, null);
        DashboardHistoryHeaderContent.BeginAnimation(FrameworkElement.MaxHeightProperty, null);
        GetTranslateTransform(DashboardHistoryHeaderContent).BeginAnimation(TranslateTransform.YProperty, null);
    }

    private void StopHeaderContainerAnimation()
    {
        DashboardHeaderHost.BeginAnimation(FrameworkElement.HeightProperty, null);
    }

    private static void StopAuxiliaryHeaderAnimation(UIElement element)
    {
        element.BeginAnimation(OpacityProperty, null);
        GetTranslateTransform(element).BeginAnimation(TranslateTransform.YProperty, null);
    }

    private static void ApplyHeaderAuxiliaryState(UIElement element, bool isVisible, double offsetY)
    {
        element.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        element.IsHitTestVisible = isVisible;
        element.Opacity = isVisible ? 1d : 0d;
        SetTranslateY(element, offsetY);
    }

    private void AnimateHeaderAuxiliaryTransition(
        UIElement element,
        bool shouldShow,
        double incomingOffset,
        double outgoingOffset)
    {
        var duration = TimeSpan.FromMilliseconds(DashboardHeaderSwitchDurationMilliseconds);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };

        if (shouldShow)
        {
            element.Visibility = Visibility.Visible;
            element.IsHitTestVisible = false;
            element.Opacity = 0d;
            SetTranslateY(element, incomingOffset);

            var opacityAnimation = new DoubleAnimation(0d, 1d, duration) { EasingFunction = easing };
            opacityAnimation.Completed += (_, _) =>
            {
                element.IsHitTestVisible = true;
                element.Opacity = 1d;
                SetTranslateY(element, 0d);
            };

            element.BeginAnimation(OpacityProperty, opacityAnimation, HandoffBehavior.SnapshotAndReplace);
            GetTranslateTransform(element).BeginAnimation(
                TranslateTransform.YProperty,
                new DoubleAnimation(incomingOffset, 0d, duration) { EasingFunction = easing },
                HandoffBehavior.SnapshotAndReplace);
            return;
        }

        element.Visibility = Visibility.Visible;
        element.IsHitTestVisible = false;
        element.Opacity = 1d;
        SetTranslateY(element, 0d);

        var exitOpacityAnimation = new DoubleAnimation(1d, 0d, duration) { EasingFunction = easing };
        exitOpacityAnimation.Completed += (_, _) =>
        {
            element.Visibility = Visibility.Collapsed;
            element.IsHitTestVisible = false;
            element.Opacity = 0d;
            SetTranslateY(element, 0d);
        };

        element.BeginAnimation(OpacityProperty, exitOpacityAnimation, HandoffBehavior.SnapshotAndReplace);
        GetTranslateTransform(element).BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(0d, outgoingOffset, duration) { EasingFunction = easing },
            HandoffBehavior.SnapshotAndReplace);
    }

    private void AnimateHeaderMenuTransition(bool shouldShow)
    {
        var duration = TimeSpan.FromMilliseconds(DashboardHeaderSwitchDurationMilliseconds);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };

        if (shouldShow)
        {
            OverviewModeMenu.Visibility = Visibility.Visible;
            OverviewModeMenu.IsHitTestVisible = false;
            OverviewModeMenu.Opacity = 0d;
            SetTranslateY(OverviewModeMenu, 0d);

            var opacityAnimation = new DoubleAnimation(0d, 1d, duration) { EasingFunction = easing };
            opacityAnimation.Completed += (_, _) =>
            {
                OverviewModeMenu.IsHitTestVisible = true;
                OverviewModeMenu.Opacity = 1d;
            };

            OverviewModeMenu.BeginAnimation(OpacityProperty, opacityAnimation, HandoffBehavior.SnapshotAndReplace);
            return;
        }

        OverviewModeMenu.Visibility = Visibility.Visible;
        OverviewModeMenu.IsHitTestVisible = false;
        OverviewModeMenu.Opacity = 1d;
        SetTranslateY(OverviewModeMenu, 0d);

        var exitOpacityAnimation = new DoubleAnimation(1d, 0d, duration) { EasingFunction = easing };
        exitOpacityAnimation.Completed += (_, _) =>
        {
            OverviewModeMenu.Visibility = Visibility.Collapsed;
            OverviewModeMenu.IsHitTestVisible = false;
            OverviewModeMenu.Opacity = 0d;
        };

        OverviewModeMenu.BeginAnimation(OpacityProperty, exitOpacityAnimation, HandoffBehavior.SnapshotAndReplace);
    }

    private double ResolveHistoryHeaderContentExpandedHeight()
    {
        DashboardHistoryHeaderContent.UpdateLayout();
        return Math.Max(1d, DashboardHistoryHeaderContent.ActualHeight);
    }

    private void AnimateHeaderContainerHeight(double fromHeight, double toHeight)
    {
        DashboardHeaderHost.Height = fromHeight;

        var duration = TimeSpan.FromMilliseconds(DashboardHeaderSwitchDurationMilliseconds);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var heightAnimation = new DoubleAnimation(fromHeight, toHeight, duration) { EasingFunction = easing };
        heightAnimation.Completed += (_, _) =>
        {
            DashboardHeaderHost.Height = double.NaN;
        };

        DashboardHeaderHost.BeginAnimation(
            FrameworkElement.HeightProperty,
            heightAnimation,
            HandoffBehavior.SnapshotAndReplace);
    }

    private double ResolveDashboardHeaderHeight(bool isHistoryActive)
    {
        var historyVisibility = DashboardHistoryHeaderContent.Visibility;
        var historyHitTest = DashboardHistoryHeaderContent.IsHitTestVisible;
        var historyOpacity = DashboardHistoryHeaderContent.Opacity;
        var historyMaxHeight = DashboardHistoryHeaderContent.MaxHeight;
        var historyOffset = GetTranslateY(DashboardHistoryHeaderContent);

        var realtimeVisibility = RealtimeOverviewHeaderPanel.Visibility;
        var realtimeHitTest = RealtimeOverviewHeaderPanel.IsHitTestVisible;
        var realtimeOpacity = RealtimeOverviewHeaderPanel.Opacity;
        var realtimeOffset = GetTranslateY(RealtimeOverviewHeaderPanel);

        var menuVisibility = OverviewModeMenu.Visibility;
        var menuHitTest = OverviewModeMenu.IsHitTestVisible;
        var menuOpacity = OverviewModeMenu.Opacity;
        var menuOffset = GetTranslateY(OverviewModeMenu);

        var calendarVisibility = HistoryCalendarCard.Visibility;
        var calendarHitTest = HistoryCalendarCard.IsHitTestVisible;
        var calendarOpacity = HistoryCalendarCard.Opacity;
        var calendarOffset = GetTranslateY(HistoryCalendarCard);

        ApplyHeaderAuxiliaryState(OverviewModeMenu, !isHistoryActive, 0d);
        ApplyHeaderAuxiliaryState(RealtimeOverviewHeaderPanel, !isHistoryActive, 0d);
        ApplyHeaderAuxiliaryState(HistoryCalendarCard, isHistoryActive, 0d);
        DashboardHistoryHeaderContent.Visibility = isHistoryActive ? Visibility.Visible : Visibility.Collapsed;
        DashboardHistoryHeaderContent.IsHitTestVisible = isHistoryActive;
        DashboardHistoryHeaderContent.Opacity = isHistoryActive ? 1d : 0d;
        DashboardHistoryHeaderContent.MaxHeight = isHistoryActive ? double.PositiveInfinity : 0d;
        SetTranslateY(DashboardHistoryHeaderContent, 0d);

        DashboardHeaderRoot.UpdateLayout();
        var measuredHeight = Math.Max(1d, DashboardHeaderRoot.ActualHeight);

        DashboardHistoryHeaderContent.Visibility = historyVisibility;
        DashboardHistoryHeaderContent.IsHitTestVisible = historyHitTest;
        DashboardHistoryHeaderContent.Opacity = historyOpacity;
        DashboardHistoryHeaderContent.MaxHeight = historyMaxHeight;
        SetTranslateY(DashboardHistoryHeaderContent, historyOffset);

        RealtimeOverviewHeaderPanel.Visibility = realtimeVisibility;
        RealtimeOverviewHeaderPanel.IsHitTestVisible = realtimeHitTest;
        RealtimeOverviewHeaderPanel.Opacity = realtimeOpacity;
        SetTranslateY(RealtimeOverviewHeaderPanel, realtimeOffset);

        OverviewModeMenu.Visibility = menuVisibility;
        OverviewModeMenu.IsHitTestVisible = menuHitTest;
        OverviewModeMenu.Opacity = menuOpacity;
        SetTranslateY(OverviewModeMenu, menuOffset);

        HistoryCalendarCard.Visibility = calendarVisibility;
        HistoryCalendarCard.IsHitTestVisible = calendarHitTest;
        HistoryCalendarCard.Opacity = calendarOpacity;
        SetTranslateY(HistoryCalendarCard, calendarOffset);

        DashboardHeaderRoot.UpdateLayout();
        return measuredHeight;
    }

    private static TranslateTransform GetTranslateTransform(UIElement element)
    {
        if (element.RenderTransform is not TranslateTransform transform)
        {
            transform = new TranslateTransform();
            element.RenderTransform = transform;
        }

        return transform;
    }

    private static double GetTranslateY(UIElement element)
    {
        return GetTranslateTransform(element).Y;
    }

    private static void SetTranslateY(UIElement element, double value)
    {
        GetTranslateTransform(element).Y = value;
    }

    private void EnsureHistoryPageLoaded()
    {
        if (_isHistoryPageLoaded)
        {
            return;
        }

        _isHistoryPageLoaded = true;
        HistoryPageHost.Content = new HistoryPageView
        {
            DataContext = DataContext
        };
    }

    private void ResetHistoryPageScrollPosition()
    {
        if (HistoryPageHost.Content is HistoryPageView historyPageView)
        {
            historyPageView.ResetScrollPosition();
        }
    }

    private void ReleaseHistoryPage()
    {
        if (!_isHistoryPageLoaded)
        {
            return;
        }

        if (HistoryPageHost.Content is FrameworkElement historyView)
        {
            historyView.DataContext = null;
        }

        HistoryPageHost.Content = null;
        _isHistoryPageLoaded = false;
    }

    private void UpdateRenderingActiveState()
    {
        if (DataContext is ViewModels.DashboardViewModel viewModel)
        {
            viewModel.SetMainWindowRenderingActive(ShouldRenderWindow());
        }
    }

    private bool ShouldRenderWindow() => IsVisible && WindowState != WindowState.Minimized;
}
