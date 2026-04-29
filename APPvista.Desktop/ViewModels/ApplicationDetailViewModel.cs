using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ScottPlot;
using SkiaSharp;
using APPvista.Desktop.Services;
using APPvista.Domain.Entities;

namespace APPvista.Desktop.ViewModels;

public sealed class ApplicationDetailViewModel : ObservableObject, IDisposable
{
    private enum DetailDataMode
    {
        Current,
        History
    }

    private enum HistoryAnalysisDimension
    {
        Day,
        Week,
        Month,
        Custom
    }

    private const int MaxHistorySeconds = 120;
    private const int ChartWidth = 760;
    private const int ChartHeight = 340;
    private const int HistoryChartHeight = 180;
    private const int DefaultHistoryChartViewportWidth = 760;
    private const int HistoryChartVisibleBars = 7;
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(1);
    private const int HistoryChartDays = 30;
    private const uint ShellExecuteInvokeIdListMask = 0x0000000C;
    private const int ShellShowNormal = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellExecuteInfo
    {
        public int cbSize;
        public uint fMask;
        public nint hwnd;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpVerb;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpParameters;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpDirectory;
        public int nShow;
        public nint hInstApp;
        public nint lpIDList;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpClass;
        public nint hkeyClass;
        public uint dwHotKey;
        public nint hIconOrMonitor;
        public nint hProcess;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ShellExecuteEx(ref ShellExecuteInfo lpExecInfo);

    private readonly ApplicationCardViewModel _application;
    private readonly DetailDisplayPreferences _preferences;
    private readonly ApplicationHistoryAnalysisProvider _historyAnalysisProvider;
    private readonly DispatcherTimer _refreshTimer;
    private readonly List<TimedMetricSample> _networkHistory = new();
    private readonly List<TimedMetricSample> _ioHistory = new();
    private DetailDataMode _selectedDataMode = DetailDataMode.Current;
    private HistoryAnalysisDimension _selectedHistoryDimension = HistoryAnalysisDimension.Day;
    private ApplicationHistorySummary _historySummary = ApplicationHistorySummary.Empty;
    private List<DailyProcessActivitySummary> _historyDatabaseDailyRecords = [];
    private List<DailyProcessActivitySummary> _allHistoryDailyRecords = [];
    private List<DailyProcessActivitySummary> _historyDailyRecords = [];
    private DateOnly _historyDisplayedMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateOnly _historySelectedDate = DateOnly.FromDateTime(DateTime.Today);
    private readonly HashSet<DateOnly> _historyCustomSelectedDates = [];
    private (HistoryAnalysisDimension TargetDimension, DateOnly RangeStart, DateOnly RangeEnd)? _pendingHistorySelectionRange;
    private bool _isHistoryDatePickerOpen;

    private ImageSource? _networkChartSource;
    private ImageSource? _ioChartSource;
    private ImageSource? _historyNetworkChartSource;
    private ImageSource? _historyIoChartSource;
    private string _networkChartTopLabel = "1 KB/s";
    private string _ioChartTopLabel = "1 KB/s";
    private string _historyNetworkChartTopLabel = "1 KB";
    private string _historyIoChartTopLabel = "1 KB";
    private bool _pendingStaticApplicationRefresh;
    private bool _pendingApplicationRefresh;
    private bool _pendingChartRefresh;
    private bool _pendingHistoryRefresh;
    private bool _pendingHistoryChartRefresh;
    private bool _isWindowRenderingActive = true;
    private int _chartRenderVersion;
    private int _historyChartRenderVersion;
    private double _historyChartViewportWidth = DefaultHistoryChartViewportWidth;
    private int _historySummaryLoadVersion;
    private bool _isDisposed;
    private readonly bool _isHistoryOnlyMode;
    private bool _hasLoadedHistoryDatabaseRecords;
    private readonly RelayCommand _openOnlineSearchCommand;
    private readonly RelayCommand _openExecutablePropertiesCommand;

    public ApplicationDetailViewModel(ApplicationCardViewModel application, DetailDisplayPreferences preferences, string databasePath)
        : this(application, preferences, databasePath, isHistoryOnlyMode: false, initialDataMode: DetailDataMode.Current)
    {
    }

    public ApplicationDetailViewModel(
        string processName,
        string displayName,
        string executablePath,
        string? iconSourcePath,
        DetailDisplayPreferences preferences,
        string databasePath)
        : this(
            CreateHistoryOnlyApplicationCard(processName, displayName, executablePath, iconSourcePath),
            preferences,
            databasePath,
            isHistoryOnlyMode: true,
            initialDataMode: DetailDataMode.History)
    {
    }

    private ApplicationDetailViewModel(
        ApplicationCardViewModel application,
        DetailDisplayPreferences preferences,
        string databasePath,
        bool isHistoryOnlyMode,
        DetailDataMode initialDataMode)
    {
        _application = application;
        _preferences = preferences;
        _historyAnalysisProvider = new ApplicationHistoryAnalysisProvider(databasePath);
        _isHistoryOnlyMode = isHistoryOnlyMode;
        _selectedDataMode = initialDataMode;
        _refreshTimer = new DispatcherTimer
        {
            Interval = RefreshInterval
        };
        _refreshTimer.Tick += OnRefreshTimerTick;

        _application.PropertyChanged += OnApplicationPropertyChanged;
        _preferences.PropertyChanged += OnPreferencesPropertyChanged;

        NetworkDisplayOptions = new ObservableCollection<string>(
        [
            DetailDisplayPreferences.HiddenOption,
            DetailDisplayPreferences.TotalOption,
            DetailDisplayPreferences.SplitOption
        ]);

        IoDisplayOptions = new ObservableCollection<string>(
        [
            DetailDisplayPreferences.HiddenOption,
            DetailDisplayPreferences.TotalOption,
            DetailDisplayPreferences.SplitOption
        ]);

        ForegroundBackgroundOptions = new ObservableCollection<string>(
        [
            DetailDisplayPreferences.HiddenOption,
            DetailDisplayPreferences.VisibleOption
        ]);

        ChartScaleOptions = new ObservableCollection<string>(
        [
            DetailDisplayPreferences.ChartScale30SecondsOption,
            DetailDisplayPreferences.ChartScale1MinuteOption,
            DetailDisplayPreferences.ChartScale2MinutesOption
        ]);
        HistoryOverlayOptions = new ObservableCollection<string>(
        [
            DetailDisplayPreferences.HistoryOverlayOffOption,
            DetailDisplayPreferences.HistoryOverlayUsageDurationOption,
            DetailDisplayPreferences.HistoryOverlayForegroundDurationOption
        ]);
        HistoryCalendarDays = new ObservableCollection<ApplicationHistoryCalendarDayViewModel>();

        SetNetworkHiddenDisplayCommand = new RelayCommand(() => SelectedNetworkDisplayOption = DetailDisplayPreferences.HiddenOption);
        SetNetworkTotalDisplayCommand = new RelayCommand(() => SelectedNetworkDisplayOption = DetailDisplayPreferences.TotalOption);
        SetNetworkSplitDisplayCommand = new RelayCommand(() => SelectedNetworkDisplayOption = DetailDisplayPreferences.SplitOption);
        SetIoHiddenDisplayCommand = new RelayCommand(() => SelectedIoDisplayOption = DetailDisplayPreferences.HiddenOption);
        SetIoTotalDisplayCommand = new RelayCommand(() => SelectedIoDisplayOption = DetailDisplayPreferences.TotalOption);
        SetIoSplitDisplayCommand = new RelayCommand(() => SelectedIoDisplayOption = DetailDisplayPreferences.SplitOption);
        SetChartScale30SecondsCommand = new RelayCommand(() => SelectedChartScaleOption = DetailDisplayPreferences.ChartScale30SecondsOption);
        SetChartScale1MinuteCommand = new RelayCommand(() => SelectedChartScaleOption = DetailDisplayPreferences.ChartScale1MinuteOption);
        SetChartScale2MinutesCommand = new RelayCommand(() => SelectedChartScaleOption = DetailDisplayPreferences.ChartScale2MinutesOption);
        SetHistoryOverlayOffCommand = new RelayCommand(() => SelectedHistoryOverlayOption = DetailDisplayPreferences.HistoryOverlayOffOption);
        SetHistoryOverlayUsageDurationCommand = new RelayCommand(() => SelectedHistoryOverlayOption = DetailDisplayPreferences.HistoryOverlayUsageDurationOption);
        SetHistoryOverlayForegroundDurationCommand = new RelayCommand(() => SelectedHistoryOverlayOption = DetailDisplayPreferences.HistoryOverlayForegroundDurationOption);
        ShowCurrentDataCommand = new RelayCommand(ShowCurrentData);
        ShowHistoryDataCommand = new RelayCommand(ShowHistoryData);
        SetHistoryDayDimensionCommand = new RelayCommand(() => SetHistoryDimension(HistoryAnalysisDimension.Day));
        SetHistoryWeekDimensionCommand = new RelayCommand(() => SetHistoryDimension(HistoryAnalysisDimension.Week));
        SetHistoryMonthDimensionCommand = new RelayCommand(() => SetHistoryDimension(HistoryAnalysisDimension.Month));
        SetHistoryCustomDimensionCommand = new RelayCommand(() => SetHistoryDimension(HistoryAnalysisDimension.Custom));
        ToggleHistoryDatePickerCommand = new RelayCommand(ToggleHistoryDatePicker);
        ShowPreviousHistoryMonthCommand = new RelayCommand(ShowPreviousHistoryMonth);
        ShowNextHistoryMonthCommand = new RelayCommand(ShowNextHistoryMonth);
        _openOnlineSearchCommand = new RelayCommand(OpenOnlineSearch, CanOpenOnlineSearch);
        _openExecutablePropertiesCommand = new RelayCommand(OpenExecutableProperties, CanOpenExecutableProperties);

        LoadHistorySummary();
        RefreshHistoryCalendar();
        AppendCurrentSamples(DateTime.UtcNow);
        QueueChartRefresh();
    }

    private ProcessResourceSnapshot Snapshot => _application.Snapshot;
    private int HistorySeconds => _preferences.ChartHistorySeconds;

    public ObservableCollection<string> NetworkDisplayOptions { get; }
    public ObservableCollection<string> IoDisplayOptions { get; }
    public ObservableCollection<string> ForegroundBackgroundOptions { get; }
    public ObservableCollection<string> ChartScaleOptions { get; }
    public ObservableCollection<string> HistoryOverlayOptions { get; }
    public ObservableCollection<ApplicationHistoryCalendarDayViewModel> HistoryCalendarDays { get; }

    public string DisplayName => _application.DisplayName;
    public string OriginalName => _application.OriginalName;
    public string DisplayNameWithOriginal =>
        string.Equals(_application.DisplayName, _application.OriginalName, StringComparison.Ordinal)
            ? _application.DisplayName
            : $"{_application.DisplayName}（原名：{_application.OriginalName}）";
    public string? IconSourcePath => _application.IconSourcePath;
    public string StateDisplay => _application.StateDisplay;
    public bool IsHistoryOnlyMode => _isHistoryOnlyMode;
    public bool CanShowCurrentData => !_isHistoryOnlyMode;
    public bool ShowDataModeSwitch => !_isHistoryOnlyMode;
    public bool ShowHeaderState => !_isHistoryOnlyMode;
    public bool ShowHeaderFocusRatio => !_isHistoryOnlyMode;
    public bool ShowHeaderExecutablePath => _isHistoryOnlyMode;
    public string HeaderExecutablePathDisplay => HistoryExecutablePathDisplay;
    public string HeaderCaptionDisplay => _isHistoryOnlyMode ? DisplayNameWithOriginal : StateDisplay;
    public string HeaderTitleDisplay => _isHistoryOnlyMode ? HistoryExecutablePathDisplay : DisplayNameWithOriginal;
    public double HeaderCaptionFontSize => _isHistoryOnlyMode ? 15d : 13d;
    public double HeaderTitleFontSize => _isHistoryOnlyMode ? 18d : 30d;
    public System.Windows.FontWeight HeaderCaptionFontWeight => _isHistoryOnlyMode ? System.Windows.FontWeights.SemiBold : System.Windows.FontWeights.Normal;
    public System.Windows.FontWeight HeaderTitleFontWeight => _isHistoryOnlyMode ? System.Windows.FontWeights.Normal : System.Windows.FontWeights.SemiBold;
    public bool IsCurrentDataMode => _selectedDataMode == DetailDataMode.Current;
    public bool IsHistoryDataMode => _selectedDataMode == DetailDataMode.History;
    public bool IsHistoryDayDimension => _selectedHistoryDimension == HistoryAnalysisDimension.Day;
    public bool IsHistoryWeekDimension => _selectedHistoryDimension == HistoryAnalysisDimension.Week;
    public bool IsHistoryMonthDimension => _selectedHistoryDimension == HistoryAnalysisDimension.Month;
    public bool IsHistoryCustomDimension => _selectedHistoryDimension == HistoryAnalysisDimension.Custom;
    public string HistoryDimensionTitle => _selectedHistoryDimension switch
    {
        HistoryAnalysisDimension.Week => "按周",
        HistoryAnalysisDimension.Month => "按月",
        HistoryAnalysisDimension.Custom => "自选",
        _ => "按日"
    };
    public string HistorySelectionDisplay => _selectedHistoryDimension switch
    {
        HistoryAnalysisDimension.Week => $"已选周：{GetWeekStart(_historySelectedDate):yyyy-MM-dd}-{GetWeekStart(_historySelectedDate).AddDays(6):yyyy-MM-dd}",
        HistoryAnalysisDimension.Month => $"已选月：{new DateOnly(_historySelectedDate.Year, _historySelectedDate.Month, 1):yyyy 年 MM 月}",
        HistoryAnalysisDimension.Custom => $"已选区间：{BuildCustomRangeDisplay(GetCustomSelectedDays())}",
        _ => $"已选日：{_historySelectedDate:yyyy-MM-dd}"
    };
    public DateTime? SelectedHistoryDateTime
    {
        get => _historySelectedDate.ToDateTime(TimeOnly.MinValue);
        set
        {
            if (value is not null)
            {
                SetHistorySelectedDate(DateOnly.FromDateTime(value.Value));
            }
        }
    }
    public bool IsHistoryDatePickerOpen
    {
        get => _isHistoryDatePickerOpen;
        set => SetProperty(ref _isHistoryDatePickerOpen, value);
    }
    public string HistoryCalendarMonthDisplay => $"{_historyDisplayedMonth:yyyy 年 MM 月}";
    public string HistoryRangeDisplay => _historySummary.RangeDisplay;
    public string HistoryActiveDaysDisplay => _historySummary.ActiveDaysDisplay;
    public string HistoryUsageDisplay => _historySummary.TotalUsageDisplay;
    public string HistoryForegroundDurationDisplay => _historySummary.ForegroundDurationDisplay;
    public string HistoryBackgroundDurationDisplay => _historySummary.BackgroundDurationDisplay;
    public string HistoryForegroundRatioDisplay => _historySummary.ForegroundRatioDisplay;
    public string HistoryTrafficDisplay => BuildHistoryTrafficDisplay();
    public string HistoryIoDisplay => BuildHistoryIoDisplay();
    public string HistoryAverageCpuDisplay => _historySummary.AverageCpuDisplay;
    public string HistoryAverageIopsDisplay => _historySummary.AverageIopsDisplay;
    public string HistoryMemoryDisplay => _historySummary.MemorySummaryDisplay;
    public string HistoryThreadDisplay => _historySummary.ThreadSummaryDisplay;
    public string HistoryAverageForegroundCpuDisplay => _historySummary.AverageForegroundCpuDisplay;
    public string HistoryAverageForegroundMemoryDisplay => _historySummary.AverageForegroundMemoryDisplay;
    public string HistoryAverageForegroundIopsDisplay => _historySummary.AverageForegroundIopsDisplay;
    public string HistoryAverageBackgroundCpuDisplay => _historySummary.AverageBackgroundCpuDisplay;
    public string HistoryAverageBackgroundMemoryDisplay => _historySummary.AverageBackgroundMemoryDisplay;
    public string HistoryAverageBackgroundIopsDisplay => _historySummary.AverageBackgroundIopsDisplay;
    public string HistoryExecutablePathDisplay => _historySummary.ExecutablePathDisplay;
    public string TodayFocusRatio => BuildFocusRatio(Snapshot.DailyForegroundMilliseconds, Snapshot.DailyBackgroundMilliseconds);

    public string SelectedNetworkDisplayOption
    {
        get => _preferences.NetworkDisplayOption;
        set => _preferences.NetworkDisplayOption = value;
    }

    public string SelectedIoDisplayOption
    {
        get => _preferences.IoDisplayOption;
        set => _preferences.IoDisplayOption = value;
    }

    public string SelectedForegroundBackgroundOption
    {
        get => _preferences.ForegroundBackgroundOption;
        set => _preferences.ForegroundBackgroundOption = value;
    }

    public string SelectedChartScaleOption
    {
        get => _preferences.ChartScaleOption;
        set => _preferences.ChartScaleOption = value;
    }

    public string SelectedHistoryOverlayOption
    {
        get => _preferences.HistoryOverlayOption;
        set => _preferences.HistoryOverlayOption = value;
    }

    public bool IsNetworkHiddenMode => _preferences.NetworkDisplayOption == DetailDisplayPreferences.HiddenOption;
    public bool IsNetworkTotalMode => _preferences.NetworkDisplayOption == DetailDisplayPreferences.TotalOption;
    public bool IsNetworkSplitMode => _preferences.NetworkDisplayOption == DetailDisplayPreferences.SplitOption;
    public bool IsIoHiddenMode => _preferences.IoDisplayOption == DetailDisplayPreferences.HiddenOption;
    public bool IsIoTotalMode => _preferences.IoDisplayOption == DetailDisplayPreferences.TotalOption;
    public bool IsIoSplitMode => _preferences.IoDisplayOption == DetailDisplayPreferences.SplitOption;
    public bool IsChartScale30SecondsMode => _preferences.ChartScaleOption == DetailDisplayPreferences.ChartScale30SecondsOption;
    public bool IsChartScale1MinuteMode => _preferences.ChartScaleOption == DetailDisplayPreferences.ChartScale1MinuteOption;
    public bool IsChartScale2MinutesMode => _preferences.ChartScaleOption == DetailDisplayPreferences.ChartScale2MinutesOption;
    public bool IsHistoryOverlayOffMode => _preferences.HistoryOverlayOption == DetailDisplayPreferences.HistoryOverlayOffOption;
    public bool IsHistoryOverlayUsageDurationMode => _preferences.HistoryOverlayOption == DetailDisplayPreferences.HistoryOverlayUsageDurationOption;
    public bool IsHistoryOverlayForegroundDurationMode => _preferences.HistoryOverlayOption == DetailDisplayPreferences.HistoryOverlayForegroundDurationOption;

    public string ProcessCountDisplay => Snapshot.ProcessCount.ToString(CultureInfo.InvariantCulture);
    public string ProcessIdDisplay => Snapshot.ProcessId.ToString(CultureInfo.InvariantCulture);
    public string ExecutablePathDisplay => string.IsNullOrWhiteSpace(Snapshot.ExecutablePath) ? "-" : Snapshot.ExecutablePath;
    public string CpuDisplay => Snapshot.CpuDisplay;
    public string WorkingSetDisplay => Snapshot.WorkingSetDisplay;
    public string PeakWorkingSetDisplay => Snapshot.PeakWorkingSetDisplay;
    public string PrivateMemoryDisplay => Snapshot.PrivateMemoryDisplay;
    public string CommitSizeDisplay => Snapshot.CommitSizeDisplay;
    public string ThreadCountDisplay => Snapshot.ThreadCount.ToString(CultureInfo.InvariantCulture);
    public string ThreadAverageDisplay => Snapshot.ThreadAverageDisplay;
    public string PeakThreadDisplay => Snapshot.PeakThreadDisplay;
    public string ThreadPeakMeanRatioDisplay => Snapshot.ThreadPeakMeanRatioDisplay;
    public string RealtimeIopsDisplay => Snapshot.RealtimeIopsDisplay;
    public string AverageIopsDisplay => Snapshot.AverageIopsDisplay;
    public string IoReadWriteRatioDisplay => Snapshot.IoReadWriteRatioDisplay;
    public string ForegroundDurationDisplay => Snapshot.DailyForegroundDisplay;
    public string BackgroundDurationDisplay => Snapshot.DailyBackgroundDisplay;
    public string AverageForegroundCpuDisplay => Snapshot.AverageForegroundCpuDisplay;
    public string AverageForegroundWorkingSetDisplay => Snapshot.AverageForegroundWorkingSetDisplay;
    public string AverageForegroundIopsDisplay => Snapshot.AverageForegroundIopsDisplay;
    public string AverageBackgroundCpuDisplay => Snapshot.AverageBackgroundCpuDisplay;
    public string AverageBackgroundWorkingSetDisplay => Snapshot.AverageBackgroundWorkingSetDisplay;
    public string AverageBackgroundIopsDisplay => Snapshot.AverageBackgroundIopsDisplay;

    public bool ShowNetworkHiddenNotice => _preferences.IsNetworkHidden;
    public bool ShowNetworkTotals => !_preferences.IsNetworkHidden && !_preferences.IsNetworkSplit;
    public bool ShowNetworkSplit => !_preferences.IsNetworkHidden && _preferences.IsNetworkSplit;
    public bool ShowNetworkChart => !_preferences.IsNetworkHidden;
    public bool ShowIoHiddenNotice => _preferences.IsIoHidden;
    public bool ShowIoTotals => !_preferences.IsIoHidden && !_preferences.IsIoSplit;
    public bool ShowIoSplit => !_preferences.IsIoHidden && _preferences.IsIoSplit;
    public bool ShowIoChart => !_preferences.IsIoHidden;
    public bool ShowForegroundBackgroundDetails => _preferences.IsForegroundBackgroundVisible;
    public bool ShowNetworkLegend => ShowNetworkChart && _preferences.IsNetworkSplit;
    public bool ShowIoLegend => ShowIoChart && _preferences.IsIoSplit;

    public string NetworkChartTitle => _preferences.IsNetworkSplit ? $"{HistorySeconds} 秒网络趋势（上下行）" : $"{HistorySeconds} 秒网络趋势（总量）";
    public string IoChartTitle => _preferences.IsIoSplit ? $"{HistorySeconds} 秒 I/O 趋势（读写）" : $"{HistorySeconds} 秒 I/O 趋势（总量）";
    public string HistoryNetworkChartTitle => _preferences.IsNetworkSplit ? $"{HistoryDimensionTitle}网络趋势（上下行）" : $"{HistoryDimensionTitle}网络趋势（总量）";
    public string HistoryIoChartTitle => _preferences.IsIoSplit ? $"{HistoryDimensionTitle} I/O 趋势（读写）" : $"{HistoryDimensionTitle} I/O 趋势（总量）";
    public string ChartXAxisStartLabel => $"{HistorySeconds} 秒前";
    public string ChartXAxisCenterLabel => "时间（秒）";
    public string ChartXAxisEndLabel => "现在";
    public string HistoryChartXAxisStartLabel => _historySummary.ChartStartLabel;
    public string HistoryChartXAxisCenterLabel => "日期";
    public string HistoryChartXAxisEndLabel => _historySummary.ChartEndLabel;
    public double HistoryChartDisplayWidth => GetHistoryChartRenderWidth(_historyDailyRecords.Count, _historyChartViewportWidth);
    public string NetworkChartTopLabel
    {
        get => _networkChartTopLabel;
        private set => SetProperty(ref _networkChartTopLabel, value);
    }

    public string NetworkChartBottomLabel => "0 B/s";

    public string IoChartTopLabel
    {
        get => _ioChartTopLabel;
        private set => SetProperty(ref _ioChartTopLabel, value);
    }

    public string IoChartBottomLabel => "0 B/s";
    public string HistoryNetworkChartTopLabel
    {
        get => _historyNetworkChartTopLabel;
        private set => SetProperty(ref _historyNetworkChartTopLabel, value);
    }

    public string HistoryNetworkChartBottomLabel => "0 B";
    public string HistoryIoChartTopLabel
    {
        get => _historyIoChartTopLabel;
        private set => SetProperty(ref _historyIoChartTopLabel, value);
    }

    public string HistoryIoChartBottomLabel => "0 B";
    public string NetworkPrimaryLegendLabel => "下载";
    public string NetworkSecondaryLegendLabel => "上传";
    public string IoPrimaryLegendLabel => "读取";
    public string IoSecondaryLegendLabel => "写入";

    public ImageSource? NetworkChartSource
    {
        get => _networkChartSource;
        private set => SetProperty(ref _networkChartSource, value);
    }

    public ImageSource? IoChartSource
    {
        get => _ioChartSource;
        private set => SetProperty(ref _ioChartSource, value);
    }

    public ImageSource? HistoryNetworkChartSource
    {
        get => _historyNetworkChartSource;
        private set => SetProperty(ref _historyNetworkChartSource, value);
    }

    public ImageSource? HistoryIoChartSource
    {
        get => _historyIoChartSource;
        private set => SetProperty(ref _historyIoChartSource, value);
    }

    public string RealtimeTrafficDisplay => Snapshot.RealtimeTrafficDisplay;
    public string DailyTrafficDisplay => Snapshot.DailyTrafficDisplay;
    public string RealtimeDownloadDisplay => Snapshot.RealtimeDownloadDisplay;
    public string RealtimeUploadDisplay => Snapshot.RealtimeUploadDisplay;
    public string DailyDownloadDisplay => Snapshot.DailyDownloadDisplay;
    public string DailyUploadDisplay => Snapshot.DailyUploadDisplay;
    public string PeakTrafficDisplay => Snapshot.PeakTrafficDisplay;
    public string PeakDownloadDisplay => Snapshot.PeakDownloadDisplay;
    public string PeakUploadDisplay => Snapshot.PeakUploadDisplay;
    public string RealtimeIoDisplay => Snapshot.RealtimeIoDisplay;
    public string DailyIoDisplay => Snapshot.DailyIoDisplay;
    public string RealtimeIoReadDisplay => Snapshot.RealtimeIoReadDisplay;
    public string RealtimeIoWriteDisplay => Snapshot.RealtimeIoWriteDisplay;
    public string DailyIoReadDisplay => Snapshot.DailyIoReadDisplay;
    public string DailyIoWriteDisplay => Snapshot.DailyIoWriteDisplay;
    public string PeakIoDisplay => Snapshot.PeakIoDisplay;
    public string PeakIoReadDisplay => Snapshot.PeakIoReadDisplay;
    public string PeakIoWriteDisplay => Snapshot.PeakIoWriteDisplay;

    public ICommand SetNetworkHiddenDisplayCommand { get; }
    public ICommand SetNetworkTotalDisplayCommand { get; }
    public ICommand SetNetworkSplitDisplayCommand { get; }
    public ICommand SetIoHiddenDisplayCommand { get; }
    public ICommand SetIoTotalDisplayCommand { get; }
    public ICommand SetIoSplitDisplayCommand { get; }
    public ICommand SetChartScale30SecondsCommand { get; }
    public ICommand SetChartScale1MinuteCommand { get; }
    public ICommand SetChartScale2MinutesCommand { get; }
    public ICommand SetHistoryOverlayOffCommand { get; }
    public ICommand SetHistoryOverlayUsageDurationCommand { get; }
    public ICommand SetHistoryOverlayForegroundDurationCommand { get; }
    public ICommand ShowCurrentDataCommand { get; }
    public ICommand ShowHistoryDataCommand { get; }
    public ICommand SetHistoryDayDimensionCommand { get; }
    public ICommand SetHistoryWeekDimensionCommand { get; }
    public ICommand SetHistoryMonthDimensionCommand { get; }
    public ICommand SetHistoryCustomDimensionCommand { get; }
    public ICommand ToggleHistoryDatePickerCommand { get; }
    public ICommand ShowPreviousHistoryMonthCommand { get; }
    public ICommand ShowNextHistoryMonthCommand { get; }
    public ICommand OpenOnlineSearchCommand => _openOnlineSearchCommand;
    public ICommand OpenExecutablePropertiesCommand => _openExecutablePropertiesCommand;

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _refreshTimer.Stop();
        _refreshTimer.Tick -= OnRefreshTimerTick;
        _application.PropertyChanged -= OnApplicationPropertyChanged;
        _preferences.PropertyChanged -= OnPreferencesPropertyChanged;
        Interlocked.Increment(ref _chartRenderVersion);
        Interlocked.Increment(ref _historyChartRenderVersion);
        Interlocked.Increment(ref _historySummaryLoadVersion);
        _pendingStaticApplicationRefresh = false;
        _pendingApplicationRefresh = false;
        _pendingChartRefresh = false;
        _pendingHistoryRefresh = false;
        _pendingHistoryChartRefresh = false;
        _networkHistory.Clear();
        _ioHistory.Clear();
        _allHistoryDailyRecords = [];
        _historyDailyRecords = [];
        HistoryCalendarDays.Clear();
        NetworkChartSource = null;
        IoChartSource = null;
        HistoryNetworkChartSource = null;
        HistoryIoChartSource = null;
    }

