using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ScottPlot;
using SkiaSharp;
using WinFormsApp1.Desktop.Services;
using WinFormsApp1.Domain.Entities;

namespace WinFormsApp1.Desktop.ViewModels;

public sealed class ApplicationDetailViewModel : ObservableObject
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
        Month
    }

    private const int MaxHistorySeconds = 120;
    private const int ChartWidth = 760;
    private const int ChartHeight = 340;
    private const int HistoryChartHeight = 180;
    private const int DefaultHistoryChartViewportWidth = 760;
    private const int HistoryChartVisibleBars = 7;
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(1);
    private const int HistoryChartDays = 30;

    private readonly ApplicationCardViewModel _application;
    private readonly DetailDisplayPreferences _preferences;
    private readonly ApplicationHistoryAnalysisProvider _historyAnalysisProvider;
    private readonly DispatcherTimer _refreshTimer;
    private readonly List<TimedMetricSample> _networkHistory = new();
    private readonly List<TimedMetricSample> _ioHistory = new();
    private DetailDataMode _selectedDataMode = DetailDataMode.Current;
    private HistoryAnalysisDimension _selectedHistoryDimension = HistoryAnalysisDimension.Day;
    private ApplicationHistorySummary _historySummary = ApplicationHistorySummary.Empty;
    private List<DailyProcessActivitySummary> _allHistoryDailyRecords = [];
    private List<DailyProcessActivitySummary> _historyDailyRecords = [];
    private DateOnly _historyDisplayedMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateOnly _historySelectedDate = DateOnly.FromDateTime(DateTime.Today);
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
    private double _historyChartViewportWidth = DefaultHistoryChartViewportWidth;
    private int _historySummaryLoadVersion;

    public ApplicationDetailViewModel(ApplicationCardViewModel application, DetailDisplayPreferences preferences, string databasePath)
    {
        _application = application;
        _preferences = preferences;
        _historyAnalysisProvider = new ApplicationHistoryAnalysisProvider(databasePath);
        _refreshTimer = new DispatcherTimer
        {
            Interval = RefreshInterval
        };
        _refreshTimer.Tick += OnRefreshTimerTick;

        _application.PropertyChanged += OnApplicationPropertyChanged;
        _preferences.PropertyChanged += (_, _) =>
        {
            RaisePreferenceDependentProperties();
            QueueChartRefresh();
        };

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
        ShowCurrentDataCommand = new RelayCommand(ShowCurrentData);
        ShowHistoryDataCommand = new RelayCommand(ShowHistoryData);
        SetHistoryDayDimensionCommand = new RelayCommand(() => SetHistoryDimension(HistoryAnalysisDimension.Day));
        SetHistoryWeekDimensionCommand = new RelayCommand(() => SetHistoryDimension(HistoryAnalysisDimension.Week));
        SetHistoryMonthDimensionCommand = new RelayCommand(() => SetHistoryDimension(HistoryAnalysisDimension.Month));
        ToggleHistoryDatePickerCommand = new RelayCommand(ToggleHistoryDatePicker);
        ShowPreviousHistoryMonthCommand = new RelayCommand(ShowPreviousHistoryMonth);
        ShowNextHistoryMonthCommand = new RelayCommand(ShowNextHistoryMonth);

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
    public ObservableCollection<ApplicationHistoryCalendarDayViewModel> HistoryCalendarDays { get; }

    public string DisplayName => _application.DisplayName;
    public string OriginalName => _application.OriginalName;
    public string DisplayNameWithOriginal =>
        string.Equals(_application.DisplayName, _application.OriginalName, StringComparison.Ordinal)
            ? _application.DisplayName
            : $"{_application.DisplayName}（原名：{_application.OriginalName}）";
    public string? IconSourcePath => _application.IconSourcePath;
    public string StateDisplay => _application.StateDisplay;
    public bool IsCurrentDataMode => _selectedDataMode == DetailDataMode.Current;
    public bool IsHistoryDataMode => _selectedDataMode == DetailDataMode.History;
    public bool IsHistoryDayDimension => _selectedHistoryDimension == HistoryAnalysisDimension.Day;
    public bool IsHistoryWeekDimension => _selectedHistoryDimension == HistoryAnalysisDimension.Week;
    public bool IsHistoryMonthDimension => _selectedHistoryDimension == HistoryAnalysisDimension.Month;
    public string HistoryDimensionTitle => _selectedHistoryDimension switch
    {
        HistoryAnalysisDimension.Week => "按周分析",
        HistoryAnalysisDimension.Month => "按月分析",
        _ => "按日分析"
    };
    public string HistorySelectionDisplay => _selectedHistoryDimension switch
    {
        HistoryAnalysisDimension.Week => $"已选周：{GetWeekStart(_historySelectedDate):yyyy-MM-dd} 起",
        HistoryAnalysisDimension.Month => $"已选月：{new DateOnly(_historySelectedDate.Year, _historySelectedDate.Month, 1):yyyy 年 MM 月}",
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
    public string HistorySummaryCaption => _historySummary.Caption;
    public string HistoryRangeDisplay => _historySummary.RangeDisplay;
    public string HistoryActiveDaysDisplay => _historySummary.ActiveDaysDisplay;
    public string HistoryUsageDisplay => _historySummary.TotalUsageDisplay;
    public string HistoryTrafficDisplay => BuildHistoryTrafficDisplay();
    public string HistoryIoDisplay => BuildHistoryIoDisplay();
    public string HistoryAverageCpuDisplay => _historySummary.AverageCpuDisplay;
    public string HistoryAverageIopsDisplay => _historySummary.AverageIopsDisplay;
    public string HistoryPeakMemoryDisplay => _historySummary.PeakWorkingSetDisplay;
    public string HistoryThreadDisplay => _historySummary.ThreadSummaryDisplay;
    public string HistoryHabitInsight => _historySummary.HabitInsight;
    public string HistoryPerformanceInsight => _historySummary.PerformanceInsight;
    public string HistoryExecutablePathDisplay => _historySummary.ExecutablePathDisplay;
    public string TodayFocusRatio => BuildFocusRatio(Snapshot.DailyForegroundMilliseconds, Snapshot.DailyBackgroundMilliseconds);
    public string HabitInsight => BuildHabitInsight(Snapshot);
    public string PerformanceInsight => BuildPerformanceInsight(Snapshot);

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

    public bool IsNetworkHiddenMode => _preferences.NetworkDisplayOption == DetailDisplayPreferences.HiddenOption;
    public bool IsNetworkTotalMode => _preferences.NetworkDisplayOption == DetailDisplayPreferences.TotalOption;
    public bool IsNetworkSplitMode => _preferences.NetworkDisplayOption == DetailDisplayPreferences.SplitOption;
    public bool IsIoHiddenMode => _preferences.IoDisplayOption == DetailDisplayPreferences.HiddenOption;
    public bool IsIoTotalMode => _preferences.IoDisplayOption == DetailDisplayPreferences.TotalOption;
    public bool IsIoSplitMode => _preferences.IoDisplayOption == DetailDisplayPreferences.SplitOption;
    public bool IsChartScale30SecondsMode => _preferences.ChartScaleOption == DetailDisplayPreferences.ChartScale30SecondsOption;
    public bool IsChartScale1MinuteMode => _preferences.ChartScaleOption == DetailDisplayPreferences.ChartScale1MinuteOption;
    public bool IsChartScale2MinutesMode => _preferences.ChartScaleOption == DetailDisplayPreferences.ChartScale2MinutesOption;

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
    public ICommand ShowCurrentDataCommand { get; }
    public ICommand ShowHistoryDataCommand { get; }
    public ICommand SetHistoryDayDimensionCommand { get; }
    public ICommand SetHistoryWeekDimensionCommand { get; }
    public ICommand SetHistoryMonthDimensionCommand { get; }
    public ICommand ToggleHistoryDatePickerCommand { get; }
    public ICommand ShowPreviousHistoryMonthCommand { get; }
    public ICommand ShowNextHistoryMonthCommand { get; }

    public void SetWindowRenderingActive(bool isActive)
    {
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

        _selectedHistoryDimension = dimension;
        if (dimension == HistoryAnalysisDimension.Month)
        {
            _historySelectedDate = new DateOnly(_historySelectedDate.Year, _historySelectedDate.Month, 1);
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
            IsHistoryDatePickerOpen = false;
            return;
        }

        _historySelectedDate = normalizedDate;
        _historyDisplayedMonth = new DateOnly(date.Year, date.Month, 1);
        IsHistoryDatePickerOpen = false;
        ApplyHistorySelection();
    }

    private void ShowPreviousHistoryMonth()
    {
        _historyDisplayedMonth = _historyDisplayedMonth.AddMonths(-1);
        RefreshHistoryCalendar();
        RaisePropertyChanged(nameof(HistoryCalendarMonthDisplay));
    }

    private void ShowNextHistoryMonth()
    {
        _historyDisplayedMonth = _historyDisplayedMonth.AddMonths(1);
        RefreshHistoryCalendar();
        RaisePropertyChanged(nameof(HistoryCalendarMonthDisplay));
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

        MergeTodayRecord(records);
        _allHistoryDailyRecords = records
            .OrderBy(record => record.Day, StringComparer.Ordinal)
            .ToList();
        ApplyHistorySelection();
    }

    private void ApplyHistorySelection()
    {
        _historyDailyRecords = SelectHistoryRecords(_allHistoryDailyRecords, _selectedHistoryDimension, _historySelectedDate).ToList();
        _historySummary = BuildHistorySummary(_historyDailyRecords, Snapshot, _selectedHistoryDimension, _historySelectedDate);
        RefreshHistoryCalendar();
        RaiseHistoryProperties();
        QueueHistoryChartRefresh();
    }

    private void RefreshHistoryCalendar()
    {
        HistoryCalendarDays.Clear();

        var firstVisible = _historyDisplayedMonth.AddDays(-(int)_historyDisplayedMonth.DayOfWeek);
        var selectedWeekStart = GetWeekStart(_historySelectedDate);
        var selectedWeekEnd = selectedWeekStart.AddDays(6);
        var monthStart = new DateOnly(_historySelectedDate.Year, _historySelectedDate.Month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        var daysWithData = _allHistoryDailyRecords
            .Select(record => DateOnly.Parse(record.Day, CultureInfo.InvariantCulture))
            .ToHashSet();

        for (var i = 0; i < 42; i++)
        {
            var date = firstVisible.AddDays(i);
            var isSelected = _selectedHistoryDimension switch
            {
                HistoryAnalysisDimension.Week => date >= selectedWeekStart && date <= selectedWeekEnd,
                HistoryAnalysisDimension.Month => date >= monthStart && date <= monthEnd,
                _ => date == _historySelectedDate
            };

            HistoryCalendarDays.Add(new ApplicationHistoryCalendarDayViewModel(
                date,
                isInDisplayedMonth: date.Month == _historyDisplayedMonth.Month && date.Year == _historyDisplayedMonth.Year,
                hasData: daysWithData.Contains(date),
                isSelected: isSelected,
                isRangeStart: isSelected && (_selectedHistoryDimension == HistoryAnalysisDimension.Day || date == selectedWeekStart || date == monthStart),
                isRangeEnd: isSelected && (_selectedHistoryDimension == HistoryAnalysisDimension.Day || date == selectedWeekEnd || date == monthEnd),
                onSelect: SetHistorySelectedDate));
        }
    }

    private void MergeTodayRecord(List<DailyProcessActivitySummary> records)
    {
        var today = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
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
            IoReadOperations = Snapshot.RealtimeIoReadOpsPerSecond,
            IoWriteOperations = Snapshot.RealtimeIoWriteOpsPerSecond,
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
            LoadHistorySummary();
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
        _pendingChartRefresh = true;
        EnsureRefreshTimerRunning();
    }

    private void QueueHistoryChartRefresh()
    {
        _pendingHistoryChartRefresh = true;
        EnsureRefreshTimerRunning();
    }

    private void EnsureRefreshTimerRunning()
    {
        if (!_refreshTimer.IsEnabled)
        {
            _refreshTimer.Start();
        }
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
        RaisePropertyChanged(nameof(ProcessCountDisplay));
        RaisePropertyChanged(nameof(ProcessIdDisplay));
        RaisePropertyChanged(nameof(ExecutablePathDisplay));
    }

    private void RaiseLiveApplicationProperties()
    {
        RaisePropertyChanged(nameof(TodayFocusRatio));
        RaisePropertyChanged(nameof(HabitInsight));
        RaisePropertyChanged(nameof(PerformanceInsight));
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
        RaisePropertyChanged(nameof(IsNetworkHiddenMode));
        RaisePropertyChanged(nameof(IsNetworkTotalMode));
        RaisePropertyChanged(nameof(IsNetworkSplitMode));
        RaisePropertyChanged(nameof(IsIoHiddenMode));
        RaisePropertyChanged(nameof(IsIoTotalMode));
        RaisePropertyChanged(nameof(IsIoSplitMode));
        RaisePropertyChanged(nameof(IsChartScale30SecondsMode));
        RaisePropertyChanged(nameof(IsChartScale1MinuteMode));
        RaisePropertyChanged(nameof(IsChartScale2MinutesMode));
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
        RaisePropertyChanged(nameof(HistoryDimensionTitle));
        RaisePropertyChanged(nameof(HistorySelectionDisplay));
        RaisePropertyChanged(nameof(SelectedHistoryDateTime));
        RaisePropertyChanged(nameof(HistoryCalendarMonthDisplay));
        RaisePropertyChanged(nameof(HistorySummaryCaption));
        RaisePropertyChanged(nameof(HistoryRangeDisplay));
        RaisePropertyChanged(nameof(HistoryActiveDaysDisplay));
        RaisePropertyChanged(nameof(HistoryUsageDisplay));
        RaisePropertyChanged(nameof(HistoryTrafficDisplay));
        RaisePropertyChanged(nameof(HistoryIoDisplay));
        RaisePropertyChanged(nameof(HistoryAverageCpuDisplay));
        RaisePropertyChanged(nameof(HistoryAverageIopsDisplay));
        RaisePropertyChanged(nameof(HistoryPeakMemoryDisplay));
        RaisePropertyChanged(nameof(HistoryThreadDisplay));
        RaisePropertyChanged(nameof(HistoryHabitInsight));
        RaisePropertyChanged(nameof(HistoryPerformanceInsight));
        RaisePropertyChanged(nameof(HistoryExecutablePathDisplay));
        RaisePropertyChanged(nameof(HistoryNetworkChartTitle));
        RaisePropertyChanged(nameof(HistoryIoChartTitle));
        RaisePropertyChanged(nameof(HistoryChartXAxisStartLabel));
        RaisePropertyChanged(nameof(HistoryChartXAxisEndLabel));
        RaisePropertyChanged(nameof(HistoryChartDisplayWidth));
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

        if (renderVersion != Volatile.Read(ref _chartRenderVersion))
        {
            return;
        }

        NetworkChartSource = chartResult.NetworkChart.Source;
        NetworkChartTopLabel = chartResult.NetworkChart.TopLabel;
        IoChartSource = chartResult.IoChart.Source;
        IoChartTopLabel = chartResult.IoChart.TopLabel;
    }

    private static SingleChartRenderResult BuildChartImage(TimedMetricSample[] history, int historySeconds, bool splitMode, bool isNetwork)
    {
        var nowUtc = DateTime.UtcNow;
        var primary = new double[historySeconds];
        var secondary = new double[historySeconds];

        foreach (var sample in history)
        {
            var ageSeconds = (int)Math.Floor((nowUtc - sample.TimestampUtc).TotalSeconds);
            if (ageSeconds < 0 || ageSeconds >= historySeconds)
            {
                continue;
            }

            var index = historySeconds - 1 - ageSeconds;
            primary[index] = sample.PrimaryValue;
            secondary[index] = sample.SecondaryValue;
        }

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
        plot.Axes.SetLimitsX(0, historySeconds - 1);
        plot.Axes.SetLimitsY(0, yAxisMax);
        plot.Axes.Margins(bottom: 0.02, top: 0.04, left: 0, right: 0);

        if (splitMode)
        {
            var first = plot.Add.Signal(primary);
            first.Color = ScottPlot.Color.FromHex(isNetwork ? "#2D8CFF" : "#17766C");
            first.LineWidth = 2;
            var second = plot.Add.Signal(secondary);
            second.Color = ScottPlot.Color.FromHex(isNetwork ? "#FF8A3D" : "#D06A43");
            second.LineWidth = 2;
        }
        else
        {
            var line = plot.Add.Signal(total);
            line.Color = ScottPlot.Color.FromHex(isNetwork ? "#2A6FBB" : "#176B5A");
            line.LineWidth = 2;
        }

        using var surface = SKSurface.Create(new SKImageInfo(ChartWidth, ChartHeight));
        plot.Render(surface);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        var bytes = data.ToArray();
        using var stream = new MemoryStream(bytes);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return new SingleChartRenderResult(bitmap, FormatRate(yAxisMax));
    }

    private async Task RefreshHistoryChartsAsync()
    {
        var records = _historyDailyRecords
            .TakeLast(HistoryChartDays)
            .ToArray();
        var isNetworkSplit = _preferences.IsNetworkSplit;
        var isIoSplit = _preferences.IsIoSplit;
        var renderWidth = GetHistoryChartRenderWidth(records.Length, _historyChartViewportWidth);

        var chartResult = await Task.Run(() => new ChartRenderResult(
            BuildHistoryChartImage(records, isNetworkSplit, isNetwork: true, _selectedHistoryDimension, renderWidth),
            BuildHistoryChartImage(records, isIoSplit, isNetwork: false, _selectedHistoryDimension, renderWidth)));

        HistoryNetworkChartSource = chartResult.NetworkChart.Source;
        HistoryNetworkChartTopLabel = chartResult.NetworkChart.TopLabel;
        HistoryIoChartSource = chartResult.IoChart.Source;
        HistoryIoChartTopLabel = chartResult.IoChart.TopLabel;
        RaisePropertyChanged(nameof(HistoryChartXAxisStartLabel));
        RaisePropertyChanged(nameof(HistoryChartXAxisEndLabel));
    }

    private static SingleChartRenderResult BuildHistoryChartImage(
        DailyProcessActivitySummary[] records,
        bool splitMode,
        bool isNetwork,
        HistoryAnalysisDimension dimension,
        int renderWidth)
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

        using var surface = SKSurface.Create(new SKImageInfo(renderWidth, HistoryChartHeight));
        var canvas = surface.Canvas;
        canvas.Clear(SKColor.Parse(isNetwork ? "#FFF8EF" : "#F7FBF6"));

        var plotRect = new SKRect(26, 14, renderWidth - 14, HistoryChartHeight - 30);
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
        using var labelTextPaint = new SKPaint
        {
            Color = SKColor.Parse("#223B35"),
            IsAntialias = true,
            TextSize = 11,
            Typeface = SKTypeface.FromFamilyName("Microsoft YaHei UI", SKFontStyleWeight.SemiBold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
        };
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
            IsAntialias = true,
            TextSize = records.Length <= 10 ? 15 : 13,
            TextAlign = SKTextAlign.Center,
            Typeface = SKTypeface.FromFamilyName("Microsoft YaHei UI")
        };

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
        var showLabels = records.Length <= 14;

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

                if (showLabels)
                {
                    DrawHistoryBarLabel(canvas, labelBackgroundPaint, labelBorderPaint, labelTextPaint, left, barWidth, primary[i], yAxisMax, plotRect);
                    DrawHistoryBarLabel(canvas, labelBackgroundPaint, labelBorderPaint, labelTextPaint, left + barWidth + gap, barWidth, secondary[i], yAxisMax, plotRect);
                }

                DrawHistoryDateLabel(canvas, dateTextPaint, records[i].Day, left + groupWidth / 2f, plotRect.Bottom + 24, records.Length);
            }
            else
            {
                totalPaint.Color = GetHistoryBarColor(isNetwork ? "#2A6FBB" : "#176B5A", weekTint);
                DrawHistoryBar(canvas, totalPaint, left, groupWidth, total[i], yAxisMax, plotRect);

                if (showLabels)
                {
                    DrawHistoryBarLabel(canvas, labelBackgroundPaint, labelBorderPaint, labelTextPaint, left, groupWidth, total[i], yAxisMax, plotRect);
                }

                DrawHistoryDateLabel(canvas, dateTextPaint, records[i].Day, left + groupWidth / 2f, plotRect.Bottom + 24, records.Length);
            }
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        var bytes = data.ToArray();
        using var stream = new MemoryStream(bytes);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return new SingleChartRenderResult(bitmap, FormatBytes(yAxisMax));
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

    private static void DrawHistoryBarLabel(
        SKCanvas canvas,
        SKPaint backgroundPaint,
        SKPaint borderPaint,
        SKPaint textPaint,
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

        var label = FormatBytes(value);
        var bounds = new SKRect();
        textPaint.MeasureText(label, ref bounds);
        var paddingX = 6f;
        var labelWidth = bounds.Width + paddingX * 2;
        var labelHeight = 18f;
        const float labelGap = 4f;
        const float labelHeadroom = 24f;
        var drawableHeight = Math.Max(0f, plotRect.Height - labelHeadroom);
        var height = (float)(value / maxValue * drawableHeight);
        var centerX = left + width / 2f;
        var labelTop = Math.Max(plotRect.Top + 2, plotRect.Bottom - height - labelHeight - labelGap);
        var labelBottom = labelTop + labelHeight;
        var rect = new SKRect(
            centerX - labelWidth / 2f,
            labelTop,
            centerX + labelWidth / 2f,
            labelBottom);
        var textBaseline = rect.MidY - (bounds.Top + bounds.Bottom) / 2f;
        canvas.DrawRoundRect(rect, 7, 7, backgroundPaint);
        canvas.DrawRoundRect(rect, 7, 7, borderPaint);
        canvas.DrawText(label, rect.Left + paddingX, textBaseline, textPaint);
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

        canvas.DrawText(label, centerX, baselineY, textPaint);
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
        DateOnly selectedDate)
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
            _ => records
                .Where(record => string.Equals(record.Day, selectedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparison.Ordinal))
                .OrderBy(record => record.Day, StringComparer.Ordinal)
                .ToList()
        };
    }

    private static DateOnly GetWeekStart(DateOnly date)
    {
        return date.AddDays(-(int)date.DayOfWeek);
    }

    private static ApplicationHistorySummary BuildHistorySummary(
        IReadOnlyList<DailyProcessActivitySummary> records,
        ProcessResourceSnapshot snapshot,
        HistoryAnalysisDimension dimension,
        DateOnly selectedDate)
    {
        var rangeDisplay = dimension switch
        {
            HistoryAnalysisDimension.Week => $"{GetWeekStart(selectedDate):yyyy-MM-dd} 至 {GetWeekStart(selectedDate).AddDays(6):yyyy-MM-dd}",
            HistoryAnalysisDimension.Month => $"{selectedDate:yyyy 年 MM 月}",
            _ => $"{selectedDate:yyyy-MM-dd}"
        };

        if (records.Count == 0)
        {
            return ApplicationHistorySummary.Empty with
            {
                Caption = dimension switch
                {
                    HistoryAnalysisDimension.Week => "按周历史",
                    HistoryAnalysisDimension.Month => "按月历史",
                    _ => "按日历史"
                },
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

        return new ApplicationHistorySummary
        {
            Caption = dimension switch
            {
                HistoryAnalysisDimension.Week => "按周历史",
                HistoryAnalysisDimension.Month => "按月历史",
                _ => "按日历史"
            },
            RangeDisplay = rangeDisplay,
            ActiveDaysDisplay = $"活跃天数 {ordered.Count}",
            TotalUsageDisplay = FormatDuration(totalForegroundMilliseconds + totalBackgroundMilliseconds),
            DownloadDisplay = FormatBytes(totalDownloadBytes),
            UploadDisplay = FormatBytes(totalUploadBytes),
            TotalTrafficDisplay = FormatBytes(totalDownloadBytes + totalUploadBytes),
            IoReadDisplay = FormatBytes(totalIoReadBytes),
            IoWriteDisplay = FormatBytes(totalIoWriteBytes),
            TotalIoDisplay = FormatBytes(totalIoReadBytes + totalIoWriteBytes),
            AverageCpuDisplay = $"{averageCpu:F1}%",
            AverageIopsDisplay = averageIops.ToString("F1", CultureInfo.InvariantCulture),
            PeakWorkingSetDisplay = FormatBytes(peakWorkingSetBytes),
            ThreadSummaryDisplay = $"均值 {averageThreadCount:F1} / 峰值 {peakThreadCount}",
            HabitInsight = BuildHistoryHabitInsight(focusRatio),
            PerformanceInsight = BuildHistoryPerformanceInsight(totalDownloadBytes + totalUploadBytes, totalIoReadBytes + totalIoWriteBytes, averageCpu),
            ExecutablePathDisplay = executablePath,
            ChartStartLabel = firstDay,
            ChartEndLabel = lastDay
        };
    }

    private static string BuildHistoryHabitInsight(double foregroundRatio)
    {
        if (foregroundRatio <= 0)
        {
            return "历史样本不足，暂时无法判断该应用的前后台使用倾向。";
        }

        if (foregroundRatio >= 0.7d)
        {
            return "历史上该应用以前台主动使用为主，更接近被反复打开和持续操作的工具。";
        }

        if (foregroundRatio <= 0.3d)
        {
            return "历史上该应用更多处于后台驻留状态，偏向服务型、同步型或托盘型进程。";
        }

        return "历史上该应用的前后台占比较均衡，使用方式在交互和驻留之间切换频繁。";
    }

    private static string BuildHistoryPerformanceInsight(long totalTrafficBytes, long totalIoBytes, double averageCpu)
    {
        if (averageCpu >= 15d && totalTrafficBytes >= 500L * 1024 * 1024)
        {
            return "从历史看，该应用同时存在较持续的计算与网络负载，常见于同步、下载或在线处理任务。";
        }

        if (totalIoBytes >= 500L * 1024 * 1024)
        {
            return "从历史看，该应用的磁盘读写较活跃，适合优先关注缓存、落盘和批量处理行为。";
        }

        if (averageCpu >= 15d)
        {
            return "从历史看，该应用更偏本地计算型负载，CPU 占用长期高于普通驻留应用。";
        }

        return "从历史看，该应用整体负载较温和，更像常规使用或轻量驻留。";
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

    private static string BuildHabitInsight(ProcessResourceSnapshot snapshot)
    {
        var total = snapshot.DailyForegroundMilliseconds + snapshot.DailyBackgroundMilliseconds;
        if (total <= 0)
        {
            return "尚未积累足够的前后台使用行为数据。";
        }

        if (snapshot.DailyForegroundMilliseconds >= snapshot.DailyBackgroundMilliseconds * 1.8)
        {
            return "该应用明显以主动交互为主，通常在被打开后保持较长时间停留。";
        }

        if (snapshot.DailyBackgroundMilliseconds >= snapshot.DailyForegroundMilliseconds * 1.8)
        {
            return "该应用大部分时间处于后台常驻状态，更像服务型或托盘型进程。";
        }

        return "该应用在前台使用和后台驻留之间较为均衡，使用场景切换频繁。";
    }

    private static string BuildPerformanceInsight(ProcessResourceSnapshot snapshot)
    {
        var hotNetwork = snapshot.RealtimeDownloadBytesPerSecond + snapshot.RealtimeUploadBytesPerSecond >= 5L * 1024 * 1024;
        var hotIo = snapshot.RealtimeIoReadBytesPerSecond + snapshot.RealtimeIoWriteBytesPerSecond >= 5L * 1024 * 1024;
        var hotCpu = snapshot.CpuUsagePercent >= 20d;

        if (hotCpu && hotNetwork)
        {
            return "当前同时存在明显的计算与网络负载，适合优先排查活跃任务、下载或同步行为。";
        }

        if (hotIo)
        {
            return "当前应用吞吐较高，若界面卡顿或系统响应下降，可优先关注该应用的读写行为。";
        }

        if (hotCpu)
        {
            return "当前 CPU 压力较明显，但网络与IO负载相对稳定，偏向本地计算型任务。";
        }

        return "当前性能压力较温和，更多体现为常规驻留与轻量交互。";
    }

    private sealed record ApplicationHistorySummary
    {
        public static readonly ApplicationHistorySummary Empty = new()
        {
            Caption = "历史统计",
            RangeDisplay = "暂无历史数据",
            ActiveDaysDisplay = "活跃天数 0",
            TotalUsageDisplay = "00:00:00",
            DownloadDisplay = "0 B",
            UploadDisplay = "0 B",
            TotalTrafficDisplay = "0 B",
            IoReadDisplay = "0 B",
            IoWriteDisplay = "0 B",
            TotalIoDisplay = "0 B",
            AverageCpuDisplay = "0.0%",
            AverageIopsDisplay = "0.0",
            PeakWorkingSetDisplay = "0 B",
            ThreadSummaryDisplay = "均值 0.0 / 峰值 0",
            HabitInsight = "尚未积累足够的历史样本。",
            PerformanceInsight = "尚未积累足够的历史样本。",
            ExecutablePathDisplay = "-",
            ChartStartLabel = "-",
            ChartEndLabel = "-"
        };

        public string Caption { get; init; } = string.Empty;
        public string RangeDisplay { get; init; } = string.Empty;
        public string ActiveDaysDisplay { get; init; } = string.Empty;
        public string TotalUsageDisplay { get; init; } = string.Empty;
        public string DownloadDisplay { get; init; } = string.Empty;
        public string UploadDisplay { get; init; } = string.Empty;
        public string TotalTrafficDisplay { get; init; } = string.Empty;
        public string IoReadDisplay { get; init; } = string.Empty;
        public string IoWriteDisplay { get; init; } = string.Empty;
        public string TotalIoDisplay { get; init; } = string.Empty;
        public string AverageCpuDisplay { get; init; } = string.Empty;
        public string AverageIopsDisplay { get; init; } = string.Empty;
        public string PeakWorkingSetDisplay { get; init; } = string.Empty;
        public string ThreadSummaryDisplay { get; init; } = string.Empty;
        public string HabitInsight { get; init; } = string.Empty;
        public string PerformanceInsight { get; init; } = string.Empty;
        public string ExecutablePathDisplay { get; init; } = string.Empty;
        public string ChartStartLabel { get; init; } = string.Empty;
        public string ChartEndLabel { get; init; } = string.Empty;
    }

    public sealed class ApplicationHistoryCalendarDayViewModel
    {
        public ApplicationHistoryCalendarDayViewModel(
            DateOnly date,
            bool isInDisplayedMonth,
            bool hasData,
            bool isSelected,
            bool isRangeStart,
            bool isRangeEnd,
            Action<DateOnly> onSelect)
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
            SelectCommand = new RelayCommand(() => onSelect(date));
        }

        public DateOnly Date { get; }
        public string DayNumber { get; }
        public bool IsInDisplayedMonth { get; }
        public bool HasData { get; }
        public bool IsSelected { get; }
        public bool IsRangeStart { get; }
        public bool IsRangeEnd { get; }
        public bool IsSingleSelection { get; }
        public bool IsRangeStartOnly { get; }
        public bool IsRangeEndOnly { get; }
        public bool IsRangeMiddle { get; }
        public ICommand SelectCommand { get; }
    }

    private sealed record TimedMetricSample(DateTime TimestampUtc, double PrimaryValue, double SecondaryValue);
    private sealed record SingleChartRenderResult(ImageSource? Source, string TopLabel)
    {
        public static readonly SingleChartRenderResult Empty = new(null, "1 KB/s");
    }
    private sealed record ChartRenderResult(SingleChartRenderResult NetworkChart, SingleChartRenderResult IoChart);
}
