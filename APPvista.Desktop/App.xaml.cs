using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using APPvista.Application.Abstractions;
using APPvista.Desktop.Services;
using APPvista.Desktop.ViewModels;
using APPvista.Infrastructure.Persistence;
using APPvista.Infrastructure.Services;

namespace APPvista.Desktop;

public partial class App : System.Windows.Application
{
    private const string ApplicationDisplayName = "APPvista";
    private const string AutoStartAppName = "APPvista";
    private const string TrayLaunchArgument = "--tray";
    private string? _dataDirectory;
    private string? _databasePath;
    private IProcessSnapshotProvider? _processSnapshotProvider;
    private IWhitelistStore? _whitelistStore;
    private IDailyProcessActivityStore? _dailyProcessActivityStore;
    private IProcessNetworkUsageSource? _networkUsageSource;
    private IMonitoringDashboardService? _dashboardService;
    private ApplicationIconCache? _applicationIconCache;
    private ApplicationAliasStore? _applicationAliasStore;
    private ApplicationCardMetricPreferenceStore? _applicationCardMetricPreferenceStore;
    private ApplicationCardMetricPreferences? _applicationCardMetricPreferences;
    private WindowedOnlyRecordingStore? _windowedOnlyRecordingStore;
    private DetailDisplayPreferences? _detailDisplayPreferences;
    private Forms.NotifyIcon? _trayIcon;
    private Forms.ContextMenuStrip? _trayMenu;
    private Forms.ToolStripMenuItem? _autoStartMenuItem;
    private AutoStartPreferenceStore? _autoStartPreferenceStore;
    private AutoStartRegistrationService? _autoStartRegistrationService;
    private bool _isExitRequested;
    private bool _isAutoStartEnabled;

    public bool IsExitRequested => _isExitRequested;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var startInTrayOnly = e.Args.Any(static arg => string.Equals(arg, TrayLaunchArgument, StringComparison.OrdinalIgnoreCase));

        var startupTimestamp = Stopwatch.GetTimestamp();
        var dataDirectory = ResolveDataDirectory();
        StartupPerformanceTrace.Initialize(dataDirectory);
        StartupPerformanceTrace.Mark("App.OnStartup entered");
        _dataDirectory = dataDirectory;

        var iconCacheDirectory = Path.Combine(dataDirectory, "icon-cache");
        var databasePath = Path.Combine(dataDirectory, "monitoring.db");
        _databasePath = databasePath;
        var whitelistPath = Path.Combine(dataDirectory, "process-whitelist.json");
        var applicationAliasPath = Path.Combine(dataDirectory, "application-aliases.json");
        var applicationCardMetricPreferencePath = Path.Combine(dataDirectory, "application-card-metrics.json");
        var windowedOnlyRecordingPath = Path.Combine(dataDirectory, "windowed-only-recording.json");
        var autoStartPreferencePath = Path.Combine(dataDirectory, "auto-start.json");

        var processSnapshotProviderStarted = Stopwatch.GetTimestamp();
        _processSnapshotProvider =
            new TracingProcessSnapshotProvider(new ProcessSnapshotProvider());
        StartupPerformanceTrace.MarkDuration("ProcessSnapshotProvider created", processSnapshotProviderStarted);

        var whitelistStoreStarted = Stopwatch.GetTimestamp();
        _whitelistStore = new SqliteWhitelistStore(databasePath, whitelistPath);
        StartupPerformanceTrace.MarkDuration("SqliteWhitelistStore created", whitelistStoreStarted);

        var dailyStoreStarted = Stopwatch.GetTimestamp();
        _dailyProcessActivityStore = new SqliteDailyProcessActivityStore(databasePath);
        StartupPerformanceTrace.MarkDuration("SqliteDailyProcessActivityStore created", dailyStoreStarted);

        var networkUsageStarted = Stopwatch.GetTimestamp();
        _networkUsageSource = new DeferredProcessNetworkUsageSource(
            static () => new TraceEventProcessNetworkUsageSource());
        StartupPerformanceTrace.MarkDuration("DeferredProcessNetworkUsageSource created", networkUsageStarted);

        var iconCacheStarted = Stopwatch.GetTimestamp();
        _applicationIconCache = new ApplicationIconCache(iconCacheDirectory);
        StartupPerformanceTrace.MarkDuration("ApplicationIconCache created", iconCacheStarted);