    public void SetWindowRenderingActive(bool isActive)
    {
        if (_isDisposed)
        {
            return;
        }

        if (_isWindowRenderingActive == isActive)
        {
            return;
        }

        _isWindowRenderingActive = isActive;
        if (isActive)
        {
            RaiseApplicationDependentProperties();
            RaiseHistoryProperties();

            if (_selectedDataMode == DetailDataMode.History)
            {
                if (_pendingHistoryRefresh)
                {
                    LoadHistorySummary();
                }
                else
                {
                    QueueHistoryChartRefresh();
                }
            }
            else
            {
                QueueChartRefresh();
            }
        }
    }

    public void SetHistoryChartViewportWidth(double viewportWidth)
    {
        var normalizedWidth = Math.Max(240d, Math.Floor(viewportWidth));
        if (Math.Abs(_historyChartViewportWidth - normalizedWidth) < 1d)
        {
            return;
        }

        _historyChartViewportWidth = normalizedWidth;
        RaisePropertyChanged(nameof(HistoryChartDisplayWidth));

        if (_selectedDataMode == DetailDataMode.History && _isWindowRenderingActive)
        {
            QueueHistoryChartRefresh();
        }
    }

    private void ShowCurrentData()
    {
        if (!CanShowCurrentData)
        {
            return;
        }

        if (_selectedDataMode == DetailDataMode.Current)
        {
            return;
        }

        _selectedDataMode = DetailDataMode.Current;
        RaiseDataModeProperties();
        QueueChartRefresh();
    }

