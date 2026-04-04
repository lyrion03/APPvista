using System.ComponentModel;
using System.Windows.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace WinFormsApp1.Desktop;

public partial class MainWindow : Window
{
    private INotifyPropertyChanged? _notifyingContext;
    private bool? _lastHistoryPageActive;
    private bool? _lastHistoryNetworkSplitMode;
    private bool? _lastHistoryIoSplitMode;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        DataContextChanged += OnDataContextChanged;
        Activated += OnActivated;
        Deactivated += OnDeactivated;
        Closing += OnClosing;
    }

    public MainWindow(object viewModel) : this()
    {
        DataContext = viewModel;
    }

    protected override void OnClosed(EventArgs e)
    {
        Closing -= OnClosing;

        if (_notifyingContext is not null)
        {
            _notifyingContext.PropertyChanged -= OnViewModelPropertyChanged;
            _notifyingContext = null;
        }

        base.OnClosed(e);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.DashboardViewModel viewModel)
        {
            viewModel.SetMainWindowRenderingActive(IsActive);
            UpdateApplicationCardViewportWidth();
        }

        var isHistoryActive = ReadHistoryPageState();
        var isHistoryNetworkSplitMode = ReadHistoryNetworkSplitMode();
        var isHistoryIoSplitMode = ReadHistoryIoSplitMode();
        _lastHistoryPageActive = isHistoryActive;
        _lastHistoryNetworkSplitMode = isHistoryNetworkSplitMode;
        _lastHistoryIoSplitMode = isHistoryIoSplitMode;
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
            viewModel.SetMainWindowRenderingActive(IsActive);
            UpdateApplicationCardViewportWidth();
        }

        var isHistoryActive = ReadHistoryPageState();
        var isHistoryNetworkSplitMode = ReadHistoryNetworkSplitMode();
        var isHistoryIoSplitMode = ReadHistoryIoSplitMode();
        _lastHistoryPageActive = isHistoryActive;
        _lastHistoryNetworkSplitMode = isHistoryNetworkSplitMode;
        _lastHistoryIoSplitMode = isHistoryIoSplitMode;
        ApplyPageAnimation(isHistoryActive, animate: false);
        ApplySwitchIndicatorAnimation(isHistoryActive, animate: false);
        ApplyHistoryNetworkSwitchIndicatorAnimation(isHistoryNetworkSplitMode, animate: false);
        ApplyHistoryIoSwitchIndicatorAnimation(isHistoryIoSplitMode, animate: false);
    }

    private void OnActivated(object? sender, EventArgs e)
    {
        if (DataContext is ViewModels.DashboardViewModel viewModel)
        {
            viewModel.SetMainWindowRenderingActive(true);
        }
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (DataContext is ViewModels.DashboardViewModel viewModel)
        {
            viewModel.SetMainWindowRenderingActive(false);
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (System.Windows.Application.Current is not App app || app.IsExitRequested)
        {
            return;
        }

        e.Cancel = true;
        ShowInTaskbar = false;
        Hide();

        if (DataContext is ViewModels.DashboardViewModel viewModel)
        {
            viewModel.SetMainWindowRenderingActive(false);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ViewModels.DashboardViewModel.IsHistoryPageActive) or nameof(ViewModels.DashboardViewModel.IsRealtimePageActive))
        {
            Dispatcher.BeginInvoke(() =>
            {
                var isHistoryActive = ReadHistoryPageState();
                var shouldAnimate = _lastHistoryPageActive != isHistoryActive;
                _lastHistoryPageActive = isHistoryActive;
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
        _lastHistoryPageActive = false;
        ApplySwitchIndicatorAnimation(isHistoryActive: false, animate: true);
    }

    private void HistorySwitchButton_OnClick(object sender, RoutedEventArgs e)
    {
        _lastHistoryPageActive = true;
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
        var enteringPage = isHistoryActive ? HistoryPageRoot : RealtimePageRoot;
        var exitingPage = isHistoryActive ? RealtimePageRoot : HistoryPageRoot;
        var startOffset = isHistoryActive ? 28d : -28d;

        StopPageAnimation(RealtimePageRoot);
        StopPageAnimation(HistoryPageRoot);

        exitingPage.Visibility = Visibility.Collapsed;
        exitingPage.Opacity = 0;
        exitingPage.IsHitTestVisible = false;
        System.Windows.Controls.Panel.SetZIndex(exitingPage, 0);
        SetTranslateX(exitingPage, 0);

        enteringPage.Visibility = Visibility.Visible;
        enteringPage.IsHitTestVisible = true;
        enteringPage.Opacity = 1;
        System.Windows.Controls.Panel.SetZIndex(enteringPage, 2);

        if (!animate)
        {
            SetTranslateX(enteringPage, 0);
            return;
        }

        enteringPage.Opacity = 0;
        SetTranslateX(enteringPage, startOffset);

        var duration = TimeSpan.FromMilliseconds(220);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        enteringPage.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = duration,
                EasingFunction = easing
            });

        if (enteringPage.RenderTransform is TranslateTransform transform)
        {
            transform.BeginAnimation(
                TranslateTransform.XProperty,
                new DoubleAnimation
                {
                    From = startOffset,
                    To = 0,
                    Duration = duration,
                    EasingFunction = easing
                });
        }
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
        if (element.RenderTransform is TranslateTransform transform)
        {
            transform.BeginAnimation(TranslateTransform.XProperty, null);
        }
    }

    private static void SetTranslateX(UIElement element, double value)
    {
        if (element.RenderTransform is TranslateTransform transform)
        {
            transform.X = value;
        }
    }
}
