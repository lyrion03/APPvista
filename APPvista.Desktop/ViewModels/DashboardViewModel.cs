using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using APPvista.Application.Abstractions;
using APPvista.Desktop.Services;
using APPvista.Domain.Entities;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Point = System.Windows.Point;

namespace APPvista.Desktop.ViewModels;

public sealed partial class DashboardViewModel : ObservableObject
{
    private const string SortByName = "名称";
    private const string SortByNetwork = "当前网速";
    private const string SortByTraffic = "总流量";
    private const string SortByCpu = "CPU";
    private const string SortByMemory = "内存";
    private const string SortByRealtimeIo = "实时 I/O";
    private const string SortByIo = "I/O总量";
    private const string SortByThreadCount = "线程数";
    private const string SortByThread = "线程峰均比";
    private const string SortByFocus = "前台时长";
    private const int OverviewChartCapacity = 32;
    private const double OverviewChartWidth = 104d;
    private const double OverviewChartHeight = 95d;
    private const double OverviewPeakLabelWidth = 72d;
    private const double ApplicationCardHorizontalSpacing = 16d;
    private const double DefaultApplicationCardViewportWidth = 1280d;
    private static readonly RefreshIntervalOption RefreshEvery1Second = new("1 秒", TimeSpan.FromSeconds(1));
    private static readonly RefreshIntervalOption RefreshEvery2Seconds = new("2 秒", TimeSpan.FromSeconds(2));
    private static readonly RefreshIntervalOption RefreshEvery5Seconds = new("5 秒", TimeSpan.FromSeconds(5));
    private static readonly RefreshIntervalOption RefreshEvery10Seconds = new("10 秒", TimeSpan.FromSeconds(10));
    private static readonly Brush NetworkDownloadBrush = CreateFrozenBrush("#6CB8FF");
    private static readonly Brush NetworkUploadBrush = CreateFrozenBrush("#FFAE72");
    private static readonly Brush NetworkTotalBrush = CreateFrozenBrush("#9AD0BF");
    private static readonly Brush IoReadBrush = CreateFrozenBrush("#5EB2A4");
    private static readonly Brush IoWriteBrush = CreateFrozenBrush("#E7A075");
    private static readonly Brush IoTotalBrush = CreateFrozenBrush("#8FD4B9");

    public readonly record struct RefreshIntervalOption(string Label, TimeSpan Interval)
    {
        public override string ToString() => Label;
    }

    private enum NetworkDisplayMode
    {
        Hidden,
        Total,
        Split
    }

    private enum IoDisplayMode
    {
        Hidden,
        Total,
        Split
    }

    private enum ForegroundBackgroundDisplayMode
    {
        Hidden,
        Visible
    }

    private enum OverviewDataSourceMode
    {
        ApplicationSum,
        SystemTotals
    }

    private readonly IMonitoringDashboardService _dashboardService;
    private readonly IBlacklistStore _blacklistStore;
    private readonly ApplicationIconCache _applicationIconCache;
    private readonly ApplicationAliasStore _applicationAliasStore;
    private readonly ApplicationCardMetricPreferenceStore _applicationCardMetricPreferenceStore;
    private readonly ApplicationCardMetricPreferences _applicationCardMetricPreferences;
    private readonly WindowedOnlyRecordingStore _windowedOnlyRecordingStore;
    private readonly DetailDisplayPreferences _detailDisplayPreferences;
    private SystemOverviewProvider? _systemOverviewProvider;
    private readonly HistoryAnalysisProvider _historyAnalysisProvider;
    private readonly string _databasePath;
    private readonly DispatcherTimer _refreshTimer;
    private readonly Dictionary<string, string> _applicationAliases;
    private readonly Dictionary<string, ApplicationDetailWindow> _openDetailWindows;
    private APPvista.Desktop.HistoryComparisonWindow? _historyComparisonWindow;
    private readonly List<double> _networkDownloadHistory = [];
    private readonly List<double> _networkUploadHistory = [];
    private readonly List<double> _ioReadHistory = [];
    private readonly List<double> _ioWriteHistory = [];
    private DashboardSnapshot _snapshot;
    private SystemOverviewSnapshot _systemOverviewSnapshot;
    private bool _isBlacklistPopupOpen;
    private bool _isSortPopupOpen;
    private bool _isCustomMetricPopupOpen;
    private bool _isWindowedOnlyRecordingConfirmOpen;
    private bool _updatingBlacklistCandidates;
    private bool _isRefreshing;
    private bool _pendingRefresh;
    private bool _pendingRefreshWithSort;
    private bool _hasStartedLoading;
    private Task? _systemOverviewInitializationTask;
    private string _selectedSortOption = SortByFocus;
    private RefreshIntervalOption _selectedRefreshInterval;
    private NetworkDisplayMode _networkDisplayMode = NetworkDisplayMode.Total;
    private IoDisplayMode _ioDisplayMode = IoDisplayMode.Total;
    private ForegroundBackgroundDisplayMode _foregroundBackgroundDisplayMode = ForegroundBackgroundDisplayMode.Hidden;
    private OverviewDataSourceMode _overviewDataSourceMode = OverviewDataSourceMode.ApplicationSum;
    private bool _isWindowedOnlyRecording;
    private bool _isMainWindowRenderingActive = true;
    private bool _hasDeferredMainWindowRefresh;
    private bool _pendingBlacklistCandidateRefresh;
    private double _applicationCardViewportWidth = DefaultApplicationCardViewportWidth;
    private PointCollection _networkMiniTotalPoints = [];
    private PointCollection _networkMiniDownloadPoints = [];
    private PointCollection _networkMiniUploadPoints = [];
    private PointCollection _ioMiniTotalPoints = [];
    private PointCollection _ioMiniReadPoints = [];
    private PointCollection _ioMiniWritePoints = [];
    private PeakMarkerInfo _networkPeakMarker = new(0, OverviewChartHeight - 1d, "峰 0 B/s", Brushes.Transparent, 0);
    private PeakMarkerInfo _ioPeakMarker = new(0, OverviewChartHeight - 1d, "峰 0 B/s", Brushes.Transparent, 0);