    private void ShowHistoryData()
    {
        if (_selectedDataMode == DetailDataMode.History)
        {
            return;
        }

        _selectedDataMode = DetailDataMode.History;
        if (_isWindowRenderingActive)
        {
            LoadHistorySummary();
        }
        else
        {
            _pendingHistoryRefresh = true;
        }
        RaiseDataModeProperties();
    }

    private void SetHistoryDimension(HistoryAnalysisDimension dimension)
    {
        if (_selectedHistoryDimension == dimension)
        {
            return;
        }

        var previousDimension = _selectedHistoryDimension;
        _selectedHistoryDimension = dimension;
        if (dimension == HistoryAnalysisDimension.Custom)
        {
            SeedCustomSelectionFromCurrentDimension(previousDimension);
            _pendingHistorySelectionRange = null;
        }
        else if (previousDimension == HistoryAnalysisDimension.Custom)
        {
            var anchorDate = GetHistoryCustomSelectionAnchor();
            _historySelectedDate = dimension == HistoryAnalysisDimension.Week
                ? GetWeekStart(anchorDate)
                : dimension == HistoryAnalysisDimension.Month
                    ? new DateOnly(anchorDate.Year, anchorDate.Month, 1)
                    : anchorDate;
            _historyDisplayedMonth = new DateOnly(_historySelectedDate.Year, _historySelectedDate.Month, 1);
            _pendingHistorySelectionRange = null;
        }
        else if (dimension == HistoryAnalysisDimension.Month)
        {
            _historySelectedDate = new DateOnly(_historySelectedDate.Year, _historySelectedDate.Month, 1);
        }
        else if (dimension == HistoryAnalysisDimension.Week && previousDimension == HistoryAnalysisDimension.Month)
        {
            var pendingRange = ResolveHistoryRange(previousDimension, _historySelectedDate);
            _pendingHistorySelectionRange = (dimension, pendingRange.Start, pendingRange.End);
            _historySelectedDate = ResolveWeekHistorySelection(previousDimension, _historySelectedDate);
            _historyDisplayedMonth = new DateOnly(_historySelectedDate.Year, _historySelectedDate.Month, 1);
        }
        else if (dimension == HistoryAnalysisDimension.Day)
        {
            var pendingRange = ResolveHistoryRange(previousDimension, _historySelectedDate);
            _pendingHistorySelectionRange = (dimension, pendingRange.Start, pendingRange.End);
            _historySelectedDate = ResolveDayHistorySelection(previousDimension, _historySelectedDate);
            _historyDisplayedMonth = new DateOnly(_historySelectedDate.Year, _historySelectedDate.Month, 1);
        }
        else
        {
            _pendingHistorySelectionRange = null;
        }

        ApplyHistorySelection();
    }

    private void ToggleHistoryDatePicker()
    {
        IsHistoryDatePickerOpen = !IsHistoryDatePickerOpen;
    }

    private void SetHistorySelectedDate(DateOnly date)
    {
        var normalizedDate = _selectedHistoryDimension == HistoryAnalysisDimension.Month
            ? new DateOnly(date.Year, date.Month, 1)
            : date;

        if (_historySelectedDate == normalizedDate)
        {
            return;
        }

        _historySelectedDate = normalizedDate;
        _historyDisplayedMonth = new DateOnly(date.Year, date.Month, 1);
        ApplyHistorySelection();
    }

    public void SetHistoryCustomDateSelection(DateOnly date, bool isSelected)
    {
        if (_selectedHistoryDimension != HistoryAnalysisDimension.Custom)
        {
            return;
        }

        var changed = isSelected
            ? _historyCustomSelectedDates.Add(date)
            : _historyCustomSelectedDates.Remove(date);
        if (!changed)
        {
            return;
        }

        ApplyHistorySelection();
    }

    private void ShowPreviousHistoryMonth()
    {
        _historyDisplayedMonth = _historyDisplayedMonth.AddMonths(-1);
        if (_selectedHistoryDimension == HistoryAnalysisDimension.Month)
        {
            _historySelectedDate = _historyDisplayedMonth;
            ApplyHistorySelection();
        }
        else
        {
            RefreshHistoryCalendar();
        }
        RaisePropertyChanged(nameof(HistoryCalendarMonthDisplay));
        RaisePropertyChanged(nameof(HistorySelectionDisplay));
    }

    private void ShowNextHistoryMonth()
    {
        _historyDisplayedMonth = _historyDisplayedMonth.AddMonths(1);
        if (_selectedHistoryDimension == HistoryAnalysisDimension.Month)
        {
            _historySelectedDate = _historyDisplayedMonth;
            ApplyHistorySelection();
        }
        else
        {
            RefreshHistoryCalendar();
        }
        RaisePropertyChanged(nameof(HistoryCalendarMonthDisplay));
        RaisePropertyChanged(nameof(HistorySelectionDisplay));
    }

    private void LoadHistorySummary()
    {
        _ = LoadHistorySummaryAsync();
    }

    private async Task LoadHistorySummaryAsync()
    {
        _pendingHistoryRefresh = false;
        var version = Interlocked.Increment(ref _historySummaryLoadVersion);
        var processName = _application.OriginalName;

        var records = (await Task.Run(() => _historyAnalysisProvider.LoadDailyRecords(processName, maxDays: 120)))
            .OrderBy(record => record.Day, StringComparer.Ordinal)
            .ToList();

        if (version != _historySummaryLoadVersion)
        {
            return;
        }

        if (_isDisposed)
        {
            return;
        }

        _historyDatabaseDailyRecords = records
            .OrderBy(record => record.Day, StringComparer.Ordinal)
            .ToList();
        _hasLoadedHistoryDatabaseRecords = true;
        _allHistoryDailyRecords = BuildMergedHistoryDailyRecords();
        if (_pendingHistorySelectionRange is { } pendingRange && _selectedHistoryDimension == pendingRange.TargetDimension)
        {
            var resolvedDate = pendingRange.TargetDimension switch
            {
                HistoryAnalysisDimension.Week => GetWeekStart(
                    ResolveEarliestHistoryDate(_allHistoryDailyRecords, pendingRange.RangeStart, pendingRange.RangeEnd)
                    ?? pendingRange.RangeStart),
                HistoryAnalysisDimension.Day => ResolveEarliestHistoryDate(_allHistoryDailyRecords, pendingRange.RangeStart, pendingRange.RangeEnd)
                    ?? pendingRange.RangeStart,
                _ => _historySelectedDate
            };

            if (resolvedDate != _historySelectedDate)
            {
                _historySelectedDate = resolvedDate;
                _historyDisplayedMonth = new DateOnly(resolvedDate.Year, resolvedDate.Month, 1);
            }

            _pendingHistorySelectionRange = null;
        }

        ApplyHistorySelection();
    }

    private void ApplyHistorySelection()
    {
        _allHistoryDailyRecords = BuildMergedHistoryDailyRecords();
        var customSelectedDays = GetCustomSelectedDays();
        _historyDailyRecords = SelectHistoryRecords(_allHistoryDailyRecords, _selectedHistoryDimension, _historySelectedDate, customSelectedDays).ToList();
        _historySummary = BuildHistorySummary(_historyDailyRecords, Snapshot, _selectedHistoryDimension, _historySelectedDate, customSelectedDays);
        RefreshHistoryCalendar();
        RaiseHistoryProperties();
        QueueHistoryChartRefresh();
    }

    private List<DailyProcessActivitySummary> BuildMergedHistoryDailyRecords()
    {
        var records = _historyDatabaseDailyRecords
            .OrderBy(record => record.Day, StringComparer.Ordinal)
            .ToList();
        MergeTodayRecord(records);
        return records
            .OrderBy(record => record.Day, StringComparer.Ordinal)
            .ToList();
    }

    private void RefreshHistoryCalendar()
    {
        var firstVisible = GetWeekStart(_historyDisplayedMonth);
        var selectedWeekStart = GetWeekStart(_historySelectedDate);
        var selectedWeekEnd = selectedWeekStart.AddDays(6);
        var monthStart = new DateOnly(_historySelectedDate.Year, _historySelectedDate.Month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        var daysWithData = _allHistoryDailyRecords
            .Select(record => DateOnly.Parse(record.Day, CultureInfo.InvariantCulture))
            .ToHashSet();

        EnsureHistoryCalendarDaySlots();

        for (var i = 0; i < 42; i++)
        {
            var date = firstVisible.AddDays(i);
            var isSelected = _selectedHistoryDimension switch
            {
                HistoryAnalysisDimension.Week => date >= selectedWeekStart && date <= selectedWeekEnd,
                HistoryAnalysisDimension.Month => date >= monthStart && date <= monthEnd,
                HistoryAnalysisDimension.Custom => _historyCustomSelectedDates.Contains(date),
                _ => date == _historySelectedDate
            };
            var isRangeStart = _selectedHistoryDimension == HistoryAnalysisDimension.Custom
                ? isSelected && !_historyCustomSelectedDates.Contains(date.AddDays(-1))
                : isSelected && (_selectedHistoryDimension == HistoryAnalysisDimension.Day || date == selectedWeekStart || date == monthStart);
            var isRangeEnd = _selectedHistoryDimension == HistoryAnalysisDimension.Custom
                ? isSelected && !_historyCustomSelectedDates.Contains(date.AddDays(1))
                : isSelected && (_selectedHistoryDimension == HistoryAnalysisDimension.Day || date == selectedWeekEnd || date == monthEnd);
            var isSelectable = date.Month == _historyDisplayedMonth.Month &&
                               date.Year == _historyDisplayedMonth.Year &&
                               daysWithData.Contains(date);

            HistoryCalendarDays[i].Update(
                date,
                isInDisplayedMonth: date.Month == _historyDisplayedMonth.Month && date.Year == _historyDisplayedMonth.Year,
                hasData: daysWithData.Contains(date),
                isSelected: isSelected,
                isRangeStart: isRangeStart,
                isRangeEnd: isRangeEnd,
                isSelectable: _selectedHistoryDimension != HistoryAnalysisDimension.Month && isSelectable);
        }
    }

    private void EnsureHistoryCalendarDaySlots()
    {
        while (HistoryCalendarDays.Count < 42)
        {
            HistoryCalendarDays.Add(new ApplicationHistoryCalendarDayViewModel(OnHistoryCalendarDateInvoked));
        }
    }

    private void MergeTodayRecord(List<DailyProcessActivitySummary> records)
    {
        var today = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var foregroundIoOperations = (long)Math.Round(
            Snapshot.AverageForegroundIops * Math.Max(0d, Snapshot.DailyForegroundMilliseconds) / 1000d,
            MidpointRounding.AwayFromZero);
        var backgroundIoOperations = (long)Math.Round(
            Snapshot.AverageBackgroundIops * Math.Max(0d, Snapshot.DailyBackgroundMilliseconds) / 1000d,
            MidpointRounding.AwayFromZero);
        var totalIoOperations = Math.Max(
            0L,
            (long)Math.Round(
                Snapshot.AverageIops * Math.Max(0d, Snapshot.DailyForegroundMilliseconds + Snapshot.DailyBackgroundMilliseconds) / 1000d,
                MidpointRounding.AwayFromZero));
        var totalIoBytes = Snapshot.DailyIoReadBytes + Snapshot.DailyIoWriteBytes;
        var ioReadOperations = totalIoBytes > 0
            ? (long)Math.Round(totalIoOperations * (Snapshot.DailyIoReadBytes / (double)totalIoBytes), MidpointRounding.AwayFromZero)
            : totalIoOperations / 2;
        var ioWriteOperations = Math.Max(0L, totalIoOperations - ioReadOperations);

        var todayRecord = new DailyProcessActivitySummary
        {
            Day = today,
            ProcessName = _application.OriginalName,
            ExecutablePath = Snapshot.ExecutablePath,
            ForegroundMilliseconds = Snapshot.DailyForegroundMilliseconds,
            BackgroundMilliseconds = Snapshot.DailyBackgroundMilliseconds,
            DownloadBytes = Snapshot.DailyDownloadBytes,
            UploadBytes = Snapshot.DailyUploadBytes,
            PeakDownloadBytesPerSecond = Snapshot.PeakDownloadBytesPerSecond,
            PeakUploadBytesPerSecond = Snapshot.PeakUploadBytesPerSecond,
            ForegroundCpuTotal = Snapshot.AverageForegroundCpu * Math.Max(1, Snapshot.IsForeground ? 1 : 0),
            ForegroundWorkingSetTotal = Snapshot.AverageForegroundWorkingSetBytes * Math.Max(1, Snapshot.IsForeground ? 1 : 0),
            ForegroundSamples = Snapshot.DailyForegroundMilliseconds > 0 ? 1 : 0,
            BackgroundCpuTotal = Snapshot.AverageBackgroundCpu * Math.Max(1, Snapshot.IsForeground ? 0 : 1),
            BackgroundWorkingSetTotal = Snapshot.AverageBackgroundWorkingSetBytes * Math.Max(1, Snapshot.IsForeground ? 0 : 1),
            BackgroundSamples = Snapshot.DailyBackgroundMilliseconds > 0 ? 1 : 0,
            PeakWorkingSetBytes = Snapshot.PeakWorkingSetBytes,
            ThreadCountTotal = Snapshot.AverageThreadCount,
            ThreadSamples = Snapshot.AverageThreadCount > 0 ? 1 : 0,
            PeakThreadCount = Snapshot.PeakThreadCount,
            IoReadBytes = Snapshot.DailyIoReadBytes,
            IoWriteBytes = Snapshot.DailyIoWriteBytes,
            ForegroundIoOperations = foregroundIoOperations,
            BackgroundIoOperations = backgroundIoOperations,
            IoReadOperations = ioReadOperations,
            IoWriteOperations = ioWriteOperations,
            PeakIoReadBytesPerSecond = Snapshot.PeakIoReadBytesPerSecond,
            PeakIoWriteBytesPerSecond = Snapshot.PeakIoWriteBytesPerSecond,
            PeakIoBytesPerSecond = Snapshot.PeakIoBytesPerSecond,
            HasMainWindow = Snapshot.HasMainWindow
        };

        var index = records.FindIndex(record => string.Equals(record.Day, today, StringComparison.Ordinal));
        if (index >= 0)
        {
            records[index] = todayRecord;
        }
        else
        {
            records.Add(todayRecord);
        }
    }

    private void OnApplicationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isDisposed)
        {
            return;
        }

