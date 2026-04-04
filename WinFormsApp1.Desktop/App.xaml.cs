using System.IO;
using System.Windows;
using Forms = System.Windows.Forms;
using WinFormsApp1.Application.Abstractions;
using WinFormsApp1.Desktop.Services;
using WinFormsApp1.Desktop.ViewModels;
using WinFormsApp1.Infrastructure.Persistence;
using WinFormsApp1.Infrastructure.Services;

namespace WinFormsApp1.Desktop;

public partial class App : System.Windows.Application
{
    private const string ApplicationDisplayName = "APPvista";
    private const string AutoStartAppName = "APPvista";
    private const string AppDataFolderName = "WinFormsApp1.Desktop";
    private IProcessNetworkUsageSource? _networkUsageSource;
    private IMonitoringDashboardService? _dashboardService;
    private Forms.NotifyIcon? _trayIcon;
    private Forms.ToolStripMenuItem? _autoStartMenuItem;
    private AutoStartPreferenceStore? _autoStartPreferenceStore;
    private AutoStartRegistrationService? _autoStartRegistrationService;
    private bool _isExitRequested;
    private bool _isAutoStartEnabled;

    public bool IsExitRequested => _isExitRequested;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var dataDirectory = ResolveDataDirectory();
        var iconCacheDirectory = Path.Combine(dataDirectory, "icon-cache");
        var databasePath = Path.Combine(dataDirectory, "monitoring.db");
        var whitelistPath = Path.Combine(dataDirectory, "process-whitelist.json");
        var applicationAliasPath = Path.Combine(dataDirectory, "application-aliases.json");
        var applicationCardMetricPreferencePath = Path.Combine(dataDirectory, "application-card-metrics.json");
        var windowedOnlyRecordingPath = Path.Combine(dataDirectory, "windowed-only-recording.json");
        var autoStartPreferencePath = Path.Combine(dataDirectory, "auto-start.json");

        IProcessSnapshotProvider processSnapshotProvider = new ProcessSnapshotProvider();
        IWhitelistStore whitelistStore = new SqliteWhitelistStore(databasePath, whitelistPath);
        IDailyProcessActivityStore dailyProcessActivityStore = new SqliteDailyProcessActivityStore(databasePath);
        _networkUsageSource = new TraceEventProcessNetworkUsageSource();

        var applicationIconCache = new ApplicationIconCache(iconCacheDirectory);
        var applicationAliasStore = new ApplicationAliasStore(applicationAliasPath);
        var applicationCardMetricPreferenceStore = new ApplicationCardMetricPreferenceStore(applicationCardMetricPreferencePath);
        var applicationCardMetricPreferences = new ApplicationCardMetricPreferences(applicationCardMetricPreferenceStore.Load());
        var windowedOnlyRecordingStore = new WindowedOnlyRecordingStore(windowedOnlyRecordingPath);
        _autoStartPreferenceStore = new AutoStartPreferenceStore(autoStartPreferencePath);
        _autoStartRegistrationService = new AutoStartRegistrationService(
            AutoStartAppName,
            Environment.ProcessPath ?? AppContext.BaseDirectory);
        _isAutoStartEnabled = _autoStartPreferenceStore.Load();
        _autoStartRegistrationService.SetEnabled(_isAutoStartEnabled);

        var isWindowedOnlyRecording = windowedOnlyRecordingStore.Load();
        var detailDisplayPreferences = new DetailDisplayPreferences();
        _dashboardService = new LiveMonitoringDashboardService(
            processSnapshotProvider,
            whitelistStore,
            dailyProcessActivityStore,
            _networkUsageSource,
            isWindowedOnlyRecording);
        var viewModel = new DashboardViewModel(
            _dashboardService,
            whitelistStore,
            applicationIconCache,
            applicationAliasStore,
            applicationCardMetricPreferenceStore,
            applicationCardMetricPreferences,
            windowedOnlyRecordingStore,
            detailDisplayPreferences,
            databasePath);

        var mainWindow = new MainWindow(viewModel);
        InitializeTrayIcon();

        MainWindow = mainWindow;
        mainWindow.Show();
    }

    public void ShowMainWindowFromTray()
    {
        if (MainWindow is null)
        {
            return;
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
        _trayIcon?.Dispose();
        _trayIcon = null;
        MainWindow?.Close();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        _trayIcon = null;

        if (_dashboardService is IDisposable disposableDashboardService)
        {
            disposableDashboardService.Dispose();
        }

        _dashboardService = null;
        _networkUsageSource?.Dispose();
        _networkUsageSource = null;
        base.OnExit(e);
    }

    private void InitializeTrayIcon()
    {
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
        var appDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppDataFolderName);
        Directory.CreateDirectory(appDataDirectory);

        var legacyDataDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "data"));
        TryMigrateLegacyData(legacyDataDirectory, appDataDirectory);

        return appDataDirectory;
    }

    private static void TryMigrateLegacyData(string legacyDataDirectory, string appDataDirectory)
    {
        if (string.Equals(legacyDataDirectory, appDataDirectory, StringComparison.OrdinalIgnoreCase) ||
            !Directory.Exists(legacyDataDirectory))
        {
            return;
        }

        CopyFileIfMissing(legacyDataDirectory, appDataDirectory, "monitoring.db");
        CopyFileIfMissing(legacyDataDirectory, appDataDirectory, "process-whitelist.json");
        CopyFileIfMissing(legacyDataDirectory, appDataDirectory, "application-aliases.json");
        CopyFileIfMissing(legacyDataDirectory, appDataDirectory, "application-card-metrics.json");
        CopyFileIfMissing(legacyDataDirectory, appDataDirectory, "windowed-only-recording.json");
        CopyFileIfMissing(legacyDataDirectory, appDataDirectory, "auto-start.json");
        CopyDirectoryIfMissing(
            Path.Combine(legacyDataDirectory, "icon-cache"),
            Path.Combine(appDataDirectory, "icon-cache"));
    }

    private static void CopyFileIfMissing(string sourceDirectory, string targetDirectory, string fileName)
    {
        var sourcePath = Path.Combine(sourceDirectory, fileName);
        var targetPath = Path.Combine(targetDirectory, fileName);
        if (!File.Exists(sourcePath) || File.Exists(targetPath))
        {
            return;
        }

        File.Copy(sourcePath, targetPath, overwrite: false);
    }

    private static void CopyDirectoryIfMissing(string sourceDirectory, string targetDirectory)
    {
        if (!Directory.Exists(sourceDirectory) || Directory.Exists(targetDirectory))
        {
            return;
        }

        Directory.CreateDirectory(targetDirectory);
        foreach (var sourceFile in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, sourceFile);
            var targetFile = Path.Combine(targetDirectory, relativePath);
            var targetFileDirectory = Path.GetDirectoryName(targetFile);
            if (!string.IsNullOrWhiteSpace(targetFileDirectory))
            {
                Directory.CreateDirectory(targetFileDirectory);
            }

            File.Copy(sourceFile, targetFile, overwrite: false);
        }
    }
}