        var aliasStoreStarted = Stopwatch.GetTimestamp();
        _applicationAliasStore = new ApplicationAliasStore(applicationAliasPath);
        StartupPerformanceTrace.MarkDuration("ApplicationAliasStore created", aliasStoreStarted);

        var metricStoreStarted = Stopwatch.GetTimestamp();
        _applicationCardMetricPreferenceStore = new ApplicationCardMetricPreferenceStore(applicationCardMetricPreferencePath);
        _applicationCardMetricPreferences = new ApplicationCardMetricPreferences(_applicationCardMetricPreferenceStore.Load());
        StartupPerformanceTrace.MarkDuration("ApplicationCardMetricPreferenceStore loaded", metricStoreStarted);

        var recordingStoreStarted = Stopwatch.GetTimestamp();
        _windowedOnlyRecordingStore = new WindowedOnlyRecordingStore(windowedOnlyRecordingPath);
        StartupPerformanceTrace.MarkDuration("WindowedOnlyRecordingStore created", recordingStoreStarted);

        var autoStartStoreStarted = Stopwatch.GetTimestamp();
        _autoStartPreferenceStore = new AutoStartPreferenceStore(autoStartPreferencePath);
        StartupPerformanceTrace.MarkDuration("AutoStartPreferenceStore created", autoStartStoreStarted);

        var autoStartRegistrationStarted = Stopwatch.GetTimestamp();
        _autoStartRegistrationService = new AutoStartRegistrationService(
            AutoStartAppName,
            Environment.ProcessPath ?? AppContext.BaseDirectory);
        StartupPerformanceTrace.MarkDuration("AutoStartRegistrationService created", autoStartRegistrationStarted);

        var autoStartLoadStarted = Stopwatch.GetTimestamp();
        _isAutoStartEnabled = _autoStartPreferenceStore.Load();
        StartupPerformanceTrace.MarkDuration("AutoStart preference loaded", autoStartLoadStarted);

        var autoStartApplyStarted = Stopwatch.GetTimestamp();
        _autoStartRegistrationService.SetEnabled(_isAutoStartEnabled);
        StartupPerformanceTrace.MarkDuration("AutoStart registration applied", autoStartApplyStarted);

        _detailDisplayPreferences = new DetailDisplayPreferences();
        var dashboardServiceStarted = Stopwatch.GetTimestamp();
        var isWindowedOnlyRecording = _windowedOnlyRecordingStore.Load();
        _dashboardService = new TracingMonitoringDashboardService(
            new LiveMonitoringDashboardService(
                _processSnapshotProvider,
                _whitelistStore,
                _dailyProcessActivityStore,
                _networkUsageSource,
                isWindowedOnlyRecording));
        StartupPerformanceTrace.MarkDuration("LiveMonitoringDashboardService created", dashboardServiceStarted);

        Dispatcher.BeginInvoke(new Action(() =>
        {
            var trayStarted = Stopwatch.GetTimestamp();
            InitializeTrayIcon();
            StartupPerformanceTrace.MarkDuration("Tray icon initialized", trayStarted);
        }), DispatcherPriority.Background);

        if (startInTrayOnly)
        {
            StartupPerformanceTrace.Mark("MainWindow deferred for tray launch");
        }
        else
        {
            var windowStarted = Stopwatch.GetTimestamp();
            ShowMainWindowFromTray();
            StartupPerformanceTrace.MarkDuration("MainWindow created", windowStarted);
            StartupPerformanceTrace.Mark("MainWindow shown");
        }