        if (e.PropertyName == nameof(ApplicationCardViewModel.Snapshot))
        {
            _pendingApplicationRefresh = true;
            _pendingChartRefresh = true;
            if (_selectedDataMode == DetailDataMode.History)
            {
                _pendingHistoryRefresh = true;
            }
            EnsureRefreshTimerRunning();
            return;
        }

        if (e.PropertyName == nameof(ApplicationCardViewModel.DisplayName) ||
            e.PropertyName == nameof(ApplicationCardViewModel.OriginalName) ||
            e.PropertyName == nameof(ApplicationCardViewModel.IconSourcePath) ||
            e.PropertyName == nameof(ApplicationCardViewModel.StateDisplay))
        {
            _pendingStaticApplicationRefresh = true;
            EnsureRefreshTimerRunning();
        }
    }

    private async void OnRefreshTimerTick(object? sender, EventArgs e)
    {
        if (_isDisposed)
        {
            return;
        }

        _refreshTimer.Stop();

        if (_pendingStaticApplicationRefresh)
        {
            _pendingStaticApplicationRefresh = false;
            if (_isWindowRenderingActive)
            {
                RaiseStaticApplicationProperties();
            }
        }

        if (_pendingApplicationRefresh)
        {
            _pendingApplicationRefresh = false;
            AppendCurrentSamples(DateTime.UtcNow);
            if (_isWindowRenderingActive)
            {
                RaiseLiveApplicationProperties();
            }
        }

        if (_pendingHistoryRefresh && _isWindowRenderingActive && _selectedDataMode == DetailDataMode.History)
        {
            _pendingHistoryRefresh = false;
            if (HistorySelectionIncludesToday())
            {
                if (_hasLoadedHistoryDatabaseRecords)
                {
                    ApplyHistorySelection();
                }
                else
                {
                    LoadHistorySummary();
                }
            }
        }

        if (_pendingChartRefresh && _isWindowRenderingActive && _selectedDataMode == DetailDataMode.Current)
        {
            _pendingChartRefresh = false;
            await RefreshChartsAsync();
        }

        if (_pendingHistoryChartRefresh && _isWindowRenderingActive && _selectedDataMode == DetailDataMode.History)
        {
            _pendingHistoryChartRefresh = false;
            await RefreshHistoryChartsAsync();
        }

        var shouldContinueTimer =
            _pendingStaticApplicationRefresh ||
            _pendingApplicationRefresh ||
            (_pendingChartRefresh && _isWindowRenderingActive && _selectedDataMode == DetailDataMode.Current) ||
            (_pendingHistoryRefresh && _isWindowRenderingActive && _selectedDataMode == DetailDataMode.History) ||
            (_pendingHistoryChartRefresh && _isWindowRenderingActive && _selectedDataMode == DetailDataMode.History);

        if (shouldContinueTimer)
        {
            EnsureRefreshTimerRunning();
        }
    }

    private void QueueChartRefresh()
    {
        if (_isDisposed)
        {
            return;
        }

        _pendingChartRefresh = true;
        EnsureRefreshTimerRunning();
    }

    private void QueueHistoryChartRefresh()
    {
        if (_isDisposed)
        {
            return;
        }

        _pendingHistoryChartRefresh = true;
        EnsureRefreshTimerRunning();
    }

    private void EnsureRefreshTimerRunning()
    {
        if (!_isDisposed && !_refreshTimer.IsEnabled)
        {
            _refreshTimer.Start();
        }
    }

    private void OnPreferencesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isDisposed)
        {
            return;
        }

        RaisePreferenceDependentProperties();
        QueueChartRefresh();
    }

    private void RaiseApplicationDependentProperties()
    {
        RaiseStaticApplicationProperties();
        RaiseLiveApplicationProperties();
    }

    private void RaiseStaticApplicationProperties()
    {
        RaisePropertyChanged(nameof(DisplayName));
        RaisePropertyChanged(nameof(OriginalName));
        RaisePropertyChanged(nameof(DisplayNameWithOriginal));
        RaisePropertyChanged(nameof(IconSourcePath));
        RaisePropertyChanged(nameof(StateDisplay));
        RaisePropertyChanged(nameof(HeaderCaptionDisplay));
        RaisePropertyChanged(nameof(HeaderCaptionFontSize));
        RaisePropertyChanged(nameof(HeaderCaptionFontWeight));
        RaisePropertyChanged(nameof(HeaderTitleDisplay));
        RaisePropertyChanged(nameof(HeaderTitleFontSize));
        RaisePropertyChanged(nameof(HeaderTitleFontWeight));
        RaisePropertyChanged(nameof(HeaderExecutablePathDisplay));
        RaisePropertyChanged(nameof(ProcessCountDisplay));
        RaisePropertyChanged(nameof(ProcessIdDisplay));
        RaisePropertyChanged(nameof(ExecutablePathDisplay));
        NotifyHeaderActionCommandStateChanged();
    }

    private void RaiseLiveApplicationProperties()
    {
        RaisePropertyChanged(nameof(TodayFocusRatio));
        RaisePropertyChanged(nameof(CpuDisplay));
        RaisePropertyChanged(nameof(WorkingSetDisplay));
        RaisePropertyChanged(nameof(PeakWorkingSetDisplay));
        RaisePropertyChanged(nameof(PrivateMemoryDisplay));
        RaisePropertyChanged(nameof(CommitSizeDisplay));
        RaisePropertyChanged(nameof(ThreadCountDisplay));
        RaisePropertyChanged(nameof(ThreadAverageDisplay));
        RaisePropertyChanged(nameof(PeakThreadDisplay));
        RaisePropertyChanged(nameof(ThreadPeakMeanRatioDisplay));
        RaisePropertyChanged(nameof(RealtimeIopsDisplay));
        RaisePropertyChanged(nameof(AverageIopsDisplay));
        RaisePropertyChanged(nameof(IoReadWriteRatioDisplay));
        RaisePropertyChanged(nameof(ForegroundDurationDisplay));
        RaisePropertyChanged(nameof(BackgroundDurationDisplay));
        RaisePropertyChanged(nameof(AverageForegroundCpuDisplay));
        RaisePropertyChanged(nameof(AverageForegroundWorkingSetDisplay));
        RaisePropertyChanged(nameof(AverageForegroundIopsDisplay));
        RaisePropertyChanged(nameof(AverageBackgroundCpuDisplay));
        RaisePropertyChanged(nameof(AverageBackgroundWorkingSetDisplay));
        RaisePropertyChanged(nameof(AverageBackgroundIopsDisplay));
        RaisePropertyChanged(nameof(RealtimeTrafficDisplay));
        RaisePropertyChanged(nameof(DailyTrafficDisplay));
        RaisePropertyChanged(nameof(RealtimeDownloadDisplay));
        RaisePropertyChanged(nameof(RealtimeUploadDisplay));
        RaisePropertyChanged(nameof(DailyDownloadDisplay));
        RaisePropertyChanged(nameof(DailyUploadDisplay));
        RaisePropertyChanged(nameof(PeakTrafficDisplay));
        RaisePropertyChanged(nameof(PeakDownloadDisplay));
        RaisePropertyChanged(nameof(PeakUploadDisplay));
        RaisePropertyChanged(nameof(RealtimeIoDisplay));
        RaisePropertyChanged(nameof(DailyIoDisplay));
        RaisePropertyChanged(nameof(RealtimeIoReadDisplay));
        RaisePropertyChanged(nameof(RealtimeIoWriteDisplay));
        RaisePropertyChanged(nameof(DailyIoReadDisplay));
        RaisePropertyChanged(nameof(DailyIoWriteDisplay));
        RaisePropertyChanged(nameof(PeakIoDisplay));
        RaisePropertyChanged(nameof(PeakIoReadDisplay));
        RaisePropertyChanged(nameof(PeakIoWriteDisplay));
    }

    private void RaisePreferenceDependentProperties()
    {
        RaisePropertyChanged(nameof(SelectedNetworkDisplayOption));
        RaisePropertyChanged(nameof(SelectedIoDisplayOption));
        RaisePropertyChanged(nameof(SelectedForegroundBackgroundOption));
        RaisePropertyChanged(nameof(SelectedChartScaleOption));
        RaisePropertyChanged(nameof(SelectedHistoryOverlayOption));
        RaisePropertyChanged(nameof(IsNetworkHiddenMode));
        RaisePropertyChanged(nameof(IsNetworkTotalMode));
        RaisePropertyChanged(nameof(IsNetworkSplitMode));
        RaisePropertyChanged(nameof(IsIoHiddenMode));
        RaisePropertyChanged(nameof(IsIoTotalMode));
        RaisePropertyChanged(nameof(IsIoSplitMode));
        RaisePropertyChanged(nameof(IsChartScale30SecondsMode));
        RaisePropertyChanged(nameof(IsChartScale1MinuteMode));
        RaisePropertyChanged(nameof(IsChartScale2MinutesMode));
        RaisePropertyChanged(nameof(IsHistoryOverlayOffMode));
        RaisePropertyChanged(nameof(IsHistoryOverlayUsageDurationMode));
        RaisePropertyChanged(nameof(IsHistoryOverlayForegroundDurationMode));
        RaisePropertyChanged(nameof(ShowNetworkHiddenNotice));
        RaisePropertyChanged(nameof(ShowNetworkTotals));
        RaisePropertyChanged(nameof(ShowNetworkSplit));
        RaisePropertyChanged(nameof(ShowNetworkChart));
        RaisePropertyChanged(nameof(ShowIoHiddenNotice));
        RaisePropertyChanged(nameof(ShowIoTotals));
        RaisePropertyChanged(nameof(ShowIoSplit));
        RaisePropertyChanged(nameof(ShowIoChart));
        RaisePropertyChanged(nameof(ShowForegroundBackgroundDetails));
        RaisePropertyChanged(nameof(ShowNetworkLegend));
        RaisePropertyChanged(nameof(ShowIoLegend));
        RaisePropertyChanged(nameof(NetworkChartTitle));
        RaisePropertyChanged(nameof(IoChartTitle));
        RaisePropertyChanged(nameof(HistoryNetworkChartTitle));
        RaisePropertyChanged(nameof(HistoryIoChartTitle));
        RaisePropertyChanged(nameof(ChartXAxisStartLabel));
        RaisePropertyChanged(nameof(ChartXAxisCenterLabel));
        RaisePropertyChanged(nameof(ChartXAxisEndLabel));
        RaisePropertyChanged(nameof(HistoryTrafficDisplay));
        RaisePropertyChanged(nameof(HistoryIoDisplay));
        QueueHistoryChartRefresh();
    }

    private bool HistorySelectionIncludesToday()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        return _selectedHistoryDimension switch
        {
            HistoryAnalysisDimension.Week =>
                today >= GetWeekStart(_historySelectedDate) &&
                today <= GetWeekStart(_historySelectedDate).AddDays(6),
            HistoryAnalysisDimension.Month =>
                today.Year == _historySelectedDate.Year &&
                today.Month == _historySelectedDate.Month,
            HistoryAnalysisDimension.Custom => _historyCustomSelectedDates.Contains(today),
            _ => _historySelectedDate == today
        };
    }

    private void RaiseDataModeProperties()
    {
        RaisePropertyChanged(nameof(IsCurrentDataMode));
        RaisePropertyChanged(nameof(IsHistoryDataMode));
    }

    private void RaiseHistoryProperties()
    {
        RaiseDataModeProperties();
        RaisePropertyChanged(nameof(IsHistoryDayDimension));
        RaisePropertyChanged(nameof(IsHistoryWeekDimension));
        RaisePropertyChanged(nameof(IsHistoryMonthDimension));
        RaisePropertyChanged(nameof(IsHistoryCustomDimension));
        RaisePropertyChanged(nameof(HistoryDimensionTitle));
        RaisePropertyChanged(nameof(HistorySelectionDisplay));
        RaisePropertyChanged(nameof(SelectedHistoryDateTime));
        RaisePropertyChanged(nameof(HistoryCalendarMonthDisplay));
        RaisePropertyChanged(nameof(HistoryRangeDisplay));
        RaisePropertyChanged(nameof(HistoryActiveDaysDisplay));
        RaisePropertyChanged(nameof(HistoryUsageDisplay));
        RaisePropertyChanged(nameof(HistoryForegroundDurationDisplay));
        RaisePropertyChanged(nameof(HistoryBackgroundDurationDisplay));
        RaisePropertyChanged(nameof(HistoryForegroundRatioDisplay));
        RaisePropertyChanged(nameof(HistoryTrafficDisplay));
        RaisePropertyChanged(nameof(HistoryIoDisplay));
        RaisePropertyChanged(nameof(HistoryAverageCpuDisplay));
        RaisePropertyChanged(nameof(HistoryAverageIopsDisplay));
        RaisePropertyChanged(nameof(HistoryMemoryDisplay));
        RaisePropertyChanged(nameof(HistoryThreadDisplay));
        RaisePropertyChanged(nameof(HistoryAverageForegroundCpuDisplay));
        RaisePropertyChanged(nameof(HistoryAverageForegroundMemoryDisplay));
        RaisePropertyChanged(nameof(HistoryAverageForegroundIopsDisplay));
        RaisePropertyChanged(nameof(HistoryAverageBackgroundCpuDisplay));
        RaisePropertyChanged(nameof(HistoryAverageBackgroundMemoryDisplay));
        RaisePropertyChanged(nameof(HistoryAverageBackgroundIopsDisplay));
        RaisePropertyChanged(nameof(HeaderTitleDisplay));
        RaisePropertyChanged(nameof(HistoryExecutablePathDisplay));
        RaisePropertyChanged(nameof(HeaderExecutablePathDisplay));
        RaisePropertyChanged(nameof(HistoryNetworkChartTitle));
        RaisePropertyChanged(nameof(HistoryIoChartTitle));
        RaisePropertyChanged(nameof(HistoryChartXAxisStartLabel));
        RaisePropertyChanged(nameof(HistoryChartXAxisEndLabel));
        RaisePropertyChanged(nameof(HistoryChartDisplayWidth));
        NotifyHeaderActionCommandStateChanged();
    }

    private void NotifyHeaderActionCommandStateChanged()
    {
        _openOnlineSearchCommand.NotifyCanExecuteChanged();
        _openExecutablePropertiesCommand.NotifyCanExecuteChanged();
    }

    private bool CanOpenOnlineSearch()
    {
        return !string.IsNullOrWhiteSpace(BuildOnlineSearchKeyword());
    }

    private void OpenOnlineSearch()
    {
        var keyword = BuildOnlineSearchKeyword();
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = $"https://www.bing.com/search?q={Uri.EscapeDataString(keyword)}",
                UseShellExecute = true
            });
        }
        catch
        {
        }
    }

    private string BuildOnlineSearchKeyword()
    {
        var displayName = _application.DisplayName?.Trim() ?? string.Empty;
        var originalName = _application.OriginalName?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(displayName))
        {
            return originalName;
        }

        if (string.IsNullOrWhiteSpace(originalName) || string.Equals(displayName, originalName, StringComparison.OrdinalIgnoreCase))
        {
            return displayName;
        }

        return $"{displayName} {originalName}";
    }

    private bool CanOpenExecutableProperties()
    {
        return TryResolveExecutablePathForActions(out _);
    }

    private void OpenExecutableProperties()
    {
        if (!TryResolveExecutablePathForActions(out var executablePath))
        {
            return;
        }

        try
        {
            var executeInfo = new ShellExecuteInfo
            {
                cbSize = Marshal.SizeOf<ShellExecuteInfo>(),
                fMask = ShellExecuteInvokeIdListMask,
                lpVerb = "properties",
                lpFile = executablePath,
                nShow = ShellShowNormal
            };

            ShellExecuteEx(ref executeInfo);
        }
        catch
        {
        }
    }

    private bool TryResolveExecutablePathForActions(out string executablePath)
    {
        foreach (var candidate in new[]
                 {
                     Snapshot.ExecutablePath,
                     _historySummary.ExecutablePathDisplay
                 })
        {
            if (string.IsNullOrWhiteSpace(candidate) || candidate == "-")
            {
                continue;
            }

            var normalizedPath = Environment.ExpandEnvironmentVariables(candidate.Trim());
            if (File.Exists(normalizedPath))
            {
                executablePath = normalizedPath;
                return true;
            }
        }

        executablePath = string.Empty;
        return false;
    }

    public void ActivateHistoryMode()
    {
        ShowHistoryData();
    }

    public void ApplyHistorySelectionFromDashboard(string dimensionKey, DateOnly selectedDate, IReadOnlyCollection<DateOnly>? customSelectedDates)
    {
        var targetDimension = ParseHistoryDimension(dimensionKey);
        _pendingHistorySelectionRange = null;

        if (targetDimension == HistoryAnalysisDimension.Custom)
        {
            _historyCustomSelectedDates.Clear();
            foreach (var date in (customSelectedDates ?? []).OrderBy(static date => date))
            {
                _historyCustomSelectedDates.Add(date);
            }

            if (_historyCustomSelectedDates.Count == 0)
            {
                _historyCustomSelectedDates.Add(selectedDate);
            }

            var anchorDate = GetHistoryCustomSelectionAnchor();
            _historySelectedDate = anchorDate;
            _historyDisplayedMonth = new DateOnly(anchorDate.Year, anchorDate.Month, 1);
        }
        else
        {
            _historySelectedDate = targetDimension switch
            {
                HistoryAnalysisDimension.Week => GetWeekStart(selectedDate),
                HistoryAnalysisDimension.Month => new DateOnly(selectedDate.Year, selectedDate.Month, 1),
                _ => selectedDate
            };
            _historyDisplayedMonth = new DateOnly(_historySelectedDate.Year, _historySelectedDate.Month, 1);
        }

        _selectedHistoryDimension = targetDimension;
        ApplyHistorySelection();
    }

    private static HistoryAnalysisDimension ParseHistoryDimension(string dimensionKey) =>
        dimensionKey switch
        {
            "week" => HistoryAnalysisDimension.Week,
            "month" => HistoryAnalysisDimension.Month,
            "custom" => HistoryAnalysisDimension.Custom,
            _ => HistoryAnalysisDimension.Day
        };

    private static ApplicationCardViewModel CreateHistoryOnlyApplicationCard(
        string processName,
        string displayName,
        string executablePath,
        string? iconSourcePath)
    {
        var snapshot = new ProcessResourceSnapshot
        {
            ProcessName = processName,
            ExecutablePath = executablePath ?? string.Empty,
            IconCachePath = iconSourcePath ?? string.Empty
        };
        var customName = string.Equals(displayName, processName, StringComparison.Ordinal)
            ? null
            : displayName;

        return new ApplicationCardViewModel(
            snapshot,
            customName,
            static (_, _) => { },
            static _ => { },
            new ApplicationCardMetricPreferences());
    }

    private void AppendCurrentSamples(DateTime timestampUtc)
    {
        _networkHistory.Add(new TimedMetricSample(
            timestampUtc,
            Snapshot.RealtimeDownloadBytesPerSecond,
            Snapshot.RealtimeUploadBytesPerSecond));

        _ioHistory.Add(new TimedMetricSample(
            timestampUtc,
            Snapshot.RealtimeIoReadBytesPerSecond,
            Snapshot.RealtimeIoWriteBytesPerSecond));

        TrimHistory(_networkHistory, timestampUtc);
        TrimHistory(_ioHistory, timestampUtc);
    }

    private static void TrimHistory(List<TimedMetricSample> history, DateTime nowUtc)
    {
        var cutoff = nowUtc.AddSeconds(-MaxHistorySeconds - 5);
        history.RemoveAll(sample => sample.TimestampUtc < cutoff);
    }

    private async Task RefreshChartsAsync()
    {
        var renderVersion = Interlocked.Increment(ref _chartRenderVersion);
        var networkHistory = _networkHistory.ToArray();
        var ioHistory = _ioHistory.ToArray();
        var historySeconds = HistorySeconds;
        var showNetworkChart = ShowNetworkChart;
        var showIoChart = ShowIoChart;
        var isNetworkSplit = _preferences.IsNetworkSplit;
        var isIoSplit = _preferences.IsIoSplit;

        var chartResult = await Task.Run(() => new ChartRenderResult(
            showNetworkChart ? BuildChartImage(networkHistory, historySeconds, isNetworkSplit, isNetwork: true) : SingleChartRenderResult.Empty,
            showIoChart ? BuildChartImage(ioHistory, historySeconds, isIoSplit, isNetwork: false) : SingleChartRenderResult.Empty));

        if (_isDisposed || renderVersion != Volatile.Read(ref _chartRenderVersion))
        {
            return;
        }

        NetworkChartSource = UpdateChartSource(NetworkChartSource, chartResult.NetworkChart);
        NetworkChartTopLabel = chartResult.NetworkChart.TopLabel;
        IoChartSource = UpdateChartSource(IoChartSource, chartResult.IoChart);
        IoChartTopLabel = chartResult.IoChart.TopLabel;
    }

    private static SingleChartRenderResult BuildChartImage(TimedMetricSample[] history, int historySeconds, bool splitMode, bool isNetwork)
    {
        var nowUtc = DateTime.UtcNow;
        var points = history
            .Select(sample => new
            {
                X = historySeconds - Math.Max(0d, (nowUtc - sample.TimestampUtc).TotalSeconds),
                sample.PrimaryValue,
                sample.SecondaryValue
            })
            .Where(sample => sample.X >= 0d && sample.X <= historySeconds)
            .OrderBy(static sample => sample.X)
            .ToArray();

        if (points.Length == 0)
        {
            return SingleChartRenderResult.Empty;
        }

        var xs = points.Select(static sample => sample.X).ToArray();
        var primary = points.Select(static sample => sample.PrimaryValue).ToArray();
        var secondary = points.Select(static sample => sample.SecondaryValue).ToArray();
        var total = primary.Zip(secondary, static (left, right) => left + right).ToArray();
        var maxValue = splitMode
            ? Math.Max(primary.Max(), secondary.Max())
            : total.Max();
        var yAxisMax = maxValue > 0d ? maxValue * 1.1d : 1d;

        var plot = new Plot();
        plot.FigureBackground.Color = ScottPlot.Color.FromHex("#FFFDF9");
        plot.DataBackground.Color = ScottPlot.Color.FromHex(isNetwork ? "#FFF8EF" : "#F7FBF6");
        plot.Axes.Bottom.Label.Text = string.Empty;
        plot.Axes.Left.Label.Text = string.Empty;
        plot.Axes.Bottom.TickLabelStyle.IsVisible = false;
        plot.Axes.Left.TickLabelStyle.IsVisible = false;
        plot.Axes.Bottom.MajorTickStyle.Length = 0;
        plot.Axes.Left.MajorTickStyle.Length = 0;
        plot.Axes.Frame(false);
        plot.Axes.SetLimitsX(0, historySeconds);
        plot.Axes.SetLimitsY(0, yAxisMax);
        plot.Axes.Margins(bottom: 0.02, top: 0.04, left: 0, right: 0);

        if (splitMode)
        {
            var first = plot.Add.Scatter(xs, primary);
            first.Color = ScottPlot.Color.FromHex(isNetwork ? "#2D8CFF" : "#17766C");
            first.LineWidth = 2;
            first.MarkerSize = 0;
            var second = plot.Add.Scatter(xs, secondary);
            second.Color = ScottPlot.Color.FromHex(isNetwork ? "#FF8A3D" : "#D06A43");
            second.LineWidth = 2;
            second.MarkerSize = 0;
        }
        else
        {
            var line = plot.Add.Scatter(xs, total);
            line.Color = ScottPlot.Color.FromHex(isNetwork ? "#2A6FBB" : "#176B5A");
            line.LineWidth = 2;
            line.MarkerSize = 0;
        }

        using var surface = SKSurface.Create(new SKImageInfo(ChartWidth, ChartHeight, SKColorType.Bgra8888, SKAlphaType.Premul));
        plot.Render(surface);
        return new SingleChartRenderResult(CreateBitmapBuffer(surface, ChartWidth, ChartHeight), FormatRate(yAxisMax));
    }

    private async Task RefreshHistoryChartsAsync()
    {
        var renderVersion = Interlocked.Increment(ref _historyChartRenderVersion);
        var records = _historyDailyRecords
            .TakeLast(HistoryChartDays)
            .ToArray();
        var isNetworkSplit = _preferences.IsNetworkSplit;
        var isIoSplit = _preferences.IsIoSplit;
        var historyOverlayOption = _preferences.HistoryOverlayOption;
        var renderWidth = GetHistoryChartRenderWidth(records.Length, _historyChartViewportWidth);
        var showOverlayRightAxis = renderWidth > _historyChartViewportWidth + 1d;

        var chartResult = await Task.Run(() => new ChartRenderResult(
            BuildHistoryChartImage(records, isNetworkSplit, isNetwork: true, _selectedHistoryDimension, renderWidth, historyOverlayOption, showOverlayRightAxis),
            BuildHistoryChartImage(records, isIoSplit, isNetwork: false, _selectedHistoryDimension, renderWidth, historyOverlayOption, showOverlayRightAxis)));

        if (_isDisposed || renderVersion != Volatile.Read(ref _historyChartRenderVersion))
        {
            return;
        }

        HistoryNetworkChartSource = UpdateChartSource(HistoryNetworkChartSource, chartResult.NetworkChart);
        HistoryNetworkChartTopLabel = chartResult.NetworkChart.TopLabel;
        HistoryIoChartSource = UpdateChartSource(HistoryIoChartSource, chartResult.IoChart);
        HistoryIoChartTopLabel = chartResult.IoChart.TopLabel;
        RaisePropertyChanged(nameof(HistoryChartXAxisStartLabel));
        RaisePropertyChanged(nameof(HistoryChartXAxisEndLabel));
    }

    private static SingleChartRenderResult BuildHistoryChartImage(
        DailyProcessActivitySummary[] records,
        bool splitMode,
        bool isNetwork,
        HistoryAnalysisDimension dimension,
        int renderWidth,
        string historyOverlayOption,
        bool showOverlayRightAxis)
    {
        if (records.Length == 0)
        {
            return SingleChartRenderResult.Empty with { TopLabel = "1 KB" };
        }

        var primary = records
            .Select(record => isNetwork ? (double)record.DownloadBytes : record.IoReadBytes)
            .ToArray();
        var secondary = records
            .Select(record => isNetwork ? (double)record.UploadBytes : record.IoWriteBytes)
            .ToArray();
        var total = primary.Zip(secondary, static (left, right) => left + right).ToArray();
        var maxValue = splitMode
            ? Math.Max(primary.Max(), secondary.Max())
            : total.Max();
        var yAxisMax = maxValue > 0d ? maxValue * 1.1d : 1d;
        var overlayValues = BuildHistoryOverlayValues(records, historyOverlayOption);

        using var surface = SKSurface.Create(new SKImageInfo(renderWidth, HistoryChartHeight, SKColorType.Bgra8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColor.Parse(isNetwork ? "#FFF8EF" : "#F7FBF6"));

        var hasOverlayAxis = overlayValues.Length > 0;
        var plotLeft = hasOverlayAxis ? 50f : 26f;
        var plotRight = renderWidth - (showOverlayRightAxis ? 50f : 14f);
        var plotRect = new SKRect(plotLeft, 14, plotRight, HistoryChartHeight - 30);
        using var gridPaint = new SKPaint
        {
            Color = SKColor.Parse(isNetwork ? "#EEDFCF" : "#DCE9E1"),
            IsAntialias = true,
            StrokeWidth = 1
        };
        using var primaryPaint = new SKPaint
        {
            Color = SKColor.Parse(isNetwork ? "#2D8CFF" : "#17766C"),
            IsAntialias = true
        };
        using var secondaryPaint = new SKPaint
        {
            Color = SKColor.Parse(isNetwork ? "#FF8A3D" : "#D06A43"),
            IsAntialias = true
        };
        using var totalPaint = new SKPaint
        {
            Color = SKColor.Parse(isNetwork ? "#2A6FBB" : "#176B5A"),
            IsAntialias = true
        };
        using var overlayLinePaint = new SKPaint
        {
            Color = SKColor.Parse(isNetwork ? "#AA6A00" : "#3C5E8C").WithAlpha(105),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2.2f
        };
        using var overlayPointPaint = new SKPaint
        {
            Color = SKColor.Parse(isNetwork ? "#AA6A00" : "#3C5E8C").WithAlpha(120),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
        using var overlayAxisTextPaint = new SKPaint
        {
            Color = SKColor.Parse("#6B6F68").WithAlpha(205),
            IsAntialias = true
        };
        using var overlayAxisFont = CreateSkFont("Microsoft YaHei UI", 10, SKFontStyleWeight.Normal);
        using var overlayAxisBackgroundPaint = new SKPaint
        {
            Color = SKColor.Parse(isNetwork ? "#FFF8EF" : "#F7FBF6").WithAlpha(225),
            IsAntialias = true
        };
        using var labelTextPaint = new SKPaint
        {
            Color = SKColor.Parse("#223B35"),
            IsAntialias = true
        };
        using var labelFont = CreateSkFont("Microsoft YaHei UI", 11, SKFontStyleWeight.SemiBold);
        using var splitLabelTextPaint = new SKPaint
        {
            Color = SKColor.Parse("#223B35"),
            IsAntialias = true
        };
        using var splitLabelFont = CreateSkFont("Microsoft YaHei UI", 9, SKFontStyleWeight.SemiBold);
        using var labelBackgroundPaint = new SKPaint
        {
            Color = SKColor.Parse("#FDF8F0"),
            IsAntialias = true
        };
        using var labelBorderPaint = new SKPaint
        {
            Color = SKColor.Parse("#D9D0C0"),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1
        };
        using var dateTextPaint = new SKPaint
        {
            Color = SKColor.Parse("#6F766E"),
            IsAntialias = true
        };
        using var dateFont = CreateSkFont("Microsoft YaHei UI", records.Length <= 10 ? 15 : 13);

        for (var i = 0; i < 4; i++)
        {
            var y = plotRect.Top + (plotRect.Height / 3f) * i;
            canvas.DrawLine(plotRect.Left, y, plotRect.Right, y, gridPaint);
        }

        var slotWidth = plotRect.Width / Math.Max(records.Length, 1);
        var widthFactor = dimension == HistoryAnalysisDimension.Day
            ? 0.56f
            : records.Length <= HistoryChartVisibleBars ? 0.58f : 0.5f;
        var maxWidth = dimension == HistoryAnalysisDimension.Day ? 58f : 52f;
        var groupWidth = Math.Max(4f, Math.Min(maxWidth, slotWidth * widthFactor));

        for (var i = 0; i < records.Length; i++)
        {
            var left = plotRect.Left + i * slotWidth + (slotWidth - groupWidth) / 2f;
            var weekTint = dimension == HistoryAnalysisDimension.Month
                ? (DateOnly.Parse(records[i].Day, CultureInfo.InvariantCulture).Day - 1) / 7
                : 0;

            if (splitMode)
            {
                var gap = Math.Max(1.5f, groupWidth * 0.12f);
                var barWidth = (groupWidth - gap) / 2f;
                primaryPaint.Color = GetHistoryBarColor(isNetwork ? "#2D8CFF" : "#17766C", weekTint);
                secondaryPaint.Color = GetHistoryBarColor(isNetwork ? "#FF8A3D" : "#D06A43", weekTint);
                DrawHistoryBar(canvas, primaryPaint, left, barWidth, primary[i], yAxisMax, plotRect);
                DrawHistoryBar(canvas, secondaryPaint, left + barWidth + gap, barWidth, secondary[i], yAxisMax, plotRect);

                var primaryLabelRect = DrawHistoryBarLabel(
                    canvas,
                    labelBackgroundPaint,
                    labelBorderPaint,
                    splitLabelTextPaint,
                    splitLabelFont,
                    left,
                    barWidth,
                    primary[i],
                    yAxisMax,
                    plotRect,
                    compactMode: true);
                DrawHistoryBarLabel(
                    canvas,
                    labelBackgroundPaint,
                    labelBorderPaint,
                    splitLabelTextPaint,
                    splitLabelFont,
                    left + barWidth + gap,
                    barWidth,
                    secondary[i],
                    yAxisMax,
                    plotRect,
                    compactMode: true,
                    avoidRect: primaryLabelRect);

                DrawHistoryDateLabel(canvas, dateTextPaint, dateFont, records[i].Day, left + groupWidth / 2f, plotRect.Bottom + 24, records.Length);
            }
            else
            {
                totalPaint.Color = GetHistoryBarColor(isNetwork ? "#2A6FBB" : "#176B5A", weekTint);
                DrawHistoryBar(canvas, totalPaint, left, groupWidth, total[i], yAxisMax, plotRect);

                DrawHistoryBarLabel(
                    canvas,
                    labelBackgroundPaint,
                    labelBorderPaint,
                    labelTextPaint,
                    labelFont,
                    left,
                    groupWidth,
                    total[i],
                    yAxisMax,
                    plotRect);

                DrawHistoryDateLabel(canvas, dateTextPaint, dateFont, records[i].Day, left + groupWidth / 2f, plotRect.Bottom + 24, records.Length);
            }
        }

        DrawHistoryOverlayLine(canvas, overlayLinePaint, overlayPointPaint, overlayValues, plotRect);
        DrawHistoryOverlayAxisLabels(canvas, overlayAxisTextPaint, overlayAxisFont, overlayAxisBackgroundPaint, overlayValues, plotRect, renderWidth, showOverlayRightAxis);

        return new SingleChartRenderResult(CreateBitmapBuffer(surface, renderWidth, HistoryChartHeight), FormatBytes(yAxisMax));
    }

    private static ChartBitmapBuffer CreateBitmapBuffer(SKSurface surface, int width, int height)
    {
        using var image = surface.Snapshot();
        using var bitmap = SKBitmap.FromImage(image);
        var pixels = new byte[bitmap.ByteCount];
        Marshal.Copy(bitmap.GetPixels(), pixels, 0, pixels.Length);
        return new ChartBitmapBuffer(width, height, bitmap.RowBytes, pixels);
    }

    private static ImageSource? UpdateChartSource(ImageSource? existingSource, SingleChartRenderResult chart)
    {
        if (chart.Buffer is null)
        {
            return null;
        }

        var buffer = chart.Buffer;
        WriteableBitmap bitmap;
        if (existingSource is WriteableBitmap existingBitmap &&
            existingBitmap.PixelWidth == buffer.Width &&
            existingBitmap.PixelHeight == buffer.Height &&
            existingBitmap.Format == PixelFormats.Pbgra32)
        {
            bitmap = existingBitmap;
        }
        else
        {
            bitmap = new WriteableBitmap(buffer.Width, buffer.Height, 96, 96, PixelFormats.Pbgra32, null);
        }

        bitmap.WritePixels(
            new System.Windows.Int32Rect(0, 0, buffer.Width, buffer.Height),
            buffer.Pixels,
            buffer.Stride,
            0);
        return bitmap;
    }

    private static int GetHistoryChartRenderWidth(int recordCount, double viewportWidth)
    {
        var safeViewportWidth = Math.Max(240d, viewportWidth);
        var safeCount = Math.Max(recordCount, 1);
        if (safeCount <= HistoryChartVisibleBars)
        {
            return (int)Math.Ceiling(safeViewportWidth);
        }

        return (int)Math.Ceiling(safeViewportWidth / HistoryChartVisibleBars * safeCount);
    }

    private static void DrawHistoryBar(
        SKCanvas canvas,
        SKPaint paint,
        float left,
        float width,
        double value,
        double maxValue,
        SKRect plotRect)
    {
        if (value <= 0 || maxValue <= 0)
        {
            return;
        }

        const float labelHeadroom = 24f;
        var drawableHeight = Math.Max(0f, plotRect.Height - labelHeadroom);
        var height = (float)(value / maxValue * drawableHeight);
        var rect = new SKRect(left, plotRect.Bottom - height, left + width, plotRect.Bottom);
        canvas.DrawRoundRect(rect, 4, 4, paint);
    }

    private static SKRect? DrawHistoryBarLabel(
        SKCanvas canvas,
        SKPaint backgroundPaint,
        SKPaint borderPaint,
        SKPaint textPaint,
        SKFont textFont,
        float left,
        float width,
        double value,
        double maxValue,
        SKRect plotRect,
        bool compactMode = false,
        SKRect? avoidRect = null)
    {
        if (maxValue <= 0)
        {
            return null;
        }

        var label = value <= 0
            ? "0"
            : compactMode
                ? FormatCompactBytes(value)
                : FormatBytes(value);
        var bounds = MeasureTextBounds(textFont, label);
        var paddingX = compactMode ? 4f : 6f;
        var labelWidth = bounds.Width + paddingX * 2;
        var labelHeight = compactMode ? 14f : 18f;
        var labelGap = compactMode ? 2f : 4f;
        var labelHeadroom = compactMode ? 18f : 24f;
        var drawableHeight = Math.Max(0f, plotRect.Height - labelHeadroom);
        var normalizedValue = Math.Max(0d, value);
        var height = (float)(normalizedValue / maxValue * drawableHeight);
        var centerX = left + width / 2f;
        var labelTop = Math.Max(plotRect.Top + 2, plotRect.Bottom - height - labelHeight - labelGap);
        var rect = CreateLabelRect(centerX, labelWidth, labelHeight, labelTop);

        if (avoidRect is SKRect otherRect && rect.IntersectsWith(otherRect))
        {
            var shiftedTop = otherRect.Top - labelHeight - labelGap;
            if (shiftedTop < plotRect.Top + 2)
            {
                return null;
            }

            rect = CreateLabelRect(centerX, labelWidth, labelHeight, shiftedTop);
            if (rect.IntersectsWith(otherRect))
            {
                return null;
            }
        }

        var textBaseline = rect.MidY - (bounds.Top + bounds.Bottom) / 2f;
        canvas.DrawRoundRect(rect, 7, 7, backgroundPaint);
        canvas.DrawRoundRect(rect, 7, 7, borderPaint);
        canvas.DrawText(label, rect.Left + paddingX, textBaseline, SKTextAlign.Left, textFont, textPaint);
        return rect;
    }

    private static SKRect CreateLabelRect(float centerX, float labelWidth, float labelHeight, float labelTop)
    {
        var labelBottom = labelTop + labelHeight;
        return new SKRect(
            centerX - labelWidth / 2f,
            labelTop,
            centerX + labelWidth / 2f,
            labelBottom);
    }

    private static double[] BuildHistoryOverlayValues(IReadOnlyList<DailyProcessActivitySummary> records, string historyOverlayOption)
    {
        return historyOverlayOption switch
        {
            DetailDisplayPreferences.HistoryOverlayUsageDurationOption => records
                .Select(record => (double)(record.ForegroundMilliseconds + record.BackgroundMilliseconds))
                .ToArray(),
            DetailDisplayPreferences.HistoryOverlayForegroundDurationOption => records
                .Select(record => (double)record.ForegroundMilliseconds)
                .ToArray(),
            _ => []
        };
    }

    private static void DrawHistoryOverlayLine(
        SKCanvas canvas,
        SKPaint linePaint,
        SKPaint pointPaint,
        IReadOnlyList<double> values,
        SKRect plotRect)
    {
        if (values.Count == 0)
        {
            return;
        }

        var overlayMax = values.Max();
        var effectiveOverlayMax = overlayMax > 0d ? overlayMax : 1d;

        if (values.Count == 1)
        {
            var singlePointDrawableHeight = Math.Max(0f, plotRect.Height - 8f);
            var centerX = plotRect.MidX;
            var normalizedHeight = (float)(Math.Max(0d, values[0]) / effectiveOverlayMax * singlePointDrawableHeight);
            var point = new SKPoint(centerX, plotRect.Bottom - normalizedHeight);
            canvas.DrawCircle(point, 3.6f, pointPaint);
            return;
        }

        var points = new SKPoint[values.Count];
        var slotWidth = plotRect.Width / Math.Max(values.Count, 1);
        var drawableHeight = Math.Max(0f, plotRect.Height - 8f);

        for (var i = 0; i < values.Count; i++)
        {
            var centerX = plotRect.Left + i * slotWidth + slotWidth / 2f;
            var normalizedHeight = (float)(Math.Max(0d, values[i]) / effectiveOverlayMax * drawableHeight);
            points[i] = new SKPoint(centerX, plotRect.Bottom - normalizedHeight);
        }

        if (overlayMax > 0d)
        {
            using var path = new SKPath();
            path.MoveTo(points[0]);
            for (var i = 1; i < points.Length; i++)
            {
                path.LineTo(points[i]);
            }

            canvas.DrawPath(path, linePaint);
        }

        var drawPoints = values.Count <= 14;
        for (var i = 0; i < points.Length; i++)
        {
            if (drawPoints)
            {
                canvas.DrawCircle(points[i], 2.6f, pointPaint);
            }
        }
    }

    private static void DrawHistoryOverlayAxisLabels(
        SKCanvas canvas,
        SKPaint textPaint,
        SKFont textFont,
        SKPaint backgroundPaint,
        IReadOnlyList<double> values,
        SKRect plotRect,
        int renderWidth,
        bool showRightAxis)
    {
        if (values.Count == 0)
        {
            return;
        }

        var overlayMax = values.Max();
        if (overlayMax <= 0d)
        {
            var zeroBounds = MeasureTextBounds(textFont, "0");
            DrawOverlayAxisLabel(canvas, textPaint, textFont, backgroundPaint, "0", zeroBounds, 10f, plotRect.Bottom, alignRight: false);

            if (showRightAxis)
            {
                var rightX = renderWidth - 8f;
                DrawOverlayAxisLabel(canvas, textPaint, textFont, backgroundPaint, "0", zeroBounds, rightX, plotRect.Bottom, alignRight: true);
            }

            return;
        }

        const int tickCount = 4;
        for (var i = 0; i < tickCount; i++)
        {
            var ratio = 1d - i / (double)(tickCount - 1);
            var value = overlayMax * ratio;
            var label = FormatCompactDurationLabel(value);
            var y = plotRect.Top + (float)((plotRect.Height / (tickCount - 1)) * i);
            var bounds = MeasureTextBounds(textFont, label);
            DrawOverlayAxisLabel(canvas, textPaint, textFont, backgroundPaint, label, bounds, 10f, y, alignRight: false);

            if (showRightAxis)
            {
                var rightX = renderWidth - 8f;
                DrawOverlayAxisLabel(canvas, textPaint, textFont, backgroundPaint, label, bounds, rightX, y, alignRight: true);
            }
        }
    }

    private static void DrawOverlayAxisLabel(
        SKCanvas canvas,
        SKPaint textPaint,
        SKFont textFont,
        SKPaint backgroundPaint,
        string label,
        SKRect bounds,
        float x,
        float y,
        bool alignRight)
    {
        var rect = alignRight
            ? new SKRect(x - bounds.Width - 12f, y - 8f, x, y + 8f)
            : new SKRect(x, y - 8f, x + bounds.Width + 12f, y + 8f);

        canvas.DrawRoundRect(rect, 6, 6, backgroundPaint);
        var textBaseline = rect.MidY - (bounds.Top + bounds.Bottom) / 2f;
        canvas.DrawText(label, rect.Left + 4f, textBaseline, SKTextAlign.Left, textFont, textPaint);
    }

    private static SKColor GetHistoryBarColor(string baseHex, int tintIndex)
    {
        var baseColor = SKColor.Parse(baseHex);
        if (tintIndex <= 0)
        {
            return baseColor;
        }

        var factor = Math.Min(0.18f, tintIndex * 0.05f);
        byte Mix(byte value) => (byte)Math.Clamp(value + (255 - value) * factor, 0, 255);
        return new SKColor(Mix(baseColor.Red), Mix(baseColor.Green), Mix(baseColor.Blue), baseColor.Alpha);
    }

    private static void DrawHistoryDateLabel(
        SKCanvas canvas,
        SKPaint textPaint,
        SKFont textFont,
        string dayText,
        float centerX,
        float baselineY,
        int recordCount)
    {
        if (!DateOnly.TryParse(dayText, CultureInfo.InvariantCulture, out var day))
        {
            return;
        }

        var label = recordCount <= 10
            ? day.ToString("MM-dd", CultureInfo.InvariantCulture)
            : day.ToString("dd", CultureInfo.InvariantCulture);

        canvas.DrawText(label, centerX, baselineY, SKTextAlign.Center, textFont, textPaint);
    }

    private static SKRect MeasureTextBounds(SKFont font, string text)
    {
        font.MeasureText(text, out var bounds);
        return bounds;
    }

    private static SKFont CreateSkFont(string familyName, float size, SKFontStyleWeight weight = SKFontStyleWeight.Normal)
    {
        var typeface = SKTypeface.FromFamilyName(
            familyName,
            weight,
            SKFontStyleWidth.Normal,
            SKFontStyleSlant.Upright);
        return new SKFont(typeface, size);
    }

    private string BuildHistoryTrafficDisplay()
    {
        return _preferences.IsNetworkSplit
            ? $"下载 {_historySummary.DownloadDisplay}\n上传 {_historySummary.UploadDisplay}"
            : _historySummary.TotalTrafficDisplay;
    }

    private string BuildHistoryIoDisplay()
    {
        return _preferences.IsIoSplit
            ? $"读取 {_historySummary.IoReadDisplay}\n写入 {_historySummary.IoWriteDisplay}"
            : _historySummary.TotalIoDisplay;
    }

    private static IReadOnlyList<DailyProcessActivitySummary> SelectHistoryRecords(
        IReadOnlyList<DailyProcessActivitySummary> records,
        HistoryAnalysisDimension dimension,
        DateOnly selectedDate,
        IReadOnlyCollection<DateOnly>? selectedCustomDays = null)
    {
        return dimension switch
        {
            HistoryAnalysisDimension.Week => records
                .Where(record =>
                {
                    var day = DateOnly.Parse(record.Day, CultureInfo.InvariantCulture);
                    var weekStart = GetWeekStart(selectedDate);
                    var weekEnd = weekStart.AddDays(6);
                    return day >= weekStart && day <= weekEnd;
                })
                .OrderBy(record => record.Day, StringComparer.Ordinal)
                .ToList(),
            HistoryAnalysisDimension.Month => records
                .Where(record =>
                {
                    var day = DateOnly.Parse(record.Day, CultureInfo.InvariantCulture);
                    return day.Year == selectedDate.Year && day.Month == selectedDate.Month;
                })
                .OrderBy(record => record.Day, StringComparer.Ordinal)
                .ToList(),
            HistoryAnalysisDimension.Custom => records
                .Where(record =>
                {
                    var day = DateOnly.Parse(record.Day, CultureInfo.InvariantCulture);
                    return (selectedCustomDays ?? Array.Empty<DateOnly>()).Contains(day);
                })
                .OrderBy(record => record.Day, StringComparer.Ordinal)
                .ToList(),
            _ => records
                .Where(record => string.Equals(record.Day, selectedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparison.Ordinal))
                .OrderBy(record => record.Day, StringComparer.Ordinal)
                .ToList()
        };
    }

    private static DateOnly GetWeekStart(DateOnly date)
    {
        var offset = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-offset);
    }

    private static (DateOnly Start, DateOnly End) ResolveHistoryRange(HistoryAnalysisDimension dimension, DateOnly selectedDate)
    {
        return dimension switch
        {
            HistoryAnalysisDimension.Week => (GetWeekStart(selectedDate), GetWeekStart(selectedDate).AddDays(6)),
            HistoryAnalysisDimension.Month => (new DateOnly(selectedDate.Year, selectedDate.Month, 1), new DateOnly(selectedDate.Year, selectedDate.Month, 1).AddMonths(1).AddDays(-1)),
            HistoryAnalysisDimension.Custom => (selectedDate, selectedDate),
            _ => (selectedDate, selectedDate)
        };
    }

    private DateOnly ResolveDayHistorySelection(HistoryAnalysisDimension previousDimension, DateOnly selectedDate)
    {
        var (rangeStart, rangeEnd) = ResolveHistoryRange(previousDimension, selectedDate);
        return ResolveEarliestHistoryDate(_allHistoryDailyRecords, rangeStart, rangeEnd) ?? rangeStart;
    }

    private DateOnly ResolveWeekHistorySelection(HistoryAnalysisDimension previousDimension, DateOnly selectedDate)
    {
        var (rangeStart, rangeEnd) = ResolveHistoryRange(previousDimension, selectedDate);
        return GetWeekStart(ResolveEarliestHistoryDate(_allHistoryDailyRecords, rangeStart, rangeEnd) ?? rangeStart);
    }

    private static DateOnly? ResolveEarliestHistoryDate(
        IReadOnlyList<DailyProcessActivitySummary> records,
        DateOnly rangeStart,
        DateOnly rangeEnd)
    {
        foreach (var day in records
                     .Select(static record => DateOnly.TryParseExact(record.Day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDay)
                         ? parsedDay
                         : (DateOnly?)null)
                     .Where(static day => day is not null)
                     .Select(static day => day!.Value)
                     .Where(day => day >= rangeStart && day <= rangeEnd)
                     .OrderBy(static day => day))
        {
            return day;
        }

        return null;
    }

    private static ApplicationHistorySummary BuildHistorySummary(
        IReadOnlyList<DailyProcessActivitySummary> records,
        ProcessResourceSnapshot snapshot,
        HistoryAnalysisDimension dimension,
        DateOnly selectedDate,
        IReadOnlyCollection<DateOnly>? selectedCustomDays = null)
    {
        var rangeDisplay = dimension switch
        {
            HistoryAnalysisDimension.Week => $"{GetWeekStart(selectedDate):yyyy-MM-dd} 至 {GetWeekStart(selectedDate).AddDays(6):yyyy-MM-dd}",
            HistoryAnalysisDimension.Month => $"{selectedDate:yyyy 年 MM 月}",
            HistoryAnalysisDimension.Custom => BuildCustomRangeDisplay(selectedCustomDays ?? Array.Empty<DateOnly>()),
            _ => $"{selectedDate:yyyy-MM-dd}"
        };

        if (records.Count == 0)
        {
            return ApplicationHistorySummary.Empty with
            {
                RangeDisplay = rangeDisplay,
                ExecutablePathDisplay = string.IsNullOrWhiteSpace(snapshot.ExecutablePath) ? "-" : snapshot.ExecutablePath
            };
        }

        var ordered = records
            .OrderBy(record => record.Day, StringComparer.Ordinal)
            .ToList();
        var firstDay = ordered[0].Day;
        var lastDay = ordered[^1].Day;
        var totalForegroundMilliseconds = ordered.Sum(record => record.ForegroundMilliseconds);
        var totalBackgroundMilliseconds = ordered.Sum(record => record.BackgroundMilliseconds);
        var totalDownloadBytes = ordered.Sum(record => record.DownloadBytes);
        var totalUploadBytes = ordered.Sum(record => record.UploadBytes);
        var totalIoReadBytes = ordered.Sum(record => record.IoReadBytes);
        var totalIoWriteBytes = ordered.Sum(record => record.IoWriteBytes);
        var averageCpuSamples = ordered.Sum(record => record.ForegroundSamples + record.BackgroundSamples);
        var averageCpuTotal = ordered.Sum(record => record.ForegroundCpuTotal + record.BackgroundCpuTotal);
        var averageMemorySamples = ordered.Sum(record => record.ForegroundSamples + record.BackgroundSamples);
        var averageMemoryTotal = ordered.Sum(record => record.ForegroundWorkingSetTotal + record.BackgroundWorkingSetTotal);
        var foregroundCpuSamples = ordered.Sum(record => record.ForegroundSamples);
        var foregroundCpuTotal = ordered.Sum(record => record.ForegroundCpuTotal);
        var foregroundMemorySamples = ordered.Sum(record => record.ForegroundSamples);
        var foregroundMemoryTotal = ordered.Sum(record => record.ForegroundWorkingSetTotal);
        var foregroundMilliseconds = ordered.Sum(record => record.ForegroundMilliseconds);
        var foregroundIoOperations = ordered.Sum(record => record.ForegroundIoOperations);
        var backgroundCpuSamples = ordered.Sum(record => record.BackgroundSamples);
        var backgroundCpuTotal = ordered.Sum(record => record.BackgroundCpuTotal);
        var backgroundMemorySamples = ordered.Sum(record => record.BackgroundSamples);
        var backgroundMemoryTotal = ordered.Sum(record => record.BackgroundWorkingSetTotal);
        var backgroundMilliseconds = ordered.Sum(record => record.BackgroundMilliseconds);
        var backgroundIoOperations = ordered.Sum(record => record.BackgroundIoOperations);
        var peakWorkingSetBytes = ordered.Max(record => record.PeakWorkingSetBytes);
        var averageIops = ordered.Average(record => record.AverageIops);
        var averageThreadCount = ordered.Average(record => record.AverageThreadCount);
        var peakThreadCount = ordered.Max(record => record.PeakThreadCount);
        var executablePath = ordered
            .Select(record => record.ExecutablePath)
            .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path))
            ?? (string.IsNullOrWhiteSpace(snapshot.ExecutablePath) ? "-" : snapshot.ExecutablePath);
        var focusRatio = totalForegroundMilliseconds + totalBackgroundMilliseconds > 0
            ? totalForegroundMilliseconds / (double)(totalForegroundMilliseconds + totalBackgroundMilliseconds)
            : 0d;
        var averageCpu = averageCpuSamples > 0 ? averageCpuTotal / averageCpuSamples : 0d;
        var averageMemoryBytes = averageMemorySamples > 0 ? averageMemoryTotal / averageMemorySamples : 0d;
        var averageForegroundCpu = foregroundCpuSamples > 0 ? foregroundCpuTotal / foregroundCpuSamples : 0d;
        var averageForegroundMemoryBytes = foregroundMemorySamples > 0 ? foregroundMemoryTotal / foregroundMemorySamples : 0d;
        var averageForegroundIops = foregroundMilliseconds > 0 ? foregroundIoOperations / (foregroundMilliseconds / 1000d) : 0d;
        var averageBackgroundCpu = backgroundCpuSamples > 0 ? backgroundCpuTotal / backgroundCpuSamples : 0d;
        var averageBackgroundMemoryBytes = backgroundMemorySamples > 0 ? backgroundMemoryTotal / backgroundMemorySamples : 0d;
        var averageBackgroundIops = backgroundMilliseconds > 0 ? backgroundIoOperations / (backgroundMilliseconds / 1000d) : 0d;

        return new ApplicationHistorySummary
        {
            RangeDisplay = rangeDisplay,
            ActiveDaysDisplay = $"活跃天数 {ordered.Count}",
            TotalUsageDisplay = FormatDuration(totalForegroundMilliseconds + totalBackgroundMilliseconds),
            ForegroundDurationDisplay = FormatDuration(totalForegroundMilliseconds),
            BackgroundDurationDisplay = FormatDuration(totalBackgroundMilliseconds),
            ForegroundRatioDisplay = $"前台占比 {(focusRatio * 100d).ToString("F1", CultureInfo.InvariantCulture)}%",
            DownloadDisplay = FormatBytes(totalDownloadBytes),
            UploadDisplay = FormatBytes(totalUploadBytes),
            TotalTrafficDisplay = FormatBytes(totalDownloadBytes + totalUploadBytes),
            IoReadDisplay = FormatBytes(totalIoReadBytes),
            IoWriteDisplay = FormatBytes(totalIoWriteBytes),
            TotalIoDisplay = FormatBytes(totalIoReadBytes + totalIoWriteBytes),
            AverageCpuDisplay = $"{averageCpu:F1}%",
            AverageIopsDisplay = averageIops.ToString("F1", CultureInfo.InvariantCulture),
            MemorySummaryDisplay = $"均值 {FormatBytes(averageMemoryBytes)} / 峰值 {FormatBytes(peakWorkingSetBytes)}",
            ThreadSummaryDisplay = $"均值 {averageThreadCount:F1} / 峰值 {peakThreadCount}",
            AverageForegroundCpuDisplay = $"{averageForegroundCpu:F1}%",
            AverageForegroundMemoryDisplay = FormatBytes(averageForegroundMemoryBytes),
            AverageForegroundIopsDisplay = averageForegroundIops.ToString("F1", CultureInfo.InvariantCulture),
            AverageBackgroundCpuDisplay = $"{averageBackgroundCpu:F1}%",
            AverageBackgroundMemoryDisplay = FormatBytes(averageBackgroundMemoryBytes),
            AverageBackgroundIopsDisplay = averageBackgroundIops.ToString("F1", CultureInfo.InvariantCulture),
            ExecutablePathDisplay = executablePath,
            ChartStartLabel = firstDay,
            ChartEndLabel = lastDay
        };
    }

    private static string FormatRate(double bytesPerSecond)
    {
        var value = bytesPerSecond;
        var units = new[] { "B/s", "KB/s", "MB/s", "GB/s", "TB/s" };
        var unitIndex = 0;

        while (value >= 1024d && unitIndex < units.Length - 1)
        {
            value /= 1024d;
            unitIndex++;
        }

        var decimals = value >= 100d ? 0 : value >= 10d ? 1 : 2;
        return value.ToString($"F{decimals}", CultureInfo.InvariantCulture) + " " + units[unitIndex];
    }

    private static string FormatBytes(double bytes)
    {
        var value = bytes;
        var units = new[] { "B", "KB", "MB", "GB", "TB" };
        var unitIndex = 0;

        while (value >= 1024d && unitIndex < units.Length - 1)
        {
            value /= 1024d;
            unitIndex++;
        }

        var decimals = value >= 100d ? 0 : value >= 10d ? 1 : 2;
        return value.ToString($"F{decimals}", CultureInfo.InvariantCulture) + " " + units[unitIndex];
    }

    private static string FormatBytes(long bytes) => FormatBytes((double)bytes);

    private static string FormatCompactBytes(double bytes)
    {
        var value = bytes;
        var units = new[] { "B", "K", "M", "G", "T" };
        var unitIndex = 0;

        while (value >= 1024d && unitIndex < units.Length - 1)
        {
            value /= 1024d;
            unitIndex++;
        }

        var decimals = value >= 100d ? 0 : value >= 10d ? 0 : 1;
        return value.ToString($"F{decimals}", CultureInfo.InvariantCulture) + units[unitIndex];
    }

    private static string FormatCompactDurationLabel(double milliseconds)
    {
        if (milliseconds <= 0d)
        {
            return "0m";
        }

        var duration = TimeSpan.FromMilliseconds(milliseconds);
        if (duration.TotalHours >= 1d)
        {
            return $"{(int)duration.TotalHours}h{duration.Minutes:D2}";
        }

        if (duration.TotalMinutes >= 1d)
        {
            return $"{(int)duration.TotalMinutes}m";
        }

        return $"{Math.Max(1, duration.Seconds)}s";
    }

    private static string FormatDuration(long milliseconds)
    {
        if (milliseconds <= 0)
        {
            return "00:00:00";
        }

        return TimeSpan.FromMilliseconds(milliseconds).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
    }

    private static string BuildFocusRatio(long foregroundMilliseconds, long backgroundMilliseconds)
    {
        var total = foregroundMilliseconds + backgroundMilliseconds;
        if (total <= 0)
        {
            return "今日尚无足够样本";
        }

        var ratio = foregroundMilliseconds / (double)total;
        return $"前台占比 {(ratio * 100d).ToString("F0", CultureInfo.InvariantCulture)}%";
    }

    private void OnHistoryCalendarDateInvoked(DateOnly date)
    {
        if (_selectedHistoryDimension == HistoryAnalysisDimension.Custom)
        {
            SetHistoryCustomDateSelection(date, !_historyCustomSelectedDates.Contains(date));
            return;
        }

        SetHistorySelectedDate(date);
    }

    private void SeedCustomSelectionFromCurrentDimension(HistoryAnalysisDimension previousDimension)
    {
        _historyCustomSelectedDates.Clear();
        var daysWithData = _allHistoryDailyRecords
            .Select(record => DateOnly.Parse(record.Day, CultureInfo.InvariantCulture))
            .ToHashSet();

        switch (previousDimension)
        {
            case HistoryAnalysisDimension.Week:
            {
                var weekStart = GetWeekStart(_historySelectedDate);
                for (var i = 0; i < 7; i++)
                {
                    var date = weekStart.AddDays(i);
                    if (daysWithData.Contains(date))
                    {
                        _historyCustomSelectedDates.Add(date);
                    }
                }

                break;
            }
            case HistoryAnalysisDimension.Month:
            {
                var monthStart = new DateOnly(_historySelectedDate.Year, _historySelectedDate.Month, 1);
                var monthEnd = monthStart.AddMonths(1).AddDays(-1);
                for (var date = monthStart; date <= monthEnd; date = date.AddDays(1))
                {
                    if (daysWithData.Contains(date))
                    {
                        _historyCustomSelectedDates.Add(date);
                    }
                }

                break;
            }
            case HistoryAnalysisDimension.Custom:
                break;
            default:
                if (daysWithData.Contains(_historySelectedDate))
                {
                    _historyCustomSelectedDates.Add(_historySelectedDate);
                }

                break;
        }
    }

    private DateOnly GetHistoryCustomSelectionAnchor() =>
        _historyCustomSelectedDates.Count > 0
            ? _historyCustomSelectedDates.Min()
            : _historySelectedDate;

    private IReadOnlyList<DateOnly> GetCustomSelectedDays() =>
        _historyCustomSelectedDates
            .OrderBy(static date => date)
            .ToArray();

    private static string BuildCustomRangeDisplay(IReadOnlyCollection<DateOnly> selectedDays)
    {
        if (selectedDays.Count == 0)
        {
            return "未选择";
        }

        return string.Join("、", GetContinuousRanges(selectedDays).Select(static range =>
            range.Start == range.End
                ? range.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : $"{range.Start:yyyy-MM-dd}-{range.End:yyyy-MM-dd}"));
    }

    private static IReadOnlyList<(DateOnly Start, DateOnly End)> GetContinuousRanges(IReadOnlyCollection<DateOnly> selectedDays)
    {
        if (selectedDays.Count == 0)
        {
            return [];
        }

        var ordered = selectedDays
            .Distinct()
            .OrderBy(static date => date)
            .ToArray();
        var ranges = new List<(DateOnly Start, DateOnly End)>();
        var start = ordered[0];
        var end = ordered[0];

        for (var index = 1; index < ordered.Length; index++)
        {
            var date = ordered[index];
            if (date == end.AddDays(1))
            {
                end = date;
                continue;
            }

            ranges.Add((start, end));
            start = date;
            end = date;
        }

        ranges.Add((start, end));
        return ranges;
    }


    private sealed record ApplicationHistorySummary
    {
        public static readonly ApplicationHistorySummary Empty = new()
        {
            RangeDisplay = "暂无历史数据",
            ActiveDaysDisplay = "活跃天数 0",
            TotalUsageDisplay = "00:00:00",
            ForegroundDurationDisplay = "00:00:00",
            BackgroundDurationDisplay = "00:00:00",
            ForegroundRatioDisplay = "前台占比 0.0%",
            DownloadDisplay = "0 B",
            UploadDisplay = "0 B",
            TotalTrafficDisplay = "0 B",
            IoReadDisplay = "0 B",
            IoWriteDisplay = "0 B",
            TotalIoDisplay = "0 B",
            AverageCpuDisplay = "0.0%",
            AverageIopsDisplay = "0.0",
            MemorySummaryDisplay = "均值 0 B / 峰值 0 B",
            ThreadSummaryDisplay = "均值 0.0 / 峰值 0",
            AverageForegroundCpuDisplay = "0.0%",
            AverageForegroundMemoryDisplay = "0 B",
            AverageForegroundIopsDisplay = "0.0",
            AverageBackgroundCpuDisplay = "0.0%",
            AverageBackgroundMemoryDisplay = "0 B",
            AverageBackgroundIopsDisplay = "0.0",
            ExecutablePathDisplay = "-",
            ChartStartLabel = "-",
            ChartEndLabel = "-"
        };

        public string RangeDisplay { get; init; } = string.Empty;
        public string ActiveDaysDisplay { get; init; } = string.Empty;
        public string TotalUsageDisplay { get; init; } = string.Empty;
        public string ForegroundDurationDisplay { get; init; } = string.Empty;
        public string BackgroundDurationDisplay { get; init; } = string.Empty;
        public string ForegroundRatioDisplay { get; init; } = string.Empty;
        public string DownloadDisplay { get; init; } = string.Empty;
        public string UploadDisplay { get; init; } = string.Empty;
        public string TotalTrafficDisplay { get; init; } = string.Empty;
        public string IoReadDisplay { get; init; } = string.Empty;
        public string IoWriteDisplay { get; init; } = string.Empty;
        public string TotalIoDisplay { get; init; } = string.Empty;
        public string AverageCpuDisplay { get; init; } = string.Empty;
        public string AverageIopsDisplay { get; init; } = string.Empty;
        public string MemorySummaryDisplay { get; init; } = string.Empty;
        public string ThreadSummaryDisplay { get; init; } = string.Empty;
        public string AverageForegroundCpuDisplay { get; init; } = string.Empty;
        public string AverageForegroundMemoryDisplay { get; init; } = string.Empty;
        public string AverageForegroundIopsDisplay { get; init; } = string.Empty;
        public string AverageBackgroundCpuDisplay { get; init; } = string.Empty;
        public string AverageBackgroundMemoryDisplay { get; init; } = string.Empty;
        public string AverageBackgroundIopsDisplay { get; init; } = string.Empty;
        public string ExecutablePathDisplay { get; init; } = string.Empty;
        public string ChartStartLabel { get; init; } = string.Empty;
        public string ChartEndLabel { get; init; } = string.Empty;
    }

    public sealed class ApplicationHistoryCalendarDayViewModel : ObservableObject
    {
        private readonly RelayCommand _selectCommand;
        private readonly Action<DateOnly> _onSelect;
        private DateOnly _date;
        private string _dayNumber = string.Empty;
        private bool _isInDisplayedMonth;
        private bool _hasData;
        private bool _isSelected;
        private bool _isRangeStart;
        private bool _isRangeEnd;
        private bool _isSingleSelection;
        private bool _isRangeStartOnly;
        private bool _isRangeEndOnly;
        private bool _isRangeMiddle;
        private bool _isSelectable;

        public ApplicationHistoryCalendarDayViewModel(Action<DateOnly> onSelect)
        {
            _onSelect = onSelect;
            _selectCommand = new RelayCommand(() => _onSelect(Date), () => IsSelectable);
        }

        public DateOnly Date
        {
            get => _date;
            private set => SetProperty(ref _date, value);
        }

        public string DayNumber
        {
            get => _dayNumber;
            private set => SetProperty(ref _dayNumber, value);
        }

        public bool IsInDisplayedMonth
        {
            get => _isInDisplayedMonth;
            private set => SetProperty(ref _isInDisplayedMonth, value);
        }

        public bool HasData
        {
            get => _hasData;
            private set => SetProperty(ref _hasData, value);
        }

        public bool IsSelected
        {
            get => _isSelected;
            private set => SetProperty(ref _isSelected, value);
        }

        public bool IsRangeStart
        {
            get => _isRangeStart;
            private set => SetProperty(ref _isRangeStart, value);
        }

        public bool IsRangeEnd
        {
            get => _isRangeEnd;
            private set => SetProperty(ref _isRangeEnd, value);
        }

        public bool IsSingleSelection
        {
            get => _isSingleSelection;
            private set => SetProperty(ref _isSingleSelection, value);
        }

        public bool IsRangeStartOnly
        {
            get => _isRangeStartOnly;
            private set => SetProperty(ref _isRangeStartOnly, value);
        }

        public bool IsRangeEndOnly
        {
            get => _isRangeEndOnly;
            private set => SetProperty(ref _isRangeEndOnly, value);
        }

        public bool IsRangeMiddle
        {
            get => _isRangeMiddle;
            private set => SetProperty(ref _isRangeMiddle, value);
        }

        public bool IsSelectable
        {
            get => _isSelectable;
            private set => SetProperty(ref _isSelectable, value);
        }

        public ICommand SelectCommand => _selectCommand;

        public void Update(
            DateOnly date,
            bool isInDisplayedMonth,
            bool hasData,
            bool isSelected,
            bool isRangeStart,
            bool isRangeEnd,
            bool isSelectable)
        {
            Date = date;
            DayNumber = date.Day.ToString(CultureInfo.InvariantCulture);
            IsInDisplayedMonth = isInDisplayedMonth;
            HasData = hasData;
            IsSelected = isSelected;
            IsRangeStart = isRangeStart;
            IsRangeEnd = isRangeEnd;
            IsSingleSelection = isRangeStart && isRangeEnd;
            IsRangeStartOnly = isRangeStart && !IsSingleSelection;
            IsRangeEndOnly = isRangeEnd && !IsSingleSelection;
            IsRangeMiddle = isSelected && !isRangeStart && !isRangeEnd;
            IsSelectable = isSelectable;
            _selectCommand.NotifyCanExecuteChanged();
        }
    }

    private sealed record TimedMetricSample(DateTime TimestampUtc, double PrimaryValue, double SecondaryValue);
    private sealed record ChartBitmapBuffer(int Width, int Height, int Stride, byte[] Pixels);
    private sealed record SingleChartRenderResult(ChartBitmapBuffer? Buffer, string TopLabel)
    {
        public static readonly SingleChartRenderResult Empty = new(null, "1 KB/s");
    }
    private sealed record ChartRenderResult(SingleChartRenderResult NetworkChart, SingleChartRenderResult IoChart);
}