    public DashboardViewModel(
        IMonitoringDashboardService dashboardService,
        IBlacklistStore blacklistStore,
        ApplicationIconCache applicationIconCache,
        ApplicationAliasStore applicationAliasStore,
        ApplicationCardMetricPreferenceStore applicationCardMetricPreferenceStore,
        ApplicationCardMetricPreferences applicationCardMetricPreferences,
        WindowedOnlyRecordingStore windowedOnlyRecordingStore,
        DetailDisplayPreferences detailDisplayPreferences,
        string databasePath)
    {
        _dashboardService = dashboardService;
        _blacklistStore = blacklistStore;
        _applicationIconCache = applicationIconCache;
        _applicationAliasStore = applicationAliasStore;
        _applicationCardMetricPreferenceStore = applicationCardMetricPreferenceStore;
        _applicationCardMetricPreferences = applicationCardMetricPreferences;
        _windowedOnlyRecordingStore = windowedOnlyRecordingStore;
        _detailDisplayPreferences = detailDisplayPreferences;
        _databasePath = databasePath;
        _historyAnalysisProvider = new HistoryAnalysisProvider(databasePath, blacklistStore);
        _applicationAliases = new Dictionary<string, string>(_applicationAliasStore.Load(), StringComparer.OrdinalIgnoreCase);
        _openDetailWindows = new Dictionary<string, ApplicationDetailWindow>(StringComparer.OrdinalIgnoreCase);
        _snapshot = new DashboardSnapshot();
        _systemOverviewSnapshot = new SystemOverviewSnapshot();
        _isWindowedOnlyRecording = _dashboardService.IsWindowedOnlyRecording;

        Applications = new ObservableCollection<ApplicationCardViewModel>();
        ApplicationRows = new ObservableCollection<ApplicationCardRowViewModel>();
        CustomMetricGroups = [];
        BlacklistCandidates = new ObservableCollection<BlacklistCandidateViewModel>();
        HistoryCalendarDays = new ObservableCollection<HistoryCalendarDayViewModel>();
        HistoryTrafficTopApplications = new ObservableCollection<HistoryRankingItemViewModel>();
        HistoryIoTopApplications = new ObservableCollection<HistoryRankingItemViewModel>();
        HistoryForegroundTopApplications = new ObservableCollection<HistoryRankingItemViewModel>();
        SortOptions =
        [
            SortByName,
            SortByFocus,
            SortByNetwork,
            SortByTraffic,
            SortByCpu,
            SortByMemory,
            SortByRealtimeIo,
            SortByIo,
            SortByThreadCount,
            SortByThread
        ];

        RefreshIntervalOptions =
        [
            RefreshEvery1Second,
            RefreshEvery2Seconds,
            RefreshEvery5Seconds,
            RefreshEvery10Seconds
        ];
        _selectedRefreshInterval = RefreshEvery2Seconds;
        _applicationCardMetricPreferences.PropertyChanged += OnApplicationCardMetricPreferencesPropertyChanged;

        RefreshCommand = new RelayCommand(() => _ = RefreshAsync(shouldSort: true));
        ToggleSortPopupCommand = new RelayCommand(ToggleSortPopup);
        ToggleBlacklistPopupCommand = new RelayCommand(ToggleBlacklistPopup);
        ToggleCustomMetricPopupCommand = new RelayCommand(ToggleCustomMetricPopup);
        SaveCustomMetricSelectionCommand = new RelayCommand(SaveCustomMetricSelection);
        ResetCustomMetricSelectionCommand = new RelayCommand(ResetCustomMetricSelection);
        CancelCustomMetricSelectionCommand = new RelayCommand(CancelCustomMetricSelection);
        SetSortByNameCommand = new RelayCommand(() => SelectedSortOption = SortByName);
        SetSortByFocusCommand = new RelayCommand(() => SelectedSortOption = SortByFocus);
        SetSortByNetworkCommand = new RelayCommand(() => SelectedSortOption = SortByNetwork);
        SetSortByTrafficCommand = new RelayCommand(() => SelectedSortOption = SortByTraffic);
        SetSortByCpuCommand = new RelayCommand(() => SelectedSortOption = SortByCpu);
        SetSortByMemoryCommand = new RelayCommand(() => SelectedSortOption = SortByMemory);
        SetSortByRealtimeIoCommand = new RelayCommand(() => SelectedSortOption = SortByRealtimeIo);
        SetSortByIoCommand = new RelayCommand(() => SelectedSortOption = SortByIo);
        SetSortByThreadCountCommand = new RelayCommand(() => SelectedSortOption = SortByThreadCount);
        SetSortByThreadCommand = new RelayCommand(() => SelectedSortOption = SortByThread);
        SetRefreshInterval1SecondCommand = new RelayCommand(() => SelectedRefreshInterval = RefreshEvery1Second);
        SetRefreshInterval2SecondsCommand = new RelayCommand(() => SelectedRefreshInterval = RefreshEvery2Seconds);
        SetRefreshInterval5SecondsCommand = new RelayCommand(() => SelectedRefreshInterval = RefreshEvery5Seconds);
        SetRefreshInterval10SecondsCommand = new RelayCommand(() => SelectedRefreshInterval = RefreshEvery10Seconds);
        SetNetworkHiddenDisplayCommand = new RelayCommand(SetNetworkHiddenDisplay);
        SetNetworkTotalDisplayCommand = new RelayCommand(SetNetworkTotalDisplay);
        SetNetworkSplitDisplayCommand = new RelayCommand(SetNetworkSplitDisplay);
        SetIoHiddenDisplayCommand = new RelayCommand(SetIoHiddenDisplay);
        SetIoTotalDisplayCommand = new RelayCommand(SetIoTotalDisplay);
        SetIoSplitDisplayCommand = new RelayCommand(SetIoSplitDisplay);
        SetApplicationSumDataSourceCommand = new RelayCommand(SetApplicationSumDataSource);
        SetSystemTotalsDataSourceCommand = new RelayCommand(SetSystemTotalsDataSource);
        EnableWindowedOnlyRecordingCommand = new RelayCommand(PromptEnableWindowedOnlyRecording);
        DisableWindowedOnlyRecordingCommand = new RelayCommand(DisableWindowedOnlyRecording);
        ConfirmEnableWindowedOnlyRecordingCommand = new RelayCommand(ConfirmEnableWindowedOnlyRecording);
        CancelEnableWindowedOnlyRecordingCommand = new RelayCommand(CancelEnableWindowedOnlyRecording);
        SetForegroundBackgroundHiddenDisplayCommand = new RelayCommand(SetForegroundBackgroundHiddenDisplay);
        SetForegroundBackgroundVisibleDisplayCommand = new RelayCommand(SetForegroundBackgroundVisibleDisplay);
        ShowRealtimePageCommand = new RelayCommand(ShowRealtimePage);
        ShowHistoryPageCommand = new RelayCommand(ShowHistoryPage);
        SetHistoryDayDimensionCommand = new RelayCommand(SetHistoryDayDimension);
        SetHistoryWeekDimensionCommand = new RelayCommand(SetHistoryWeekDimension);
        SetHistoryMonthDimensionCommand = new RelayCommand(SetHistoryMonthDimension);
        SetHistoryCustomDimensionCommand = new RelayCommand(SetHistoryCustomDimension);
        ShowPreviousHistoryMonthCommand = new RelayCommand(ShowPreviousHistoryMonth);
        ShowNextHistoryMonthCommand = new RelayCommand(ShowNextHistoryMonth);
        SetHistoryNetworkTotalDisplayCommand = new RelayCommand(SetHistoryNetworkTotalDisplay);
        SetHistoryNetworkSplitDisplayCommand = new RelayCommand(SetHistoryNetworkSplitDisplay);
        SetHistoryIoTotalDisplayCommand = new RelayCommand(SetHistoryIoTotalDisplay);
        SetHistoryIoSplitDisplayCommand = new RelayCommand(SetHistoryIoSplitDisplay);
        ShowHistoryComparisonCommand = new RelayCommand(OpenHistoryComparisonWindow);

        _refreshTimer = new DispatcherTimer
        {
            Interval = _selectedRefreshInterval.Interval
        };
        _refreshTimer.Tick += (_, _) => _ = RefreshAsync(shouldSort: false);

        SyncCustomMetricSelection();
    }

    public DashboardSnapshot Snapshot
    {
        get => _snapshot;
        private set
        {
            if (SetProperty(ref _snapshot, value))
            {
                RaiseLiveDisplayStateChanged();
            }
        }
    }

    public ObservableCollection<ApplicationCardViewModel> Applications { get; }
    public ObservableCollection<ApplicationCardRowViewModel> ApplicationRows { get; }
    public ObservableCollection<ApplicationCardMetricGroupViewModel> CustomMetricGroups { get; }
    public ObservableCollection<BlacklistCandidateViewModel> BlacklistCandidates { get; }
    public ObservableCollection<string> SortOptions { get; }
    public ObservableCollection<RefreshIntervalOption> RefreshIntervalOptions { get; }

    public bool IsBlacklistPopupOpen
    {
        get => _isBlacklistPopupOpen;
        set => SetProperty(ref _isBlacklistPopupOpen, value);
    }

    public bool IsCustomMetricPopupOpen
    {
        get => _isCustomMetricPopupOpen;
        set => SetProperty(ref _isCustomMetricPopupOpen, value);
    }