        StartupPerformanceTrace.MarkDuration("App.OnStartup completed", startupTimestamp);
    }

    public void ShowMainWindowFromTray()
    {
        if (MainWindow is null)
        {
            MainWindow = CreateMainWindow();
        }

        MainWindow.ShowInTaskbar = true;
        MainWindow.Show();
        if (MainWindow.WindowState == WindowState.Minimized)
        {
            MainWindow.WindowState = WindowState.Normal;
        }

        MainWindow.Activate();
    }

    public void ExitFromTray()
    {
        _isExitRequested = true;
        DisposeTrayIcon();
        MainWindow?.Close();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DisposeTrayIcon();

        if (_dashboardService is IDisposable disposableDashboardService)
        {
            disposableDashboardService.Dispose();
        }

        _dashboardService = null;
        _networkUsageSource?.Dispose();
        _networkUsageSource = null;
        base.OnExit(e);
    }

    internal void ReleaseMainWindow()
    {
        DashboardViewModel? releasedViewModel = null;
        var process = Process.GetCurrentProcess();
        var managedBefore = GC.GetTotalMemory(forceFullCollection: false);
        var privateBefore = process.PrivateMemorySize64;
        var workingSetBefore = process.WorkingSet64;

        if (MainWindow is MainWindow window)
        {
            releasedViewModel = window.DataContext as DashboardViewModel;
            window.DataContext = null;
        }

        releasedViewModel?.ReleaseDetachedUiState();
        MainWindow = null;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        process.Refresh();
        StartupPerformanceTrace.Mark(
            $"Tray release memory | managed_before={managedBefore / 1024d / 1024d:F1}MB managed_after={GC.GetTotalMemory(forceFullCollection: false) / 1024d / 1024d:F1}MB private_before={privateBefore / 1024d / 1024d:F1}MB private_after={process.PrivateMemorySize64 / 1024d / 1024d:F1}MB ws_before={workingSetBefore / 1024d / 1024d:F1}MB ws_after={process.WorkingSet64 / 1024d / 1024d:F1}MB");
    }

    private MainWindow CreateMainWindow()
    {
        if (_dashboardService is null ||
            _whitelistStore is null ||
            _applicationIconCache is null ||
            _applicationAliasStore is null ||
            _applicationCardMetricPreferenceStore is null ||
            _applicationCardMetricPreferences is null ||
            _windowedOnlyRecordingStore is null ||
            _detailDisplayPreferences is null ||
            string.IsNullOrWhiteSpace(_databasePath))
        {
            throw new InvalidOperationException("Application services are not initialized.");
        }

        var viewModel = new DashboardViewModel(
            _dashboardService,
            _whitelistStore,
            _applicationIconCache,
            _applicationAliasStore,
            _applicationCardMetricPreferenceStore,
            _applicationCardMetricPreferences,
            _windowedOnlyRecordingStore,
            _detailDisplayPreferences,
            _databasePath);

        return new MainWindow(viewModel);
    }

    private void InitializeTrayIcon()
    {
        if (_trayIcon is not null)
        {
            if (_autoStartMenuItem is not null)
            {
                _autoStartMenuItem.Checked = _isAutoStartEnabled;
            }

            return;
        }

        DisposeTrayIcon();
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("打开主窗口", null, (_, _) => ShowMainWindowFromTray());

        _autoStartMenuItem = new Forms.ToolStripMenuItem("开机自启动")
        {
            Checked = _isAutoStartEnabled,
            CheckOnClick = false
        };
        _autoStartMenuItem.Click += (_, _) => ToggleAutoStart();
        menu.Items.Add(_autoStartMenuItem);

        menu.Items.Add("退出", null, (_, _) => ExitFromTray());

        _trayMenu = menu;
        _trayIcon = new Forms.NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = ApplicationDisplayName,
            Visible = true,
            ContextMenuStrip = menu
        };

        _trayIcon.DoubleClick += (_, _) => ShowMainWindowFromTray();
    }

    private static System.Drawing.Icon LoadTrayIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "APPvista.ico");
        return File.Exists(iconPath)
            ? new System.Drawing.Icon(iconPath)
            : System.Drawing.SystemIcons.Application;
    }

    private void DisposeTrayIcon()
    {
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        _autoStartMenuItem?.Dispose();
        _autoStartMenuItem = null;
        _trayMenu?.Dispose();
        _trayMenu = null;
    }

    private void ToggleAutoStart()
    {
        var nextEnabled = !_isAutoStartEnabled;
        _autoStartRegistrationService?.SetEnabled(nextEnabled);
        _autoStartPreferenceStore?.Save(nextEnabled);
        _isAutoStartEnabled = _autoStartRegistrationService?.IsEnabled() ?? nextEnabled;

        if (_autoStartMenuItem is not null)
        {
            _autoStartMenuItem.Checked = _isAutoStartEnabled;
        }
    }

    private static string ResolveDataDirectory()
    {
        var dataDirectory = Path.Combine(AppContext.BaseDirectory, "data");
        Directory.CreateDirectory(dataDirectory);
        return dataDirectory;
    }
}