    public bool IsSortPopupOpen
    {
        get => _isSortPopupOpen;
        set => SetProperty(ref _isSortPopupOpen, value);
    }

    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set => SetProperty(ref _isRefreshing, value);
    }

    public bool IsWindowedOnlyRecordingConfirmOpen
    {
        get => _isWindowedOnlyRecordingConfirmOpen;
        set => SetProperty(ref _isWindowedOnlyRecordingConfirmOpen, value);
    }

    public string SelectedSortOption
    {
        get => _selectedSortOption;
        set
        {
            if (SetProperty(ref _selectedSortOption, value))
            {
                RaiseSortModeProperties();
                RaisePropertyChanged(nameof(SortMenuDisplay));
                ApplySorting();
                IsSortPopupOpen = false;
            }
        }
    }

    public RefreshIntervalOption SelectedRefreshInterval
    {
        get => _selectedRefreshInterval;
        set
        {
            if (SetProperty(ref _selectedRefreshInterval, value))
            {
                _refreshTimer.Interval = value.Interval;
                RaisePropertyChanged(nameof(RefreshIntervalDisplay));
                RaisePropertyChanged(nameof(RefreshIntervalMenuDisplay));
                RaiseRefreshIntervalModeProperties();
            }
        }
    }

    public string HeaderTimestamp => DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    public string SummarySubtitle => $"{Snapshot.CollectionStatus} · {Snapshot.NetworkCaptureStatus}";
    public string TotalSpeedDisplay => FormatBytesPerSecond(GetRealtimeDownloadBytesPerSecond() + GetRealtimeUploadBytesPerSecond());
    public string TotalDownloadDisplay => FormatBytes(GetTodayDownloadBytes());
    public string TotalUploadDisplay => FormatBytes(GetTodayUploadBytes());
    public string NetworkOverviewTitle => _overviewDataSourceMode == OverviewDataSourceMode.SystemTotals ? "系统网络总量" : "应用网络总量";
    public string NetworkTotalVolumeDisplay => FormatBytes(GetTodayDownloadBytes() + GetTodayUploadBytes());
    public string NetworkDownloadSpeedDisplay => FormatBytesPerSecond(GetRealtimeDownloadBytesPerSecond());
    public string NetworkUploadSpeedDisplay => FormatBytesPerSecond(GetRealtimeUploadBytesPerSecond());
    public string IoOverviewTitle => _overviewDataSourceMode == OverviewDataSourceMode.SystemTotals ? "系统磁盘 I/O" : "应用请求 I/O";
    public string TotalIoSpeedDisplay => FormatBytesPerSecond(GetRealtimeIoReadBytesPerSecond() + GetRealtimeIoWriteBytesPerSecond());
    public string TotalIoVolumeDisplay => FormatBytes(GetTodayIoReadBytes() + GetTodayIoWriteBytes());
    public string IoReadSpeedDisplay => FormatBytesPerSecond(GetRealtimeIoReadBytesPerSecond());
    public string IoWriteSpeedDisplay => FormatBytesPerSecond(GetRealtimeIoWriteBytesPerSecond());
    public string IoReadVolumeDisplay => FormatBytes(GetTodayIoReadBytes());
    public string IoWriteVolumeDisplay => FormatBytes(GetTodayIoWriteBytes());
    public string ActiveApplicationsDisplay => Snapshot.ActiveProcessCount.ToString(CultureInfo.InvariantCulture);
    public string DashboardStatusTip => $"{Snapshot.StorageStatus}\n{Snapshot.DailyActivityStatus}";
    public string ViewModeDisplay => $"数据来源 {GetDataSourceModeLabel()} · 记录 {GetRecordingScopeLabel()} · 网络 {GetNetworkModeLabel()} · IO {GetIoModeLabel()}";
    public string TrafficSummaryDisplay => BuildTrafficSummary();
    public string IoSummaryDisplay => BuildIoSummary();
    public string RefreshIntervalDisplay => SelectedRefreshInterval.Label;
    public string SortMenuDisplay => $"排序：{SelectedSortOption}";
    public string RefreshIntervalMenuDisplay => $"刷新周期：{RefreshIntervalDisplay}";
    public int SelectedCustomMetricCount => CustomMetricGroups.SelectMany(static group => group.Options).Count(static option => option.IsSelected);
    public string CustomMetricSelectionSummary =>
        $"已选 {SelectedCustomMetricCount} 项，请选择 {ApplicationCardMetricPreferences.MinimumSelectedMetricCount}-{ApplicationCardMetricPreferences.MaximumSelectedMetricCount} 项";
    public bool CanSaveCustomMetricSelection =>
        SelectedCustomMetricCount >= ApplicationCardMetricPreferences.MinimumSelectedMetricCount &&
        SelectedCustomMetricCount <= ApplicationCardMetricPreferences.MaximumSelectedMetricCount;
    public PointCollection NetworkMiniTotalPoints => _networkMiniTotalPoints;
    public PointCollection NetworkMiniDownloadPoints => _networkMiniDownloadPoints;
    public PointCollection NetworkMiniUploadPoints => _networkMiniUploadPoints;
    public PointCollection IoMiniTotalPoints => _ioMiniTotalPoints;
    public PointCollection IoMiniReadPoints => _ioMiniReadPoints;
    public PointCollection IoMiniWritePoints => _ioMiniWritePoints;
    public double NetworkMiniPeakLeft => _networkPeakMarker.Left;
    public double NetworkMiniPeakTop => _networkPeakMarker.Top;
    public string NetworkMiniPeakLabel => _networkPeakMarker.Label;
    public Brush NetworkMiniPeakBrush => _networkPeakMarker.Brush;
    public double IoMiniPeakLeft => _ioPeakMarker.Left;
    public double IoMiniPeakTop => _ioPeakMarker.Top;
    public string IoMiniPeakLabel => _ioPeakMarker.Label;
    public Brush IoMiniPeakBrush => _ioPeakMarker.Brush;
    public bool IsSortByNameMode => SelectedSortOption == SortByName;
    public bool IsSortByFocusMode => SelectedSortOption == SortByFocus;
    public bool IsSortByNetworkMode => SelectedSortOption == SortByNetwork;
    public bool IsSortByTrafficMode => SelectedSortOption == SortByTraffic;
    public bool IsSortByCpuMode => SelectedSortOption == SortByCpu;
    public bool IsSortByMemoryMode => SelectedSortOption == SortByMemory;
    public bool IsSortByRealtimeIoMode => SelectedSortOption == SortByRealtimeIo;
    public bool IsSortByIoMode => SelectedSortOption == SortByIo;
    public bool IsSortByThreadCountMode => SelectedSortOption == SortByThreadCount;
    public bool IsSortByThreadMode => SelectedSortOption == SortByThread;
    public bool IsRefreshInterval1SecondMode => SelectedRefreshInterval.Equals(RefreshEvery1Second);
    public bool IsRefreshInterval2SecondsMode => SelectedRefreshInterval.Equals(RefreshEvery2Seconds);
    public bool IsRefreshInterval5SecondsMode => SelectedRefreshInterval.Equals(RefreshEvery5Seconds);
    public bool IsRefreshInterval10SecondsMode => SelectedRefreshInterval.Equals(RefreshEvery10Seconds);

    public bool IsNetworkHiddenMode => _networkDisplayMode == NetworkDisplayMode.Hidden;
    public bool IsNetworkTotalMode => _networkDisplayMode == NetworkDisplayMode.Total;
    public bool IsNetworkSplitMode => _networkDisplayMode == NetworkDisplayMode.Split;
    public bool IsIoHiddenMode => _ioDisplayMode == IoDisplayMode.Hidden;
    public bool IsIoTotalMode => _ioDisplayMode == IoDisplayMode.Total;
    public bool IsIoSplitMode => _ioDisplayMode == IoDisplayMode.Split;
    public bool IsNetworkSummaryTotalMode => _networkDisplayMode != NetworkDisplayMode.Split;
    public bool IsNetworkSummarySplitMode => _networkDisplayMode == NetworkDisplayMode.Split;
    public bool IsIoSummaryTotalMode => _ioDisplayMode != IoDisplayMode.Split;
    public bool IsIoSummarySplitMode => _ioDisplayMode == IoDisplayMode.Split;
    public bool IsApplicationSumDataSourceMode => _overviewDataSourceMode == OverviewDataSourceMode.ApplicationSum;
    public bool IsSystemTotalsDataSourceMode => _overviewDataSourceMode == OverviewDataSourceMode.SystemTotals;
    public bool IsAllApplicationsRecordingMode => !_isWindowedOnlyRecording;
    public bool IsWindowedOnlyRecordingMode => _isWindowedOnlyRecording;
    public bool IsForegroundBackgroundHiddenMode => _foregroundBackgroundDisplayMode == ForegroundBackgroundDisplayMode.Hidden;
    public bool IsForegroundBackgroundVisibleMode => _foregroundBackgroundDisplayMode == ForegroundBackgroundDisplayMode.Visible;

    public ICommand RefreshCommand { get; }
    public ICommand ToggleSortPopupCommand { get; }
    public ICommand ToggleCustomMetricPopupCommand { get; }
    public ICommand SaveCustomMetricSelectionCommand { get; }
    public ICommand ResetCustomMetricSelectionCommand { get; }
    public ICommand CancelCustomMetricSelectionCommand { get; }
    public ICommand ToggleBlacklistPopupCommand { get; }
    public ICommand SetSortByNameCommand { get; }
    public ICommand SetSortByFocusCommand { get; }
    public ICommand SetSortByNetworkCommand { get; }
    public ICommand SetSortByTrafficCommand { get; }
    public ICommand SetSortByCpuCommand { get; }
    public ICommand SetSortByMemoryCommand { get; }
    public ICommand SetSortByRealtimeIoCommand { get; }
    public ICommand SetSortByIoCommand { get; }
    public ICommand SetSortByThreadCountCommand { get; }
    public ICommand SetSortByThreadCommand { get; }
    public ICommand SetRefreshInterval1SecondCommand { get; }
    public ICommand SetRefreshInterval2SecondsCommand { get; }
    public ICommand SetRefreshInterval5SecondsCommand { get; }
    public ICommand SetRefreshInterval10SecondsCommand { get; }
    public ICommand SetNetworkHiddenDisplayCommand { get; }
    public ICommand SetNetworkTotalDisplayCommand { get; }
    public ICommand SetNetworkSplitDisplayCommand { get; }
    public ICommand SetIoHiddenDisplayCommand { get; }
    public ICommand SetIoTotalDisplayCommand { get; }
    public ICommand SetIoSplitDisplayCommand { get; }
    public ICommand SetApplicationSumDataSourceCommand { get; }
    public ICommand SetSystemTotalsDataSourceCommand { get; }
    public ICommand EnableWindowedOnlyRecordingCommand { get; }
    public ICommand DisableWindowedOnlyRecordingCommand { get; }
    public ICommand ConfirmEnableWindowedOnlyRecordingCommand { get; }
    public ICommand CancelEnableWindowedOnlyRecordingCommand { get; }
    public ICommand SetForegroundBackgroundHiddenDisplayCommand { get; }
    public ICommand SetForegroundBackgroundVisibleDisplayCommand { get; }

    public void SetMainWindowRenderingActive(bool isActive)
    {
        if (_isMainWindowRenderingActive == isActive)
        {
            return;
        }

        _isMainWindowRenderingActive = isActive;
        if (isActive)
        {
            FlushDeferredMainWindowRefresh();
        }
    }

    public void StartBackgroundInitialization()
    {
        if (_hasStartedLoading)
        {
            return;
        }

        _hasStartedLoading = true;
        RefreshHistoryCalendar();
        EnsureSystemOverviewProviderInitialized();
        _refreshTimer.Start();
        _ = RefreshAsync(shouldSort: true);
    }

    public void PrepareForTrayClose()
    {
        SetMainWindowRenderingActive(false);
        _refreshTimer.Stop();
        _pendingRefresh = false;
        _pendingRefreshWithSort = false;
        _hasDeferredMainWindowRefresh = false;
        _pendingBlacklistCandidateRefresh = false;
        _hasStartedLoading = false;
        _historyAnalysisLoadVersion++;
        _systemOverviewInitializationTask = null;

        _applicationCardMetricPreferences.PropertyChanged -= OnApplicationCardMetricPreferencesPropertyChanged;

        foreach (var detailWindow in _openDetailWindows.Values.ToList())
        {
            detailWindow.Close();
        }

        _openDetailWindows.Clear();
    }

    public void ReleaseDetachedUiState()
    {
        _applicationAliases.Clear();
        foreach (var application in Applications)
        {
            application.Dispose();
        }

        Applications.Clear();
        ApplicationRows.Clear();
        CustomMetricGroups.Clear();
        BlacklistCandidates.Clear();
        HistoryCalendarDays.Clear();
        HistoryTrafficTopApplications.Clear();
        HistoryIoTopApplications.Clear();
        HistoryForegroundTopApplications.Clear();
        _networkDownloadHistory.Clear();
        _networkUploadHistory.Clear();
        _ioReadHistory.Clear();
        _ioWriteHistory.Clear();
        _historyCalendarRecords = [];
        _historySummary = HistoryResourceSummary.Empty;
        _historyAverageApplicationCount = 0;
        _snapshot = new DashboardSnapshot();
        _systemOverviewSnapshot = new SystemOverviewSnapshot();
        _isBlacklistPopupOpen = false;
        _isCustomMetricPopupOpen = false;
        _isWindowedOnlyRecordingConfirmOpen = false;
    }

    private void OnApplicationCardMetricPreferencesPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        RebuildApplicationRows();
    }

    private void EnsureSystemOverviewProviderInitialized()
    {
        if (_systemOverviewProvider is not null || _systemOverviewInitializationTask is not null)
        {
            return;
        }

        _systemOverviewInitializationTask = Task.Run(() =>
        {
            var provider = new SystemOverviewProvider(_databasePath);
            provider.Capture(includeRealtime: false);
            return provider;
        }).ContinueWith(task =>
        {
            _systemOverviewInitializationTask = null;
            if (task.Status != TaskStatus.RanToCompletion)
            {
                return;
            }

            _systemOverviewProvider = task.Result;
            _systemOverviewSnapshot = task.Result.Capture(includeRealtime: _overviewDataSourceMode == OverviewDataSourceMode.SystemTotals);
            _ = RefreshAsync(shouldSort: false);
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private SystemOverviewSnapshot CaptureSystemOverviewSnapshot(bool includeRealtime)
    {
        var provider = _systemOverviewProvider;
        if (provider is null)
        {
            return _systemOverviewSnapshot;
        }

        return provider.Capture(includeRealtime);
    }

    private async Task RefreshAsync(bool shouldSort)
    {
        if (IsRefreshing)
        {
            _pendingRefresh = true;
            _pendingRefreshWithSort |= shouldSort;
            return;
        }

        IsRefreshing = true;

        try
        {
            var nextSort = shouldSort;

            do
            {
                _pendingRefresh = false;
                _pendingRefreshWithSort = false;

                var result = await Task.Run(() =>
                {
                    var dashboardSnapshot = DecorateSnapshot(_dashboardService.GetSnapshot());
                    var systemOverviewSnapshot = CaptureSystemOverviewSnapshot(
                        includeRealtime: _overviewDataSourceMode == OverviewDataSourceMode.SystemTotals);
                    return (dashboardSnapshot, systemOverviewSnapshot);
                });

                _systemOverviewSnapshot = result.systemOverviewSnapshot;
                _snapshot = result.dashboardSnapshot;
                var shouldRenderMainWindow = _isMainWindowRenderingActive;
                var shouldUpdateSharedApplications = shouldRenderMainWindow || HasActiveDetailWindow();

                AppendOverviewSamples(shouldRenderMainWindow && IsRealtimePageActive);

                if (shouldUpdateSharedApplications)
                {
                    RebuildApplications(nextSort);
                }
                else
                {
                    _hasDeferredMainWindowRefresh = true;
                }

                if (shouldRenderMainWindow)
                {
                    RaiseLiveDisplayStateChanged();
                    if (IsBlacklistPopupOpen)
                    {
                        RebuildBlacklistCandidates();
                        _pendingBlacklistCandidateRefresh = false;
                    }
                    else
                    {
                        _pendingBlacklistCandidateRefresh = true;
                    }
                    if (IsHistoryPageActive)
                    {
                        LoadHistoryAnalysis();
                    }

                    RaisePropertyChanged(nameof(HeaderTimestamp));
                }
                else
                {
                    _hasDeferredMainWindowRefresh = true;
                }

                nextSort = _pendingRefreshWithSort;
            }
            while (_pendingRefresh);
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private DashboardSnapshot DecorateSnapshot(DashboardSnapshot snapshot)
    {
        return new DashboardSnapshot
        {
            CollectionStatus = snapshot.CollectionStatus,
            ActiveProcessCount = snapshot.ActiveProcessCount,
            TodayTraffic = snapshot.TodayTraffic,
            RealtimeTraffic = snapshot.RealtimeTraffic,
            TodayDiskIo = snapshot.TodayDiskIo,
            RealtimeDiskIo = snapshot.RealtimeDiskIo,
            NetworkCaptureStatus = snapshot.NetworkCaptureStatus,
            RealtimeDownloadBytesPerSecond = snapshot.RealtimeDownloadBytesPerSecond,
            RealtimeUploadBytesPerSecond = snapshot.RealtimeUploadBytesPerSecond,
            TodayDownloadBytes = snapshot.TodayDownloadBytes,
            TodayUploadBytes = snapshot.TodayUploadBytes,
            RealtimeIoReadBytesPerSecond = snapshot.RealtimeIoReadBytesPerSecond,
            RealtimeIoWriteBytesPerSecond = snapshot.RealtimeIoWriteBytesPerSecond,
            TodayIoReadBytes = snapshot.TodayIoReadBytes,
            TodayIoWriteBytes = snapshot.TodayIoWriteBytes,
            BlacklistCount = snapshot.BlacklistCount,
            StorageStatus = snapshot.StorageStatus,
            DailyActivityStatus = snapshot.DailyActivityStatus,
            TopProcesses = snapshot.TopProcesses.Select(DecorateProcess).ToList()
        };
    }

    private ProcessResourceSnapshot DecorateProcess(ProcessResourceSnapshot process)
    {
        return new ProcessResourceSnapshot
        {
            IconCachePath = _applicationIconCache.GetIconPath(process.ExecutablePath),
            ProcessName = process.ProcessName,
            ProcessId = process.ProcessId,
            ProcessCount = process.ProcessCount,
            ExecutablePath = process.ExecutablePath,
            CpuUsagePercent = process.CpuUsagePercent,
            WorkingSetBytes = process.WorkingSetBytes,
            PeakWorkingSetBytes = process.PeakWorkingSetBytes,
            PrivateMemoryBytes = process.PrivateMemoryBytes,
            CommitSizeBytes = process.CommitSizeBytes,
            ThreadCount = process.ThreadCount,
            AverageThreadCount = process.AverageThreadCount,
            PeakThreadCount = process.PeakThreadCount,
            IsForeground = process.IsForeground,
            DailyForegroundMilliseconds = process.DailyForegroundMilliseconds,
            DailyBackgroundMilliseconds = process.DailyBackgroundMilliseconds,
            AverageForegroundCpu = process.AverageForegroundCpu,
            AverageForegroundWorkingSetBytes = process.AverageForegroundWorkingSetBytes,
            AverageForegroundIops = process.AverageForegroundIops,
            AverageBackgroundCpu = process.AverageBackgroundCpu,
            AverageBackgroundWorkingSetBytes = process.AverageBackgroundWorkingSetBytes,
            AverageBackgroundIops = process.AverageBackgroundIops,
            DailyDownloadBytes = process.DailyDownloadBytes,
            DailyUploadBytes = process.DailyUploadBytes,
            RealtimeDownloadBytesPerSecond = process.RealtimeDownloadBytesPerSecond,
            RealtimeUploadBytesPerSecond = process.RealtimeUploadBytesPerSecond,
            PeakDownloadBytesPerSecond = process.PeakDownloadBytesPerSecond,
            PeakUploadBytesPerSecond = process.PeakUploadBytesPerSecond,
            RealtimeIoReadOpsPerSecond = process.RealtimeIoReadOpsPerSecond,
            RealtimeIoWriteOpsPerSecond = process.RealtimeIoWriteOpsPerSecond,
            RealtimeIoReadBytesPerSecond = process.RealtimeIoReadBytesPerSecond,
            RealtimeIoWriteBytesPerSecond = process.RealtimeIoWriteBytesPerSecond,
            IoReadBytesDelta = process.IoReadBytesDelta,
            IoWriteBytesDelta = process.IoWriteBytesDelta,
            DailyIoReadBytes = process.DailyIoReadBytes,
            DailyIoWriteBytes = process.DailyIoWriteBytes,
            PeakIoReadBytesPerSecond = process.PeakIoReadBytesPerSecond,
            PeakIoWriteBytesPerSecond = process.PeakIoWriteBytesPerSecond,
            PeakIoBytesPerSecond = process.PeakIoBytesPerSecond,
            AverageIops = process.AverageIops
        };
    }

    private void RebuildApplications(bool shouldSort)
    {
        var existing = Applications.ToDictionary(static item => item.OriginalName, StringComparer.OrdinalIgnoreCase);
        var next = new List<ApplicationCardViewModel>();

        foreach (var process in Snapshot.TopProcesses)
        {
            var aliasKey = ApplicationAliasStore.CreateKey(process);
            _applicationAliases.TryGetValue(aliasKey, out var alias);

            if (!existing.TryGetValue(process.ProcessName, out var item))
            {
                item = new ApplicationCardViewModel(process, alias, PersistApplicationAlias, OpenApplicationDetails, _applicationCardMetricPreferences);
            }
            else
            {
                if (HasApplicationCardChanges(item, process, alias))
                {
                    item.Update(process, alias);
                }
            }

            next.Add(item);
        }

        if (Applications.Any(static item => item.IsRenaming))
        {
            return;
        }

        if (shouldSort)
        {
            ReplaceApplications(OrderApplications(next));
            return;
        }

        var currentOrder = Applications
            .Select((item, index) => new { item.OriginalName, Index = index })
            .ToDictionary(static item => item.OriginalName, static item => item.Index, StringComparer.OrdinalIgnoreCase);
        var ordered = next
            .OrderBy(item =>
            {
                return currentOrder.TryGetValue(item.OriginalName, out var index) ? index : int.MaxValue;
            })
            .ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        ReplaceApplications(ordered);
    }

    private IReadOnlyList<ApplicationCardViewModel> OrderApplications(IEnumerable<ApplicationCardViewModel> applications)
    {
        IOrderedEnumerable<ApplicationCardViewModel> ordered = SelectedSortOption switch
        {
            SortByName => applications.OrderBy(static item => item.DisplayName, StringComparer.OrdinalIgnoreCase),
            SortByNetwork => applications.OrderByDescending(static item => item.CurrentBandwidthBytesPerSecond),
            SortByTraffic => applications.OrderByDescending(static item => item.TodayTrafficBytes),
            SortByCpu => applications.OrderByDescending(static item => item.CpuUsagePercent),
            SortByMemory => applications.OrderByDescending(static item => item.WorkingSetBytes),
            SortByRealtimeIo => applications.OrderByDescending(static item => item.CurrentIoBytesPerSecond),
            SortByIo => applications.OrderByDescending(static item => item.DailyIoBytes),
            SortByThreadCount => applications.OrderByDescending(static item => item.ThreadCount),
            SortByThread => applications.OrderByDescending(static item => item.ThreadPressure),
            _ => applications.OrderByDescending(static item => item.ForegroundMilliseconds)
        };

        return ordered
            .ThenByDescending(static item => item.IsForeground)
            .ThenBy(static item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool HasApplicationCardChanges(ApplicationCardViewModel item, ProcessResourceSnapshot nextSnapshot, string? alias)
    {
        var expectedDisplayName = string.IsNullOrWhiteSpace(alias) ? nextSnapshot.ProcessName : alias.Trim();
        if (!string.Equals(item.DisplayName, expectedDisplayName, StringComparison.Ordinal))
        {
            return true;
        }

        var current = item.Snapshot;
        return current.IconCachePath != nextSnapshot.IconCachePath ||
               current.ProcessName != nextSnapshot.ProcessName ||
               current.ProcessId != nextSnapshot.ProcessId ||
               current.ProcessCount != nextSnapshot.ProcessCount ||
               current.ExecutablePath != nextSnapshot.ExecutablePath ||
               current.CpuUsagePercent != nextSnapshot.CpuUsagePercent ||
               current.WorkingSetBytes != nextSnapshot.WorkingSetBytes ||
               current.PeakWorkingSetBytes != nextSnapshot.PeakWorkingSetBytes ||
               current.PrivateMemoryBytes != nextSnapshot.PrivateMemoryBytes ||
               current.CommitSizeBytes != nextSnapshot.CommitSizeBytes ||
               current.ThreadCount != nextSnapshot.ThreadCount ||
               current.AverageThreadCount != nextSnapshot.AverageThreadCount ||
               current.PeakThreadCount != nextSnapshot.PeakThreadCount ||
               current.HasMainWindow != nextSnapshot.HasMainWindow ||
               current.IsForeground != nextSnapshot.IsForeground ||
               current.DailyForegroundMilliseconds != nextSnapshot.DailyForegroundMilliseconds ||
               current.DailyBackgroundMilliseconds != nextSnapshot.DailyBackgroundMilliseconds ||
               current.AverageForegroundCpu != nextSnapshot.AverageForegroundCpu ||
               current.AverageForegroundWorkingSetBytes != nextSnapshot.AverageForegroundWorkingSetBytes ||
               current.AverageForegroundIops != nextSnapshot.AverageForegroundIops ||
               current.AverageBackgroundCpu != nextSnapshot.AverageBackgroundCpu ||
               current.AverageBackgroundWorkingSetBytes != nextSnapshot.AverageBackgroundWorkingSetBytes ||
               current.AverageBackgroundIops != nextSnapshot.AverageBackgroundIops ||
               current.DailyDownloadBytes != nextSnapshot.DailyDownloadBytes ||
               current.DailyUploadBytes != nextSnapshot.DailyUploadBytes ||
               current.RealtimeDownloadBytesPerSecond != nextSnapshot.RealtimeDownloadBytesPerSecond ||
               current.RealtimeUploadBytesPerSecond != nextSnapshot.RealtimeUploadBytesPerSecond ||
               current.PeakDownloadBytesPerSecond != nextSnapshot.PeakDownloadBytesPerSecond ||
               current.PeakUploadBytesPerSecond != nextSnapshot.PeakUploadBytesPerSecond ||
               current.RealtimeIoReadOpsPerSecond != nextSnapshot.RealtimeIoReadOpsPerSecond ||
               current.RealtimeIoWriteOpsPerSecond != nextSnapshot.RealtimeIoWriteOpsPerSecond ||
               current.RealtimeIoReadBytesPerSecond != nextSnapshot.RealtimeIoReadBytesPerSecond ||
               current.RealtimeIoWriteBytesPerSecond != nextSnapshot.RealtimeIoWriteBytesPerSecond ||
               current.IoReadBytesDelta != nextSnapshot.IoReadBytesDelta ||
               current.IoWriteBytesDelta != nextSnapshot.IoWriteBytesDelta ||
               current.DailyIoReadBytes != nextSnapshot.DailyIoReadBytes ||
               current.DailyIoWriteBytes != nextSnapshot.DailyIoWriteBytes ||
               current.PeakIoReadBytesPerSecond != nextSnapshot.PeakIoReadBytesPerSecond ||
               current.PeakIoWriteBytesPerSecond != nextSnapshot.PeakIoWriteBytesPerSecond ||
               current.PeakIoBytesPerSecond != nextSnapshot.PeakIoBytesPerSecond ||
               current.AverageIops != nextSnapshot.AverageIops;
    }

    private void ReplaceApplications(IReadOnlyList<ApplicationCardViewModel> items)
    {
        var itemSet = new HashSet<ApplicationCardViewModel>(items);

        for (var index = Applications.Count - 1; index >= 0; index--)
        {
            if (!itemSet.Contains(Applications[index]))
            {
                Applications.RemoveAt(index);
            }
        }

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            if (index < Applications.Count && ReferenceEquals(Applications[index], item))
            {
                continue;
            }

            var existingIndex = Applications.IndexOf(item);
            if (existingIndex >= 0)
            {
                Applications.Move(existingIndex, index);
            }
            else
            {
                Applications.Insert(index, item);
            }
        }

        while (Applications.Count > items.Count)
        {
            Applications.RemoveAt(Applications.Count - 1);
        }

        RebuildApplicationRows();
    }

    private void ApplySorting()
    {
        ReplaceApplications(OrderApplications(Applications));
    }

    public void SetApplicationCardViewportWidth(double viewportWidth)
    {
        var normalizedWidth = Math.Max(320d, Math.Floor(viewportWidth));
        if (Math.Abs(_applicationCardViewportWidth - normalizedWidth) < 1d)
        {
            return;
        }

        _applicationCardViewportWidth = normalizedWidth;
        RebuildApplicationRows();
    }

    private void RebuildApplicationRows()
    {
        var cardsPerRow = ResolveApplicationCardsPerRow();
        var rows = new List<ApplicationCardRowViewModel>((Applications.Count + cardsPerRow - 1) / cardsPerRow);
        var changed = false;

        for (var index = 0; index < Applications.Count; index += cardsPerRow)
        {
            var rowItemCount = Math.Min(cardsPerRow, Applications.Count - index);
            var rowItems = new List<ApplicationCardViewModel>(rowItemCount);
            for (var itemIndex = 0; itemIndex < rowItemCount; itemIndex++)
            {
                rowItems.Add(Applications[index + itemIndex]);
            }

            rows.Add(new ApplicationCardRowViewModel(rowItems));

            if (!changed)
            {
                var rowIndex = index / cardsPerRow;
                if (rowIndex >= ApplicationRows.Count)
                {
                    changed = true;
                }
                else
                {
                    var existingRow = ApplicationRows[rowIndex].Items;
                    if (existingRow.Count != rowItemCount)
                    {
                        changed = true;
                    }
                    else
                    {
                        for (var itemIndex = 0; itemIndex < rowItemCount; itemIndex++)
                        {
                            if (!ReferenceEquals(existingRow[itemIndex], rowItems[itemIndex]))
                            {
                                changed = true;
                                break;
                            }
                        }
                    }
                }
            }
        }

        if (!changed && ApplicationRows.Count == rows.Count)
        {
            return;
        }

        ApplicationRows.Clear();
        foreach (var row in rows)
        {
            ApplicationRows.Add(row);
        }
    }

    private int ResolveApplicationCardsPerRow()
    {
        var cardWidth = Applications.Count > 0 ? Applications[0].CardWidth : 324d;
        var slotWidth = cardWidth + ApplicationCardHorizontalSpacing;
        return Math.Max(1, (int)Math.Floor((_applicationCardViewportWidth + ApplicationCardHorizontalSpacing) / slotWidth));
    }

    private void RebuildBlacklistCandidates()
    {
        var blacklist = _blacklistStore.Load();
        var displayNamesByProcess = Snapshot.TopProcesses
            .GroupBy(static item => item.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                group => BuildBlacklistDisplayName(group.Key, group.First()),
                StringComparer.OrdinalIgnoreCase);

        var names = Snapshot.TopProcesses
            .Select(static item => item.ProcessName)
            .Concat(blacklist.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var existing = BlacklistCandidates.ToDictionary(static item => item.ProcessName, StringComparer.OrdinalIgnoreCase);
        var next = new List<BlacklistCandidateViewModel>();
        _updatingBlacklistCandidates = true;

        foreach (var name in names)
        {
            blacklist.TryGetValue(name, out var mode);
            BlacklistEntryMode? selectedMode = blacklist.ContainsKey(name) ? mode : null;
            if (!existing.TryGetValue(name, out var candidate))
            {
                candidate = new BlacklistCandidateViewModel(name, selectedMode, OnBlacklistCandidateChanged);
            }
            else
            {
                candidate.UpdateMode(selectedMode);
            }

            candidate.UpdateDisplayName(displayNamesByProcess.TryGetValue(name, out var displayName)
                ? displayName
                : BuildBlacklistDisplayName(name, process: null));

            next.Add(candidate);
        }

        BlacklistCandidates.Clear();
        foreach (var candidate in next)
        {
            BlacklistCandidates.Add(candidate);
        }

        _updatingBlacklistCandidates = false;
    }

    private void OnBlacklistCandidateChanged(BlacklistCandidateViewModel candidate)
    {
        if (_updatingBlacklistCandidates)
        {
            return;
        }

        _blacklistStore.Save(BlacklistCandidates
            .Where(static item => item.Mode.HasValue)
            .Select(item => new BlacklistEntry(item.ProcessName, item.Mode!.Value)));
        _ = RefreshAsync(shouldSort: true);
    }

    private void PersistApplicationAlias(ApplicationCardViewModel application, string? alias)
    {
        if (string.IsNullOrWhiteSpace(alias))
        {
            _applicationAliases.Remove(application.AliasKey);
        }
        else
        {
            _applicationAliases[application.AliasKey] = alias.Trim();
        }

        _applicationAliasStore.Save(_applicationAliases);
        ApplySorting();
        if (IsBlacklistPopupOpen)
        {
            RebuildBlacklistCandidates();
            _pendingBlacklistCandidateRefresh = false;
        }
        else
        {
            _pendingBlacklistCandidateRefresh = true;
        }
    }

    private string BuildBlacklistDisplayName(string processName, ProcessResourceSnapshot? process)
    {
        string? alias = null;

        if (process is not null)
        {
            _applicationAliases.TryGetValue(ApplicationAliasStore.CreateKey(process), out alias);
        }

        alias ??= _applicationAliases.TryGetValue(processName, out var processAlias) ? processAlias : null;

        if (string.IsNullOrWhiteSpace(alias) || string.Equals(alias, processName, StringComparison.OrdinalIgnoreCase))
        {
            return processName;
        }

        return $"{alias}（{processName}）";
    }

    private void OpenApplicationDetails(ApplicationCardViewModel application)
    {
        if (_openDetailWindows.TryGetValue(application.AliasKey, out var existingWindow))
        {
            BringDetailWindowToFront(existingWindow);
            return;
        }

        var detailWindow = new ApplicationDetailWindow(new ApplicationDetailViewModel(application, _detailDisplayPreferences, _databasePath));
        _openDetailWindows[application.AliasKey] = detailWindow;
        detailWindow.Closed += (_, _) => _openDetailWindows.Remove(application.AliasKey);
        detailWindow.Show();
    }

    private static void BringDetailWindowToFront(Window detailWindow)
    {
        if (detailWindow.WindowState == WindowState.Minimized)
        {
            detailWindow.WindowState = WindowState.Normal;
        }

        detailWindow.Show();
        detailWindow.Activate();
        detailWindow.Topmost = true;
        detailWindow.Topmost = false;
        detailWindow.Focus();
    }

    private void ToggleBlacklistPopup()
    {
        if (!IsBlacklistPopupOpen && _pendingBlacklistCandidateRefresh)
        {
            RebuildBlacklistCandidates();
            _pendingBlacklistCandidateRefresh = false;
        }

        IsBlacklistPopupOpen = !IsBlacklistPopupOpen;
    }

    private void ToggleSortPopup()
    {
        IsSortPopupOpen = !IsSortPopupOpen;
    }

    private void ToggleCustomMetricPopup()
    {
        if (!IsCustomMetricPopupOpen)
        {
            SyncCustomMetricSelectionFromCurrentDisplay();
        }

        IsCustomMetricPopupOpen = !IsCustomMetricPopupOpen;
    }

    private void SaveCustomMetricSelection()
    {
        if (!CanSaveCustomMetricSelection)
        {
            return;
        }

        var selectedIds = CustomMetricGroups
            .SelectMany(static group => group.Options)
            .Where(static option => option.IsSelected)
            .Select(static option => option.Id)
            .ToList();

        _applicationCardMetricPreferences.SetSelectedMetricIds(selectedIds);
        _applicationCardMetricPreferenceStore.Save(_applicationCardMetricPreferences.SelectedMetricIds);
        IsCustomMetricPopupOpen = false;
    }

    private void ResetCustomMetricSelection()
    {
        _applicationCardMetricPreferences.SetSelectedMetricIds(ApplicationCardMetricPreferences.DefaultMetricIds);
        _applicationCardMetricPreferenceStore.Save(_applicationCardMetricPreferences.SelectedMetricIds);
        SyncCustomMetricSelectionFromCurrentDisplay();
        IsCustomMetricPopupOpen = false;
    }

    private void CancelCustomMetricSelection()
    {
        SyncCustomMetricSelection();
        IsCustomMetricPopupOpen = false;
    }

    private void SyncCustomMetricSelection()
    {
        SetCustomMetricSelection(_applicationCardMetricPreferences.SelectedMetricIds);
    }

    private void SyncCustomMetricSelectionFromCurrentDisplay()
    {
        // The cards are rendered from _applicationCardMetricPreferences.SelectedMetricIds,
        // so normalize against that source before reflecting the checked state in the popup.
        _applicationCardMetricPreferences.SetSelectedMetricIds(_applicationCardMetricPreferences.SelectedMetricIds);
        SyncCustomMetricSelection();
    }

    private void SetCustomMetricSelection(IEnumerable<string> selectedIds)
    {
        var selectedSet = new HashSet<string>(selectedIds, StringComparer.OrdinalIgnoreCase);
        CustomMetricGroups.Clear();

        foreach (var group in ApplicationCardMetricPreferences.Definitions
                     .GroupBy(static item => item.Category)
                     .Select(group =>
                     {
                         var options = group.Select(item =>
                         {
                             var option = new ApplicationCardMetricOptionViewModel(item.Id, item.Label, OnCustomMetricSelectionChanged);
                             option.SetSelectedSilently(selectedSet.Contains(item.Id));
                             return option;
                         });

                         return new ApplicationCardMetricGroupViewModel(group.Key, options);
                     }))
        {
            CustomMetricGroups.Add(group);
        }

        RaisePropertyChanged(nameof(SelectedCustomMetricCount));
        RaisePropertyChanged(nameof(CustomMetricSelectionSummary));
        RaisePropertyChanged(nameof(CanSaveCustomMetricSelection));
    }

    private void OnCustomMetricSelectionChanged(ApplicationCardMetricOptionViewModel _, bool __)
    {
        RaisePropertyChanged(nameof(SelectedCustomMetricCount));
        RaisePropertyChanged(nameof(CustomMetricSelectionSummary));
        RaisePropertyChanged(nameof(CanSaveCustomMetricSelection));
    }

    private void RaiseSortModeProperties()
    {
        RaisePropertyChanged(nameof(IsSortByNameMode));
        RaisePropertyChanged(nameof(IsSortByFocusMode));
        RaisePropertyChanged(nameof(IsSortByNetworkMode));
        RaisePropertyChanged(nameof(IsSortByTrafficMode));
        RaisePropertyChanged(nameof(IsSortByCpuMode));
        RaisePropertyChanged(nameof(IsSortByMemoryMode));
        RaisePropertyChanged(nameof(IsSortByRealtimeIoMode));
        RaisePropertyChanged(nameof(IsSortByIoMode));
        RaisePropertyChanged(nameof(IsSortByThreadCountMode));
        RaisePropertyChanged(nameof(IsSortByThreadMode));
    }

    private void RaiseRefreshIntervalModeProperties()
    {
        RaisePropertyChanged(nameof(IsRefreshInterval1SecondMode));
        RaisePropertyChanged(nameof(IsRefreshInterval2SecondsMode));
        RaisePropertyChanged(nameof(IsRefreshInterval5SecondsMode));
        RaisePropertyChanged(nameof(IsRefreshInterval10SecondsMode));
    }

    private void SetNetworkHiddenDisplay()
    {
        if (_networkDisplayMode != NetworkDisplayMode.Hidden)
        {
            _networkDisplayMode = NetworkDisplayMode.Hidden;
            RaiseDisplayStateChanged();
        }
    }

    private void SetNetworkTotalDisplay()
    {
        if (_networkDisplayMode != NetworkDisplayMode.Total)
        {
            _networkDisplayMode = NetworkDisplayMode.Total;
            RaiseDisplayStateChanged();
        }
    }

    private void SetNetworkSplitDisplay()
    {
        if (_networkDisplayMode != NetworkDisplayMode.Split)
        {
            _networkDisplayMode = NetworkDisplayMode.Split;
            RaiseDisplayStateChanged();
        }
    }

    private void SetIoHiddenDisplay()
    {
        if (_ioDisplayMode != IoDisplayMode.Hidden)
        {
            _ioDisplayMode = IoDisplayMode.Hidden;
            RaiseDisplayStateChanged();
        }
    }

    private void SetIoTotalDisplay()
    {
        if (_ioDisplayMode != IoDisplayMode.Total)
        {
            _ioDisplayMode = IoDisplayMode.Total;
            RaiseDisplayStateChanged();
        }
    }

    private void SetIoSplitDisplay()
    {
        if (_ioDisplayMode != IoDisplayMode.Split)
        {
            _ioDisplayMode = IoDisplayMode.Split;
            RaiseDisplayStateChanged();
        }
    }

    private void SetApplicationSumDataSource()
    {
        if (_overviewDataSourceMode != OverviewDataSourceMode.ApplicationSum)
        {
            _overviewDataSourceMode = OverviewDataSourceMode.ApplicationSum;
            ResetOverviewChartHistory();
            RaiseDisplayStateChanged();
        }
    }

    private void SetSystemTotalsDataSource()
    {
        if (_overviewDataSourceMode != OverviewDataSourceMode.SystemTotals)
        {
            _overviewDataSourceMode = OverviewDataSourceMode.SystemTotals;
            EnsureSystemOverviewProviderInitialized();
            _systemOverviewSnapshot = CaptureSystemOverviewSnapshot(includeRealtime: true);
            ResetOverviewChartHistory();
            RaiseDisplayStateChanged();
        }
    }

    private void PromptEnableWindowedOnlyRecording()
    {
        if (_isWindowedOnlyRecording)
        {
            return;
        }

        IsWindowedOnlyRecordingConfirmOpen = true;
    }

    private void DisableWindowedOnlyRecording()
    {
        if (!_isWindowedOnlyRecording)
        {
            return;
        }

        _isWindowedOnlyRecording = false;
        _windowedOnlyRecordingStore.Save(false);
        _dashboardService.SetWindowedOnlyRecording(false);
        IsWindowedOnlyRecordingConfirmOpen = false;
        RaiseDisplayStateChanged();
        _ = RefreshAsync(shouldSort: true);
    }

    private void ConfirmEnableWindowedOnlyRecording()
    {
        if (_isWindowedOnlyRecording)
        {
            IsWindowedOnlyRecordingConfirmOpen = false;
            return;
        }

        _isWindowedOnlyRecording = true;
        _windowedOnlyRecordingStore.Save(true);
        _dashboardService.SetWindowedOnlyRecording(true);
        IsWindowedOnlyRecordingConfirmOpen = false;
        RaiseDisplayStateChanged();
        _ = RefreshAsync(shouldSort: true);
    }

    private void CancelEnableWindowedOnlyRecording()
    {
        IsWindowedOnlyRecordingConfirmOpen = false;
    }

    private void SetForegroundBackgroundHiddenDisplay()
    {
        if (_foregroundBackgroundDisplayMode != ForegroundBackgroundDisplayMode.Hidden)
        {
            _foregroundBackgroundDisplayMode = ForegroundBackgroundDisplayMode.Hidden;
            RaiseDisplayStateChanged();
        }
    }

    private void SetForegroundBackgroundVisibleDisplay()
    {
        if (_foregroundBackgroundDisplayMode != ForegroundBackgroundDisplayMode.Visible)
        {
            _foregroundBackgroundDisplayMode = ForegroundBackgroundDisplayMode.Visible;
            RaiseDisplayStateChanged();
        }
    }

    private string BuildTrafficSummary()
    {
        if (_networkDisplayMode == NetworkDisplayMode.Hidden)
        {
            return "已隐藏";
        }

        return _networkDisplayMode == NetworkDisplayMode.Split
            ? $"下载 {FormatBytesPerSecond(GetRealtimeDownloadBytesPerSecond())} / 今日 {FormatBytes(GetTodayDownloadBytes())}\n上传 {FormatBytesPerSecond(GetRealtimeUploadBytesPerSecond())} / 今日 {FormatBytes(GetTodayUploadBytes())}"
            : $"总速 {FormatBytesPerSecond(GetRealtimeDownloadBytesPerSecond() + GetRealtimeUploadBytesPerSecond())}\n总量 {FormatBytes(GetTodayDownloadBytes() + GetTodayUploadBytes())}";
    }

    private string BuildIoSummary()
    {
        if (_ioDisplayMode == IoDisplayMode.Hidden)
        {
            return "已隐藏";
        }

        return _ioDisplayMode == IoDisplayMode.Split
            ? $"读取 {FormatBytesPerSecond(GetRealtimeIoReadBytesPerSecond())} / 今日 {FormatBytes(GetTodayIoReadBytes())}\n写入 {FormatBytesPerSecond(GetRealtimeIoWriteBytesPerSecond())} / 今日 {FormatBytes(GetTodayIoWriteBytes())}"
            : $"总速 {FormatBytesPerSecond(GetRealtimeIoReadBytesPerSecond() + GetRealtimeIoWriteBytesPerSecond())}\n总量 {FormatBytes(GetTodayIoReadBytes() + GetTodayIoWriteBytes())}";
    }

    private long GetRealtimeDownloadBytesPerSecond() =>
        _overviewDataSourceMode == OverviewDataSourceMode.SystemTotals
            ? _systemOverviewSnapshot.RealtimeDownloadBytesPerSecond
            : Snapshot.RealtimeDownloadBytesPerSecond;

    private long GetRealtimeUploadBytesPerSecond() =>
        _overviewDataSourceMode == OverviewDataSourceMode.SystemTotals
            ? _systemOverviewSnapshot.RealtimeUploadBytesPerSecond
            : Snapshot.RealtimeUploadBytesPerSecond;

    private long GetTodayDownloadBytes() =>
        _overviewDataSourceMode == OverviewDataSourceMode.SystemTotals
            ? _systemOverviewSnapshot.TodayDownloadBytes
            : Snapshot.TodayDownloadBytes;

    private long GetTodayUploadBytes() =>
        _overviewDataSourceMode == OverviewDataSourceMode.SystemTotals
            ? _systemOverviewSnapshot.TodayUploadBytes
            : Snapshot.TodayUploadBytes;

    private long GetRealtimeIoReadBytesPerSecond() =>
        _overviewDataSourceMode == OverviewDataSourceMode.SystemTotals
            ? _systemOverviewSnapshot.RealtimeIoReadBytesPerSecond
            : Snapshot.RealtimeIoReadBytesPerSecond;

    private long GetRealtimeIoWriteBytesPerSecond() =>
        _overviewDataSourceMode == OverviewDataSourceMode.SystemTotals
            ? _systemOverviewSnapshot.RealtimeIoWriteBytesPerSecond
            : Snapshot.RealtimeIoWriteBytesPerSecond;

    private long GetTodayIoReadBytes() =>
        _overviewDataSourceMode == OverviewDataSourceMode.SystemTotals
            ? _systemOverviewSnapshot.TodayIoReadBytes
            : Snapshot.TodayIoReadBytes;

    private long GetTodayIoWriteBytes() =>
        _overviewDataSourceMode == OverviewDataSourceMode.SystemTotals
            ? _systemOverviewSnapshot.TodayIoWriteBytes
            : Snapshot.TodayIoWriteBytes;

    private string GetNetworkModeLabel()
    {
        return _networkDisplayMode switch
        {
            NetworkDisplayMode.Hidden => "隐藏",
            NetworkDisplayMode.Split => "上下行",
            _ => "总量"
        };
    }

    private string GetIoModeLabel()
    {
        return _ioDisplayMode switch
        {
            IoDisplayMode.Hidden => "隐藏",
            IoDisplayMode.Split => "读写分离",
            _ => "总量"
        };
    }

    private string GetDataSourceModeLabel()
    {
        return _overviewDataSourceMode == OverviewDataSourceMode.SystemTotals ? "系统总量" : "应用总和";
    }

    private string GetRecordingScopeLabel()
    {
        return _isWindowedOnlyRecording ? "仅窗口应用" : "全部应用";
    }

    private static string FormatBytesPerSecond(long bytesPerSecond)
    {
        return FormatBytes(bytesPerSecond) + "/s";
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return value.ToString(unitIndex == 0 ? "F0" : "F2", CultureInfo.InvariantCulture) + " " + units[unitIndex];
    }

    private bool HasActiveDetailWindow()
    {
        return _openDetailWindows.Values.Any(window => window.IsActive);
    }

    private void FlushDeferredMainWindowRefresh()
    {
        if (!_hasDeferredMainWindowRefresh)
        {
            if (IsRealtimePageActive)
            {
                RaiseOverviewChartProperties();
            }

            if (IsHistoryPageActive)
            {
                LoadHistoryAnalysis();
            }

            return;
        }

        _hasDeferredMainWindowRefresh = false;
        RebuildApplications(shouldSort: true);
        RebuildBlacklistCandidates();
        RaiseDisplayStateChanged();
        if (IsHistoryPageActive)
        {
            LoadHistoryAnalysis();
        }
    }

    private void AppendOverviewSamples(bool notify)
    {
        AppendSample(_networkDownloadHistory, GetRealtimeDownloadBytesPerSecond());
        AppendSample(_networkUploadHistory, GetRealtimeUploadBytesPerSecond());
        AppendSample(_ioReadHistory, GetRealtimeIoReadBytesPerSecond());
        AppendSample(_ioWriteHistory, GetRealtimeIoWriteBytesPerSecond());
        RefreshOverviewChartState();
        if (notify)
        {
            RaiseOverviewChartProperties();
        }
    }

    private void ResetOverviewChartHistory()
    {
        _networkDownloadHistory.Clear();
        _networkUploadHistory.Clear();
        _ioReadHistory.Clear();
        _ioWriteHistory.Clear();
        AppendOverviewSamples(_isMainWindowRenderingActive && IsRealtimePageActive);
    }

    private static void AppendSample(List<double> history, long value)
    {
        history.Add(Math.Max(0, value));
        if (history.Count > OverviewChartCapacity)
        {
            history.RemoveAt(0);
        }
    }

    private void RefreshOverviewChartState()
    {
        _networkMiniDownloadPoints = BuildSparklinePoints(_networkDownloadHistory);
        _networkMiniUploadPoints = BuildSparklinePoints(_networkUploadHistory);
        _networkMiniTotalPoints = BuildCombinedSparklinePoints(_networkDownloadHistory, _networkUploadHistory);
        _ioMiniReadPoints = BuildSparklinePoints(_ioReadHistory);
        _ioMiniWritePoints = BuildSparklinePoints(_ioWriteHistory);
        _ioMiniTotalPoints = BuildCombinedSparklinePoints(_ioReadHistory, _ioWriteHistory);
        _networkPeakMarker = GetNetworkPeakMarker();
        _ioPeakMarker = GetIoPeakMarker();
    }

    private static PointCollection BuildSparklinePoints(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return new PointCollection();
        }

        if (values.Count == 1)
        {
            return new PointCollection
            {
                new Point(0, OverviewChartHeight / 2d),
                new Point(OverviewChartWidth, OverviewChartHeight / 2d)
            };
        }

        var max = 0d;
        for (var index = 0; index < values.Count; index++)
        {
            max = Math.Max(max, values[index]);
        }

        var normalizedMax = max > 0d ? max : 1d;
        var stepX = OverviewChartWidth / (values.Count - 1d);
        var drawableHeight = OverviewChartHeight - 1d;
        var points = new PointCollection(values.Count);

        for (var index = 0; index < values.Count; index++)
        {
            var x = stepX * index;
            var y = drawableHeight - (values[index] / normalizedMax * drawableHeight);
            points.Add(new Point(x, y));
        }

        return points;
    }

    private static PointCollection BuildCombinedSparklinePoints(IReadOnlyList<double> first, IReadOnlyList<double> second)
    {
        var count = Math.Min(first.Count, second.Count);
        if (count == 0)
        {
            return new PointCollection();
        }

        if (count == 1)
        {
            return new PointCollection
            {
                new Point(0, OverviewChartHeight / 2d),
                new Point(OverviewChartWidth, OverviewChartHeight / 2d)
            };
        }

        var max = 0d;
        for (var index = 0; index < count; index++)
        {
            max = Math.Max(max, first[index] + second[index]);
        }

        var normalizedMax = max > 0d ? max : 1d;
        var stepX = OverviewChartWidth / (count - 1d);
        var drawableHeight = OverviewChartHeight - 1d;
        var points = new PointCollection(count);

        for (var index = 0; index < count; index++)
        {
            var x = stepX * index;
            var y = drawableHeight - ((first[index] + second[index]) / normalizedMax * drawableHeight);
            points.Add(new Point(x, y));
        }

        return points;
    }

    private PeakMarkerInfo GetNetworkPeakMarker()
    {
        return _networkDisplayMode == NetworkDisplayMode.Split
            ? BuildPeakMarker(
                ("下", _networkDownloadHistory, NetworkDownloadBrush),
                ("上", _networkUploadHistory, NetworkUploadBrush))
            : BuildCombinedPeakMarker("峰", _networkDownloadHistory, _networkUploadHistory, NetworkTotalBrush);
    }

    private PeakMarkerInfo GetIoPeakMarker()
    {
        return _ioDisplayMode == IoDisplayMode.Split
            ? BuildPeakMarker(
                ("读", _ioReadHistory, IoReadBrush),
                ("写", _ioWriteHistory, IoWriteBrush))
            : BuildCombinedPeakMarker("峰", _ioReadHistory, _ioWriteHistory, IoTotalBrush);
    }

    private static PeakMarkerInfo BuildPeakMarker(params (string Prefix, IReadOnlyList<double> Samples, Brush Brush)[] series)
    {
        PeakMarkerInfo? best = null;

        foreach (var (prefix, samples, brush) in series)
        {
            if (samples.Count == 0)
            {
                continue;
            }

            var peakValue = samples.Max();
            var peakIndex = -1;
            for (var index = 0; index < samples.Count; index++)
            {
                if (samples[index] == peakValue)
                {
                    peakIndex = index;
                    break;
                }
            }

            if (peakIndex < 0)
            {
                continue;
            }

            var marker = BuildPeakMarker(samples, peakIndex, peakValue, prefix, brush);
            if (best is null || marker.RawValue > best.Value.RawValue)
            {
                best = marker;
            }
        }

        return best ?? new PeakMarkerInfo(0, OverviewChartHeight - 1d, "峰 0 B/s", Brushes.Transparent, 0);
    }

    private static PeakMarkerInfo BuildCombinedPeakMarker(string prefix, IReadOnlyList<double> first, IReadOnlyList<double> second, Brush brush)
    {
        var count = Math.Min(first.Count, second.Count);
        if (count == 0)
        {
            return new PeakMarkerInfo(0, OverviewChartHeight - 1d, "峰 0 B/s", Brushes.Transparent, 0);
        }

        var peakValue = 0d;
        var peakIndex = 0;
        for (var index = 0; index < count; index++)
        {
            var value = first[index] + second[index];
            if (value > peakValue)
            {
                peakValue = value;
                peakIndex = index;
            }
        }

        return BuildCombinedPeakMarker(first, second, count, peakIndex, peakValue, prefix, brush);
    }

    private static PeakMarkerInfo BuildPeakMarker(IReadOnlyList<double> samples, int peakIndex, double peakValue, string prefix, Brush brush)
    {
        var normalizedMax = Math.Max(1d, samples.Max());
        var drawableHeight = OverviewChartHeight - 1d;
        var stepX = samples.Count > 1 ? OverviewChartWidth / (samples.Count - 1d) : OverviewChartWidth;
        var x = samples.Count > 1 ? stepX * peakIndex : OverviewChartWidth / 2d;
        var y = drawableHeight - (peakValue / normalizedMax * drawableHeight);
        var left = Math.Max(0d, Math.Min(OverviewChartWidth - OverviewPeakLabelWidth, x + 4d));
        var top = -18d;
        return new PeakMarkerInfo(left, top, $"{prefix} {FormatBytesPerSecond((long)Math.Round(peakValue, MidpointRounding.AwayFromZero))}", brush, peakValue);
    }

    private static PeakMarkerInfo BuildCombinedPeakMarker(
        IReadOnlyList<double> first,
        IReadOnlyList<double> second,
        int count,
        int peakIndex,
        double peakValue,
        string prefix,
        Brush brush)
    {
        var normalizedMax = Math.Max(1d, peakValue);
        for (var index = 0; index < count; index++)
        {
            normalizedMax = Math.Max(normalizedMax, first[index] + second[index]);
        }

        var drawableHeight = OverviewChartHeight - 1d;
        var stepX = count > 1 ? OverviewChartWidth / (count - 1d) : OverviewChartWidth;
        var x = count > 1 ? stepX * peakIndex : OverviewChartWidth / 2d;
        var y = drawableHeight - (peakValue / normalizedMax * drawableHeight);
        var left = Math.Max(0d, Math.Min(OverviewChartWidth - OverviewPeakLabelWidth, x + 4d));
        return new PeakMarkerInfo(left, -18d, $"{prefix} {FormatBytesPerSecond((long)Math.Round(peakValue, MidpointRounding.AwayFromZero))}", brush, peakValue);
    }

    private static Brush CreateFrozenBrush(string colorHex)
    {
        var brush = (Brush)new BrushConverter().ConvertFromString(colorHex)!;
        brush.Freeze();
        return brush;
    }

    private void RaiseOverviewChartProperties()
    {
        RaisePropertyChanged(nameof(NetworkMiniTotalPoints));
        RaisePropertyChanged(nameof(NetworkMiniDownloadPoints));
        RaisePropertyChanged(nameof(NetworkMiniUploadPoints));
        RaisePropertyChanged(nameof(IoMiniTotalPoints));
        RaisePropertyChanged(nameof(IoMiniReadPoints));
        RaisePropertyChanged(nameof(IoMiniWritePoints));
        RaisePropertyChanged(nameof(NetworkMiniPeakLeft));
        RaisePropertyChanged(nameof(NetworkMiniPeakTop));
        RaisePropertyChanged(nameof(NetworkMiniPeakLabel));
        RaisePropertyChanged(nameof(NetworkMiniPeakBrush));
        RaisePropertyChanged(nameof(IoMiniPeakLeft));
        RaisePropertyChanged(nameof(IoMiniPeakTop));
        RaisePropertyChanged(nameof(IoMiniPeakLabel));
        RaisePropertyChanged(nameof(IoMiniPeakBrush));
    }

    private void RaiseLiveDisplayStateChanged()
    {
        RaisePropertyChanged(nameof(HeaderTimestamp));
        RaisePropertyChanged(nameof(SummarySubtitle));
        RaisePropertyChanged(nameof(TotalSpeedDisplay));
        RaisePropertyChanged(nameof(TotalDownloadDisplay));
        RaisePropertyChanged(nameof(TotalUploadDisplay));
        RaisePropertyChanged(nameof(NetworkOverviewTitle));
        RaisePropertyChanged(nameof(NetworkTotalVolumeDisplay));
        RaisePropertyChanged(nameof(NetworkDownloadSpeedDisplay));
        RaisePropertyChanged(nameof(NetworkUploadSpeedDisplay));
        RaisePropertyChanged(nameof(IoOverviewTitle));
        RaisePropertyChanged(nameof(TotalIoSpeedDisplay));
        RaisePropertyChanged(nameof(TotalIoVolumeDisplay));
        RaisePropertyChanged(nameof(IoReadSpeedDisplay));
        RaisePropertyChanged(nameof(IoWriteSpeedDisplay));
        RaisePropertyChanged(nameof(IoReadVolumeDisplay));
        RaisePropertyChanged(nameof(IoWriteVolumeDisplay));
        RaisePropertyChanged(nameof(ActiveApplicationsDisplay));
        RaisePropertyChanged(nameof(DashboardStatusTip));
        RaisePropertyChanged(nameof(ViewModeDisplay));
        RaisePropertyChanged(nameof(TrafficSummaryDisplay));
        RaisePropertyChanged(nameof(IoSummaryDisplay));
        RaiseOverviewChartProperties();
    }

    private void RaiseDisplayStateChanged()
    {
        RaiseLiveDisplayStateChanged();
        RaisePropertyChanged(nameof(SortMenuDisplay));
        RaisePropertyChanged(nameof(RefreshIntervalDisplay));
        RaisePropertyChanged(nameof(RefreshIntervalMenuDisplay));
        RaisePropertyChanged(nameof(IsNetworkHiddenMode));
        RaisePropertyChanged(nameof(IsNetworkTotalMode));
        RaisePropertyChanged(nameof(IsNetworkSplitMode));
        RaisePropertyChanged(nameof(IsIoHiddenMode));
        RaisePropertyChanged(nameof(IsIoTotalMode));
        RaisePropertyChanged(nameof(IsIoSplitMode));
        RaisePropertyChanged(nameof(IsNetworkSummaryTotalMode));
        RaisePropertyChanged(nameof(IsNetworkSummarySplitMode));
        RaisePropertyChanged(nameof(IsIoSummaryTotalMode));
        RaisePropertyChanged(nameof(IsIoSummarySplitMode));
        RaisePropertyChanged(nameof(IsApplicationSumDataSourceMode));
        RaisePropertyChanged(nameof(IsSystemTotalsDataSourceMode));
        RaisePropertyChanged(nameof(IsAllApplicationsRecordingMode));
        RaisePropertyChanged(nameof(IsWindowedOnlyRecordingMode));
        RaisePropertyChanged(nameof(IsForegroundBackgroundHiddenMode));
        RaisePropertyChanged(nameof(IsForegroundBackgroundVisibleMode));
    }
}

public sealed class ApplicationCardRowViewModel
{
    public ApplicationCardRowViewModel(IReadOnlyList<ApplicationCardViewModel> items)
    {
        Items = items;
    }

    public IReadOnlyList<ApplicationCardViewModel> Items { get; }
}

internal readonly record struct PeakMarkerInfo(double Left, double Top, string Label, Brush Brush, double RawValue);
