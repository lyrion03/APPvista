using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using APPvista.Desktop.Services;
using APPvista.Domain.Entities;

namespace APPvista.Desktop.ViewModels;

public sealed partial class DashboardViewModel
{
    private enum DashboardPage
    {
        Realtime,
        History
    }

    private enum HistoryDimension
    {
        Day,
        Week,
        Month,
        Custom
    }

    private enum HistoryNetworkDisplayMode
    {
        Total,
        Split
    }

    private enum HistoryIoDisplayMode
    {
        Total,
        Split
    }

    private DashboardPage _selectedDashboardPage = DashboardPage.Realtime;
    private HistoryDimension _selectedHistoryDimension = HistoryDimension.Day;
    private HistoryResourceSummary _historySummary = HistoryResourceSummary.Empty;
    private DateOnly _historyDisplayedMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateOnly _historySelectedDate = DateOnly.FromDateTime(DateTime.Today);
    private readonly HashSet<DateOnly> _historyCustomSelectedDates = [];
    private HistoryNetworkDisplayMode _historyNetworkDisplayMode = HistoryNetworkDisplayMode.Total;
    private HistoryIoDisplayMode _historyIoDisplayMode = HistoryIoDisplayMode.Total;
    private int _historyActiveApplicationCount;
    private int _historyAverageApplicationCount;
    private (HistoryDimension TargetDimension, DateOnly RangeStart, DateOnly RangeEnd)? _pendingHistorySelectionRange;
    private int _historyTopN = 3;
    private IReadOnlyList<HistoryDailyRecord> _historyCalendarRecords = [];
    private ImageSource? _historyTrafficPieChartSource;
    private ImageSource? _historyIoPieChartSource;
    private ImageSource? _historyForegroundPieChartSource;
    private int _historyAnalysisLoadVersion;

    public ObservableCollection<HistoryCalendarDayViewModel> HistoryCalendarDays { get; }
    public ObservableCollection<HistoryRankingItemViewModel> HistoryTrafficTopApplications { get; }
    public ObservableCollection<HistoryRankingItemViewModel> HistoryIoTopApplications { get; }
    public ObservableCollection<HistoryRankingItemViewModel> HistoryForegroundTopApplications { get; }

    public ICommand ShowRealtimePageCommand { get; }
    public ICommand ShowHistoryPageCommand { get; }
    public ICommand SetHistoryDayDimensionCommand { get; }
    public ICommand SetHistoryWeekDimensionCommand { get; }
    public ICommand SetHistoryMonthDimensionCommand { get; }
    public ICommand SetHistoryCustomDimensionCommand { get; }
    public ICommand ShowPreviousHistoryMonthCommand { get; }
    public ICommand ShowNextHistoryMonthCommand { get; }
    public ICommand SetHistoryNetworkTotalDisplayCommand { get; }
    public ICommand SetHistoryNetworkSplitDisplayCommand { get; }
    public ICommand SetHistoryIoTotalDisplayCommand { get; }
    public ICommand SetHistoryIoSplitDisplayCommand { get; }
    public ICommand ShowHistoryComparisonCommand { get; }

    public bool IsRealtimePageActive => _selectedDashboardPage == DashboardPage.Realtime;
    public bool IsHistoryPageActive => _selectedDashboardPage == DashboardPage.History;
    public bool IsHistoryDayDimension => _selectedHistoryDimension == HistoryDimension.Day;
    public bool IsHistoryWeekDimension => _selectedHistoryDimension == HistoryDimension.Week;
    public bool IsHistoryMonthDimension => _selectedHistoryDimension == HistoryDimension.Month;
    public bool IsHistoryCustomDimension => _selectedHistoryDimension == HistoryDimension.Custom;
    public string HistoryDimensionTitle => _selectedHistoryDimension switch
    {
        HistoryDimension.Week => "按周",
        HistoryDimension.Month => "按月",
        HistoryDimension.Custom => "自选",
        _ => "按日"
    };
    public string HistoryDimensionHeadline => _selectedHistoryDimension switch
    {
        HistoryDimension.Week => $"{HistoryDimensionTitle} · 期间活跃应用数 {_historyActiveApplicationCount} · 日均活跃应用数 {_historyAverageApplicationCount}",
        HistoryDimension.Month => $"{HistoryDimensionTitle} · 期间活跃应用数 {_historyActiveApplicationCount} · 日均活跃应用数 {_historyAverageApplicationCount}",
        HistoryDimension.Custom => $"{HistoryDimensionTitle} · 期间活跃应用数 {_historyActiveApplicationCount} · 日均活跃应用数 {_historyAverageApplicationCount}",
        _ => $"{HistoryDimensionTitle} · 期间活跃应用数 {_historyActiveApplicationCount}"
    };

    public string HistorySummaryCaption => _selectedHistoryDimension switch
    {
        HistoryDimension.Month => "所选月汇总数据",
        HistoryDimension.Custom => "所选区间汇总数据",
        _ => "所选日汇总数据"
    };
    public bool IsHistoryNetworkTotalMode => _historyNetworkDisplayMode == HistoryNetworkDisplayMode.Total;
    public bool IsHistoryNetworkSplitMode => _historyNetworkDisplayMode == HistoryNetworkDisplayMode.Split;
    public bool IsHistoryIoTotalMode => _historyIoDisplayMode == HistoryIoDisplayMode.Total;
    public bool IsHistoryIoSplitMode => _historyIoDisplayMode == HistoryIoDisplayMode.Split;
    public string HistoryApplicationNetworkDisplay => BuildHistoryNetworkDisplay(_historySummary.AppDownloadBytes, _historySummary.AppUploadBytes, _historySummary.AppPeakDownloadBytes, _historySummary.AppPeakUploadBytes);
    public string HistoryApplicationIoDisplay => BuildHistoryIoDisplay(_historySummary.AppIoReadBytes, _historySummary.AppIoWriteBytes, _historySummary.AppPeakIoReadBytes, _historySummary.AppPeakIoWriteBytes);
    public string HistorySystemNetworkDisplay => BuildHistoryNetworkDisplay(_historySummary.SystemDownloadBytes, _historySummary.SystemUploadBytes, _historySummary.SystemPeakDownloadBytes, _historySummary.SystemPeakUploadBytes);
    public string HistorySystemIoDisplay => BuildHistoryIoDisplay(_historySummary.SystemIoReadBytes, _historySummary.SystemIoWriteBytes, _historySummary.SystemPeakIoReadBytes, _historySummary.SystemPeakIoWriteBytes);
    public string HistoryCalendarMonthDisplay => $"{_historyDisplayedMonth:yyyy 年 MM 月}";
    public int HistoryTopN
    {
        get => _historyTopN;
        set => SetHistoryTopN(value);
    }
    public string HistoryTrafficTopTitle => $"流量 Top {_historyTopN} 应用";
    public string HistoryIoTopTitle => $"I/O Top {_historyTopN} 应用";
    public string HistoryForegroundTopTitle => $"前台时长 Top {_historyTopN} 应用";
    public ImageSource? HistoryTrafficPieChartSource => _historyTrafficPieChartSource;
    public ImageSource? HistoryIoPieChartSource => _historyIoPieChartSource;
    public ImageSource? HistoryForegroundPieChartSource => _historyForegroundPieChartSource;

    public string HistoryCalendarSelectionDisplay => _selectedHistoryDimension switch
    {
        HistoryDimension.Week => $"已选周：{GetWeekStart(_historySelectedDate):yyyy-MM-dd}-{GetWeekStart(_historySelectedDate).AddDays(6):yyyy-MM-dd}",
        HistoryDimension.Month => $"已选月：{_historyDisplayedMonth:yyyy 年 MM 月}",
        HistoryDimension.Custom => $"已选区间：{BuildHistoryCustomRangeDisplay()}",
        _ => $"已选日：{_historySelectedDate:yyyy-MM-dd}"
    };

    private void ShowRealtimePage()
    {
        if (_selectedDashboardPage == DashboardPage.Realtime)
        {
            return;
        }

        _selectedDashboardPage = DashboardPage.Realtime;
        RaiseHistoryStateChanged();
        if (_isMainWindowRenderingActive)
        {
            RaiseOverviewChartProperties();
        }
    }

    private void ShowHistoryPage()
    {
        if (_selectedDashboardPage == DashboardPage.History)
        {
            if (_isMainWindowRenderingActive)
            {
                LoadHistoryAnalysis();
            }
            return;
        }

        _selectedDashboardPage = DashboardPage.History;
        if (_isMainWindowRenderingActive)
        {
            LoadHistoryAnalysis();
        }
        else
        {
            _hasDeferredMainWindowRefresh = true;
        }
        RaiseHistoryStateChanged();
    }

    private void SetHistoryDayDimension()
    {
        SetHistoryDimension(HistoryDimension.Day);
    }

    private void SetHistoryWeekDimension()
    {
        SetHistoryDimension(HistoryDimension.Week);
    }

    private void SetHistoryMonthDimension()
    {
        SetHistoryDimension(HistoryDimension.Month);
    }

    private void SetHistoryCustomDimension()
    {
        SetHistoryDimension(HistoryDimension.Custom);
    }

    private void SetHistoryNetworkTotalDisplay()
    {
        if (_historyNetworkDisplayMode == HistoryNetworkDisplayMode.Total)
        {
            return;
        }

        _historyNetworkDisplayMode = HistoryNetworkDisplayMode.Total;
        RaiseHistorySummaryChanged();
    }

    private void SetHistoryNetworkSplitDisplay()
    {
        if (_historyNetworkDisplayMode == HistoryNetworkDisplayMode.Split)
        {
            return;
        }

        _historyNetworkDisplayMode = HistoryNetworkDisplayMode.Split;
        RaiseHistorySummaryChanged();
    }

    private void SetHistoryIoTotalDisplay()
    {
        if (_historyIoDisplayMode == HistoryIoDisplayMode.Total)
        {
            return;
        }

        _historyIoDisplayMode = HistoryIoDisplayMode.Total;
        RaiseHistorySummaryChanged();
    }

    private void SetHistoryIoSplitDisplay()
    {
        if (_historyIoDisplayMode == HistoryIoDisplayMode.Split)
        {
            return;
        }

        _historyIoDisplayMode = HistoryIoDisplayMode.Split;
        RaiseHistorySummaryChanged();
    }

    private void OpenHistoryComparisonWindow()
    {
        var selectedDimension = _selectedHistoryDimension;
        var selectedDate = _historySelectedDate;
        var displayedMonth = _historyDisplayedMonth;
        IReadOnlyList<HistoryApplicationAggregate> applicationRecords;
        if (selectedDimension == HistoryDimension.Custom)
        {
            var selectedDays = GetCustomSelectedDays();
            applicationRecords = MergeLiveTodayComparisonApplicationAggregates(
                _historyAnalysisProvider.LoadApplicationAggregates(selectedDays),
                selectedDays);
        }
        else
        {
            var (rangeStart, rangeEnd) = ResolveHistoryRange(selectedDimension, selectedDate, displayedMonth);
            applicationRecords = MergeLiveTodayComparisonApplicationAggregates(
                _historyAnalysisProvider.LoadApplicationAggregates(rangeStart, rangeEnd),
                rangeStart,
                rangeEnd);
        }

        var windowTitle = $"详细对比 · {HistoryDimensionTitle}";
        var rangeDisplay = HistoryCalendarSelectionDisplay;

        if (_historyComparisonWindow?.DataContext is HistoryComparisonViewModel existingViewModel)
        {
            existingViewModel.Load(windowTitle, rangeDisplay, applicationRecords);
            _historyComparisonWindow.WindowState = WindowState.Maximized;

            _historyComparisonWindow.Show();
            _historyComparisonWindow.Activate();
            _historyComparisonWindow.Topmost = true;
            _historyComparisonWindow.Topmost = false;
            _historyComparisonWindow.Focus();
            return;
        }

        var viewModel = new HistoryComparisonViewModel(
            _applicationIconCache,
            new Dictionary<string, string>(_applicationAliases, StringComparer.OrdinalIgnoreCase),
            applicationRecords,
            windowTitle,
            rangeDisplay);
        var comparisonWindow = new APPvista.Desktop.HistoryComparisonWindow(viewModel)
        {
            Owner = System.Windows.Application.Current?.MainWindow,
            WindowState = WindowState.Maximized
        };
        _historyComparisonWindow = comparisonWindow;
        comparisonWindow.Closed += (_, _) => _historyComparisonWindow = null;
        comparisonWindow.Show();
    }

    private void SetHistoryDimension(HistoryDimension dimension)
    {
        if (_selectedHistoryDimension == dimension)
        {
            return;
        }

        var previousDimension = _selectedHistoryDimension;
        _selectedHistoryDimension = dimension;
        if (dimension == HistoryDimension.Custom)
        {
            SeedCustomSelectionFromCurrentDimension(previousDimension);
            _pendingHistorySelectionRange = null;
        }
        else if (previousDimension == HistoryDimension.Custom)
        {
            var anchorDate = GetHistoryCustomSelectionAnchor();
            _historySelectedDate = dimension == HistoryDimension.Week
                ? GetWeekStart(anchorDate)
                : dimension == HistoryDimension.Month
                    ? new DateOnly(anchorDate.Year, anchorDate.Month, 1)
                    : anchorDate;
            _historyDisplayedMonth = new DateOnly(_historySelectedDate.Year, _historySelectedDate.Month, 1);
            _pendingHistorySelectionRange = null;
        }
        else if (dimension == HistoryDimension.Month)
        {
            _historySelectedDate = _historyDisplayedMonth;
        }
        else if (dimension == HistoryDimension.Week && previousDimension == HistoryDimension.Month)
        {
            var pendingRange = ResolveHistoryRange(previousDimension, _historySelectedDate, _historyDisplayedMonth);
            _pendingHistorySelectionRange = (dimension, pendingRange.Start, pendingRange.End);
            _historySelectedDate = ResolveWeekHistorySelection(previousDimension, _historySelectedDate, _historyDisplayedMonth);
            _historyDisplayedMonth = new DateOnly(_historySelectedDate.Year, _historySelectedDate.Month, 1);
        }
        else if (dimension == HistoryDimension.Day)
        {
            var pendingRange = ResolveHistoryRange(previousDimension, _historySelectedDate, _historyDisplayedMonth);
            _pendingHistorySelectionRange = (dimension, pendingRange.Start, pendingRange.End);
            _historySelectedDate = ResolveDayHistorySelection(previousDimension, _historySelectedDate, _historyDisplayedMonth);
            _historyDisplayedMonth = new DateOnly(_historySelectedDate.Year, _historySelectedDate.Month, 1);
        }
        else
        {
            _pendingHistorySelectionRange = null;
        }

        RefreshHistoryCalendar();
        if (_isMainWindowRenderingActive && IsHistoryPageActive)
        {
            LoadHistoryAnalysis();
        }
        else
        {
            _hasDeferredMainWindowRefresh = true;
        }
        RaiseHistoryStateChanged();
    }

    private void ShowPreviousHistoryMonth()
    {
        _historyDisplayedMonth = _historyDisplayedMonth.AddMonths(-1);
        if (_selectedHistoryDimension == HistoryDimension.Month)
        {
            _historySelectedDate = _historyDisplayedMonth;
            if (_isMainWindowRenderingActive && IsHistoryPageActive)
            {
                LoadHistoryAnalysis();
            }
            else
            {
                _hasDeferredMainWindowRefresh = true;
            }
        }

        RefreshHistoryCalendar();
        RaisePropertyChanged(nameof(HistoryCalendarMonthDisplay));
        RaisePropertyChanged(nameof(HistoryCalendarSelectionDisplay));
    }

    private void ShowNextHistoryMonth()
    {
        _historyDisplayedMonth = _historyDisplayedMonth.AddMonths(1);
        if (_selectedHistoryDimension == HistoryDimension.Month)
        {
            _historySelectedDate = _historyDisplayedMonth;
            if (_isMainWindowRenderingActive && IsHistoryPageActive)
            {
                LoadHistoryAnalysis();
            }
            else
            {
                _hasDeferredMainWindowRefresh = true;
            }
        }

        RefreshHistoryCalendar();
        RaisePropertyChanged(nameof(HistoryCalendarMonthDisplay));
        RaisePropertyChanged(nameof(HistoryCalendarSelectionDisplay));
    }

    private void SelectHistoryDate(DateOnly date)
    {
        if (_selectedHistoryDimension == HistoryDimension.Month)
        {
            return;
        }

        _historySelectedDate = date;
        _historyDisplayedMonth = new DateOnly(date.Year, date.Month, 1);
        RefreshHistoryCalendar();
        if (_isMainWindowRenderingActive && IsHistoryPageActive)
        {
            LoadHistoryAnalysis();
        }
        else
        {
            _hasDeferredMainWindowRefresh = true;
        }
        RaisePropertyChanged(nameof(HistoryCalendarMonthDisplay));
        RaisePropertyChanged(nameof(HistoryCalendarSelectionDisplay));
    }

    public void SetHistoryCustomDateSelection(DateOnly date, bool isSelected)
    {
        if (_selectedHistoryDimension != HistoryDimension.Custom)
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

        RefreshHistoryCalendar();
        if (_isMainWindowRenderingActive && IsHistoryPageActive)
        {
            LoadHistoryAnalysis();
        }
        else
        {
            _hasDeferredMainWindowRefresh = true;
        }

        RaisePropertyChanged(nameof(HistoryCalendarSelectionDisplay));
        RaisePropertyChanged(nameof(HistoryDimensionHeadline));
    }

    private void LoadHistoryAnalysis()
    {
        _ = LoadHistoryAnalysisAsync();
    }

    private async Task LoadHistoryAnalysisAsync()
    {
        var version = Interlocked.Increment(ref _historyAnalysisLoadVersion);
        var selectedDimension = _selectedHistoryDimension;
        var selectedDate = _historySelectedDate;
        var displayedMonth = _historyDisplayedMonth;
        var selectedCustomDays = selectedDimension == HistoryDimension.Custom ? GetCustomSelectedDays() : Array.Empty<DateOnly>();
        var (rangeStart, rangeEnd) = ResolveHistoryRange(selectedDimension, selectedDate, displayedMonth);

        var loaded = await Task.Run(() =>
        {
            var dailyRecords = _historyAnalysisProvider.LoadDailyRecords(maxDays: 120);
            var applicationRecords = selectedDimension == HistoryDimension.Custom
                ? _historyAnalysisProvider.LoadOverviewApplicationAggregates(selectedCustomDays)
                : _historyAnalysisProvider.LoadOverviewApplicationAggregates(rangeStart, rangeEnd);

            return new
            {
                DailyRecords = dailyRecords,
                ApplicationRecords = applicationRecords
            };
        });

        if (version != _historyAnalysisLoadVersion)
        {
            return;
        }

        var dailyRecords = MergeLiveTodayRecord(loaded.DailyRecords);
        _historyCalendarRecords = dailyRecords;
        if (_pendingHistorySelectionRange is { } pendingRange && selectedDimension == pendingRange.TargetDimension)
        {
            var resolvedDate = pendingRange.TargetDimension switch
            {
                HistoryDimension.Week => GetWeekStart(
                    ResolveEarliestHistoryDate(dailyRecords, pendingRange.RangeStart, pendingRange.RangeEnd)
                    ?? pendingRange.RangeStart),
                HistoryDimension.Day => ResolveEarliestHistoryDate(dailyRecords, pendingRange.RangeStart, pendingRange.RangeEnd)
                    ?? pendingRange.RangeStart,
                _ => _historySelectedDate
            };

            if (resolvedDate != _historySelectedDate)
            {
                _historySelectedDate = resolvedDate;
                _historyDisplayedMonth = new DateOnly(resolvedDate.Year, resolvedDate.Month, 1);
                selectedDate = _historySelectedDate;
                displayedMonth = _historyDisplayedMonth;
            }

            _pendingHistorySelectionRange = null;
        }

        RefreshHistoryCalendar();
        var selectedRecords = SelectHistoryRecords(dailyRecords, selectedDimension, selectedDate, displayedMonth, selectedCustomDays);
        var applicationRecords = selectedDimension == HistoryDimension.Custom
            ? MergeLiveTodayOverviewApplicationAggregates(loaded.ApplicationRecords, selectedCustomDays)
            : MergeLiveTodayOverviewApplicationAggregates(
                loaded.ApplicationRecords,
                rangeStart,
                rangeEnd);

        _historySummary = BuildHistorySummary(selectedRecords, selectedDimension, selectedDate, displayedMonth, selectedCustomDays);
        _historyActiveApplicationCount = applicationRecords.Count;
        _historyAverageApplicationCount = selectedRecords.Count == 0
            ? 0
            : (int)Math.Round(selectedRecords.Average(static record => record.ApplicationCount), MidpointRounding.AwayFromZero);
        RefreshHistoryTopApplications(applicationRecords);
        RaisePropertyChanged(nameof(HistoryDimensionTitle));
        RaisePropertyChanged(nameof(HistoryDimensionHeadline));
        RaiseHistorySummaryChanged();
    }

    private IReadOnlyList<HistoryDailyRecord> MergeLiveTodayRecord(IReadOnlyList<HistoryDailyRecord> dailyRecords)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var records = dailyRecords.ToList();
        var existingIndex = records.FindIndex(record => record.Day == today);
        var baselineRecord = existingIndex >= 0 ? records[existingIndex] : default;
        var activeProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Snapshot.TopProcesses)
        {
            if (!string.IsNullOrWhiteSpace(process.ProcessName))
            {
                activeProcessNames.Add(process.ProcessName);
            }
        }

        var mergedRecord = baselineRecord with
        {
            Day = today,
            ApplicationCount = activeProcessNames.Count,
            AppDownloadBytes = Snapshot.TodayDownloadBytes,
            AppUploadBytes = Snapshot.TodayUploadBytes,
            AppIoReadBytes = Snapshot.TodayIoReadBytes,
            AppIoWriteBytes = Snapshot.TodayIoWriteBytes,
            SystemDownloadBytes = _systemOverviewSnapshot.TodayDownloadBytes,
            SystemUploadBytes = _systemOverviewSnapshot.TodayUploadBytes,
            SystemIoReadBytes = _systemOverviewSnapshot.TodayIoReadBytes,
            SystemIoWriteBytes = _systemOverviewSnapshot.TodayIoWriteBytes
        };

        if (existingIndex >= 0)
        {
            records[existingIndex] = mergedRecord;
        }
        else
        {
            records.Add(mergedRecord);
        }

        return records
            .OrderByDescending(record => record.Day)
            .ToList();
    }

    private void RefreshHistoryCalendar()
    {
        HistoryCalendarDays.Clear();

        var firstOfMonth = _historyDisplayedMonth;
        var firstVisible = GetWeekStart(firstOfMonth);
        var selectedWeekStart = GetWeekStart(_historySelectedDate);
        var selectedWeekEnd = selectedWeekStart.AddDays(6);
        var monthEnd = _historyDisplayedMonth.AddMonths(1).AddDays(-1);
        var daysWithData = _historyCalendarRecords
            .Select(record => record.Day)
            .ToHashSet();

        for (var i = 0; i < 42; i++)
        {
            var date = firstVisible.AddDays(i);
            var isSelected = _selectedHistoryDimension switch
            {
                HistoryDimension.Week => date >= selectedWeekStart && date <= selectedWeekEnd,
                HistoryDimension.Month => date >= _historyDisplayedMonth && date <= monthEnd,
                HistoryDimension.Custom => _historyCustomSelectedDates.Contains(date),
                _ => date == _historySelectedDate
            };
            var isRangeStart = _selectedHistoryDimension == HistoryDimension.Custom
                ? isSelected && !_historyCustomSelectedDates.Contains(date.AddDays(-1))
                : isSelected && (_selectedHistoryDimension == HistoryDimension.Day || date == selectedWeekStart || date == _historyDisplayedMonth);
            var isRangeEnd = _selectedHistoryDimension == HistoryDimension.Custom
                ? isSelected && !_historyCustomSelectedDates.Contains(date.AddDays(1))
                : isSelected && (_selectedHistoryDimension == HistoryDimension.Day || date == selectedWeekEnd || date == monthEnd);
            var isSelectable = date.Month == _historyDisplayedMonth.Month &&
                               date.Year == _historyDisplayedMonth.Year &&
                               daysWithData.Contains(date);

            HistoryCalendarDays.Add(new HistoryCalendarDayViewModel(
                date,
                isInDisplayedMonth: date.Month == _historyDisplayedMonth.Month && date.Year == _historyDisplayedMonth.Year,
                hasData: daysWithData.Contains(date),
                isSelected: isSelected,
                isRangeStart: isRangeStart,
                isRangeEnd: isRangeEnd,
                isSelectable: _selectedHistoryDimension != HistoryDimension.Month && isSelectable,
                onSelect: OnHistoryCalendarDateInvoked));
        }
    }

    private static (DateOnly Start, DateOnly End) ResolveHistoryRange(
        HistoryDimension dimension,
        DateOnly selectedDate,
        DateOnly displayedMonth)
    {
        return dimension switch
        {
            HistoryDimension.Week =>
            (
                GetWeekStart(selectedDate),
                GetWeekStart(selectedDate).AddDays(6)
            ),
            HistoryDimension.Month =>
            (
                displayedMonth,
                displayedMonth.AddMonths(1).AddDays(-1)
            ),
            HistoryDimension.Custom =>
            (
                selectedDate,
                selectedDate
            ),
            _ => (selectedDate, selectedDate)
        };
    }

    private IReadOnlyList<HistoryOverviewApplicationAggregate> MergeLiveTodayOverviewApplicationAggregates(
        IReadOnlyList<HistoryOverviewApplicationAggregate> applicationRecords,
        DateOnly rangeStart,
        DateOnly rangeEnd)
    {
        return applicationRecords;
    }

    private IReadOnlyList<HistoryOverviewApplicationAggregate> MergeLiveTodayOverviewApplicationAggregates(
        IReadOnlyList<HistoryOverviewApplicationAggregate> applicationRecords,
        IReadOnlyCollection<DateOnly> selectedDays)
    {
        return selectedDays.Contains(DateOnly.FromDateTime(DateTime.Today))
            ? applicationRecords
            : applicationRecords;
    }

    private IReadOnlyList<HistoryApplicationAggregate> MergeLiveTodayComparisonApplicationAggregates(
        IReadOnlyList<HistoryApplicationAggregate> applicationRecords,
        DateOnly rangeStart,
        DateOnly rangeEnd)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (today < rangeStart || today > rangeEnd)
        {
            return applicationRecords;
        }
        return MergeLiveTodayComparisonApplicationAggregates(applicationRecords, [today]);
    }

    private IReadOnlyList<HistoryApplicationAggregate> MergeLiveTodayComparisonApplicationAggregates(
        IReadOnlyList<HistoryApplicationAggregate> applicationRecords,
        IReadOnlyCollection<DateOnly> selectedDays)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (!selectedDays.Contains(today))
        {
            return applicationRecords;
        }

        var todayStoredAggregates = _historyAnalysisProvider
            .LoadApplicationAggregates(today, today)
            .ToDictionary(static record => record.ProcessName, StringComparer.OrdinalIgnoreCase);
        var merged = applicationRecords.ToDictionary(
            static record => record.ProcessName,
            StringComparer.OrdinalIgnoreCase);

        foreach (var process in Snapshot.TopProcesses)
        {
            if (string.IsNullOrWhiteSpace(process.ProcessName))
            {
                continue;
            }

            if (!merged.TryGetValue(process.ProcessName, out var aggregate))
            {
                aggregate = new HistoryApplicationAggregate
                {
                    ProcessName = process.ProcessName,
                    ExecutablePath = process.ExecutablePath
                };
            }

            if (todayStoredAggregates.TryGetValue(process.ProcessName, out var todayStoredAggregate))
            {
                aggregate = SubtractHistoryApplicationAggregate(aggregate, todayStoredAggregate);
            }

            merged[process.ProcessName] = AddSnapshotTodayAggregate(aggregate, process);
        }

        return merged.Values.ToList();
    }

    private static HistoryApplicationAggregate AddSnapshotTodayAggregate(HistoryApplicationAggregate aggregate, ProcessResourceSnapshot process)
    {
        var foregroundSampleCount = process.DailyForegroundMilliseconds > 0 ? 1 : 0;
        var backgroundSampleCount = process.DailyBackgroundMilliseconds > 0 ? 1 : 0;
        var totalUsageMilliseconds = process.DailyForegroundMilliseconds + process.DailyBackgroundMilliseconds;
        var estimatedIoOperations = totalUsageMilliseconds > 0
            ? (long)Math.Round(process.AverageIops * (totalUsageMilliseconds / 1000d), MidpointRounding.AwayFromZero)
            : 0L;
        var totalIoBytes = process.DailyIoReadBytes + process.DailyIoWriteBytes;
        var estimatedReadOperations = totalIoBytes > 0
            ? (long)Math.Round(estimatedIoOperations * (process.DailyIoReadBytes / (double)totalIoBytes), MidpointRounding.AwayFromZero)
            : totalUsageMilliseconds > 0 ? estimatedIoOperations / 2 : 0L;
        var estimatedWriteOperations = Math.Max(0L, estimatedIoOperations - estimatedReadOperations);

        return aggregate with
        {
            ActiveDays = Math.Max(aggregate.ActiveDays, 1),
            ExecutablePath = string.IsNullOrWhiteSpace(aggregate.ExecutablePath) && !string.IsNullOrWhiteSpace(process.ExecutablePath)
                ? process.ExecutablePath
                : aggregate.ExecutablePath,
            ForegroundMilliseconds = aggregate.ForegroundMilliseconds + process.DailyForegroundMilliseconds,
            BackgroundMilliseconds = aggregate.BackgroundMilliseconds + process.DailyBackgroundMilliseconds,
            DownloadBytes = aggregate.DownloadBytes + process.DailyDownloadBytes,
            UploadBytes = aggregate.UploadBytes + process.DailyUploadBytes,
            IoReadBytes = aggregate.IoReadBytes + process.DailyIoReadBytes,
            IoWriteBytes = aggregate.IoWriteBytes + process.DailyIoWriteBytes,
            ForegroundCpuTotal = aggregate.ForegroundCpuTotal + (process.AverageForegroundCpu * foregroundSampleCount),
            ForegroundWorkingSetTotal = aggregate.ForegroundWorkingSetTotal + (process.AverageForegroundWorkingSetBytes * foregroundSampleCount),
            ForegroundSamples = aggregate.ForegroundSamples + foregroundSampleCount,
            BackgroundCpuTotal = aggregate.BackgroundCpuTotal + (process.AverageBackgroundCpu * backgroundSampleCount),
            BackgroundWorkingSetTotal = aggregate.BackgroundWorkingSetTotal + (process.AverageBackgroundWorkingSetBytes * backgroundSampleCount),
            BackgroundSamples = aggregate.BackgroundSamples + backgroundSampleCount,
            PeakWorkingSetBytes = Math.Max(aggregate.PeakWorkingSetBytes, process.PeakWorkingSetBytes),
            ThreadCountTotal = aggregate.ThreadCountTotal + (process.AverageThreadCount > 0 ? process.AverageThreadCount : 0d),
            ThreadSamples = aggregate.ThreadSamples + (process.AverageThreadCount > 0 ? 1 : 0),
            PeakThreadCount = Math.Max(aggregate.PeakThreadCount, process.PeakThreadCount),
            IoReadOperations = aggregate.IoReadOperations + estimatedReadOperations,
            IoWriteOperations = aggregate.IoWriteOperations + estimatedWriteOperations,
            PeakDownloadBytesPerSecond = Math.Max(aggregate.PeakDownloadBytesPerSecond, process.PeakDownloadBytesPerSecond),
            PeakUploadBytesPerSecond = Math.Max(aggregate.PeakUploadBytesPerSecond, process.PeakUploadBytesPerSecond),
            PeakIoBytesPerSecond = Math.Max(aggregate.PeakIoBytesPerSecond, process.PeakIoBytesPerSecond)
        };
    }

    private static HistoryApplicationAggregate SubtractHistoryApplicationAggregate(
        HistoryApplicationAggregate source,
        HistoryApplicationAggregate subtract)
    {
        return source with
        {
            ActiveDays = Math.Max(0, source.ActiveDays - subtract.ActiveDays),
            ForegroundMilliseconds = Math.Max(0L, source.ForegroundMilliseconds - subtract.ForegroundMilliseconds),
            BackgroundMilliseconds = Math.Max(0L, source.BackgroundMilliseconds - subtract.BackgroundMilliseconds),
            DownloadBytes = Math.Max(0L, source.DownloadBytes - subtract.DownloadBytes),
            UploadBytes = Math.Max(0L, source.UploadBytes - subtract.UploadBytes),
            IoReadBytes = Math.Max(0L, source.IoReadBytes - subtract.IoReadBytes),
            IoWriteBytes = Math.Max(0L, source.IoWriteBytes - subtract.IoWriteBytes),
            ForegroundCpuTotal = Math.Max(0d, source.ForegroundCpuTotal - subtract.ForegroundCpuTotal),
            ForegroundWorkingSetTotal = Math.Max(0d, source.ForegroundWorkingSetTotal - subtract.ForegroundWorkingSetTotal),
            ForegroundSamples = Math.Max(0, source.ForegroundSamples - subtract.ForegroundSamples),
            BackgroundCpuTotal = Math.Max(0d, source.BackgroundCpuTotal - subtract.BackgroundCpuTotal),
            BackgroundWorkingSetTotal = Math.Max(0d, source.BackgroundWorkingSetTotal - subtract.BackgroundWorkingSetTotal),
            BackgroundSamples = Math.Max(0, source.BackgroundSamples - subtract.BackgroundSamples),
            ThreadCountTotal = Math.Max(0d, source.ThreadCountTotal - subtract.ThreadCountTotal),
            ThreadSamples = Math.Max(0, source.ThreadSamples - subtract.ThreadSamples),
            IoReadOperations = Math.Max(0L, source.IoReadOperations - subtract.IoReadOperations),
            IoWriteOperations = Math.Max(0L, source.IoWriteOperations - subtract.IoWriteOperations)
        };
    }

    private void RefreshHistoryTopApplications(IReadOnlyList<HistoryOverviewApplicationAggregate> applicationRecords)
    {
        var trafficRecords = applicationRecords
            .OrderByDescending(static record => record.TotalTrafficBytes)
            .ThenByDescending(static record => record.ForegroundMilliseconds)
            .ThenBy(static record => record.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var ioRecords = applicationRecords
            .OrderByDescending(static record => record.TotalIoBytes)
            .ThenByDescending(static record => record.ForegroundMilliseconds)
            .ThenBy(static record => record.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var foregroundRecords = applicationRecords
            .OrderByDescending(static record => record.ForegroundMilliseconds)
            .ThenByDescending(static record => record.TotalUsageMilliseconds)
            .ThenBy(static record => record.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        UpdateHistoryRankingCollection(
            HistoryTrafficTopApplications,
            trafficRecords
                .Take(_historyTopN)
                .Select((record, index) => CreateHistoryRankingItem(index + 1, record, FormatBytes(record.TotalTrafficBytes)))
                .ToList(),
            "暂无流量数据");

        UpdateHistoryRankingCollection(
            HistoryIoTopApplications,
            ioRecords
                .Take(_historyTopN)
                .Select((record, index) => CreateHistoryRankingItem(index + 1, record, FormatBytes(record.TotalIoBytes)))
                .ToList(),
            "暂无 I/O 数据");

        UpdateHistoryRankingCollection(
            HistoryForegroundTopApplications,
            foregroundRecords
                .Take(_historyTopN)
                .Select((record, index) => CreateHistoryRankingItem(index + 1, record, FormatDuration(record.ForegroundMilliseconds)))
                .ToList(),
            "暂无前台时长数据");

        _historyTrafficPieChartSource = BuildHistoryPieChartImage(
            trafficRecords,
            static record => record.TotalTrafficBytes,
            static record => FormatBytes(record.TotalTrafficBytes));
        _historyIoPieChartSource = BuildHistoryPieChartImage(
            ioRecords,
            static record => record.TotalIoBytes,
            static record => FormatBytes(record.TotalIoBytes));
        _historyForegroundPieChartSource = BuildHistoryPieChartImage(
            foregroundRecords,
            static record => record.ForegroundMilliseconds,
            static record => FormatDuration(record.ForegroundMilliseconds));
    }

    private HistoryRankingItemViewModel CreateHistoryRankingItem(int rank, HistoryOverviewApplicationAggregate record, string metricDisplay)
    {
        return new HistoryRankingItemViewModel(
            rank.ToString(CultureInfo.InvariantCulture),
            BuildHistoryApplicationDisplayName(record.ProcessName, record.ExecutablePath),
            metricDisplay,
            string.IsNullOrWhiteSpace(record.ExecutablePath) ? null : _applicationIconCache.GetIconPath(record.ExecutablePath),
            record.ProcessName,
            record.ExecutablePath,
            new RelayCommand(() => OpenHistoryRankingApplication(record.ProcessName, record.ExecutablePath)));
    }

    private string BuildHistoryApplicationDisplayName(string processName, string executablePath)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return "未知应用";
        }

        string? alias = null;
        if (!string.IsNullOrWhiteSpace(executablePath))
        {
            _applicationAliases.TryGetValue(ApplicationAliasStore.CreateKey(executablePath, processName), out alias);
        }

        alias ??= _applicationAliases.TryGetValue(processName, out var processAlias) ? processAlias : null;

        if (string.IsNullOrWhiteSpace(alias) || string.Equals(alias, processName, StringComparison.OrdinalIgnoreCase))
        {
            return processName;
        }

        return $"{alias}（{processName}）";
    }

    private static void UpdateHistoryRankingCollection(
        ObservableCollection<HistoryRankingItemViewModel> target,
        IReadOnlyList<HistoryRankingItemViewModel> items,
        string emptyMetricDisplay)
    {
        var source = items.Count == 0
            ? [new HistoryRankingItemViewModel("-", "暂无数据", emptyMetricDisplay, null)]
            : items;

        SyncObservableCollection(
            target,
            source,
            static (left, right) =>
                left.Rank == right.Rank &&
                left.ApplicationName == right.ApplicationName &&
                left.MetricDisplay == right.MetricDisplay &&
                left.IconSourcePath == right.IconSourcePath &&
                left.ProcessName == right.ProcessName &&
                left.ExecutablePath == right.ExecutablePath &&
                Equals(left.OpenDetailsCommand, right.OpenDetailsCommand));
    }

    private void OpenHistoryRankingApplication(string processName, string executablePath)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return;
        }

        var liveApplication = FindHistoryRankingLiveApplication(processName, executablePath);
        var displayName = BuildHistoryApplicationDisplayName(processName, executablePath);
        var detailWindow = OpenHistoryDetailWindow(processName, executablePath, displayName, liveApplication);
        if (detailWindow.DataContext is not ApplicationDetailViewModel detailViewModel)
        {
            return;
        }

        detailViewModel.ApplyHistorySelectionFromDashboard(GetHistorySelectionDimensionKey(), _historySelectedDate, _historyCustomSelectedDates);
        detailViewModel.ActivateHistoryMode();
        BringDetailWindowToFront(detailWindow);
    }

    private ApplicationCardViewModel? FindHistoryRankingLiveApplication(string processName, string executablePath)
    {
        return Applications.FirstOrDefault(application =>
            (!string.IsNullOrWhiteSpace(executablePath) &&
             string.Equals(application.Snapshot.ExecutablePath, executablePath, StringComparison.OrdinalIgnoreCase)) ||
            string.Equals(application.OriginalName, processName, StringComparison.OrdinalIgnoreCase));
    }

    private ApplicationDetailWindow OpenHistoryDetailWindow(
        string processName,
        string executablePath,
        string displayName,
        ApplicationCardViewModel? liveApplication)
    {
        var windowKey = liveApplication?.AliasKey ?? ApplicationAliasStore.CreateKey(executablePath, processName);
        var shouldOpenHistoryOnly = liveApplication is null;
        if (_openDetailWindows.TryGetValue(windowKey, out var existingWindow) &&
            existingWindow.DataContext is ApplicationDetailViewModel existingViewModel &&
            existingViewModel.IsHistoryOnlyMode != shouldOpenHistoryOnly)
        {
            existingWindow.Close();
        }

        if (_openDetailWindows.TryGetValue(windowKey, out existingWindow))
        {
            return existingWindow;
        }

        ApplicationDetailWindow detailWindow;
        if (liveApplication is not null)
        {
            detailWindow = new ApplicationDetailWindow(new ApplicationDetailViewModel(liveApplication, _detailDisplayPreferences, _databasePath));
        }
        else
        {
            var iconSourcePath = string.IsNullOrWhiteSpace(executablePath) ? null : _applicationIconCache.GetIconPath(executablePath);
            detailWindow = new ApplicationDetailWindow(
                new ApplicationDetailViewModel(processName, displayName, executablePath, iconSourcePath, _detailDisplayPreferences, _databasePath));
        }

        _openDetailWindows[windowKey] = detailWindow;
        detailWindow.Closed += (_, _) => _openDetailWindows.Remove(windowKey);
        detailWindow.Show();
        return detailWindow;
    }

    private string GetHistorySelectionDimensionKey() => _selectedHistoryDimension switch
    {
        HistoryDimension.Week => "week",
        HistoryDimension.Month => "month",
        HistoryDimension.Custom => "custom",
        _ => "day"
    };

    private void SetHistoryTopN(int value)
    {
        var normalized = Math.Clamp(value, 1, 10);
        if (_historyTopN == normalized)
        {
            return;
        }

        _historyTopN = normalized;
        RaisePropertyChanged(nameof(HistoryTopN));
        RaiseHistoryRankingChanged();
        if (_isMainWindowRenderingActive && IsHistoryPageActive)
        {
            LoadHistoryAnalysis();
        }
        else
        {
            _hasDeferredMainWindowRefresh = true;
        }
    }

    private static IReadOnlyList<HistoryDailyRecord> SelectHistoryRecords(
        IReadOnlyList<HistoryDailyRecord> dailyRecords,
        HistoryDimension dimension,
        DateOnly selectedDate,
        DateOnly displayedMonth,
        IReadOnlyCollection<DateOnly>? selectedCustomDays = null)
    {
        return dimension switch
        {
            HistoryDimension.Week => SelectWeekRecords(dailyRecords, selectedDate),
            HistoryDimension.Month => SelectMonthRecords(dailyRecords, displayedMonth),
            HistoryDimension.Custom => SelectCustomRecords(dailyRecords, selectedCustomDays ?? Array.Empty<DateOnly>()),
            _ => dailyRecords.Where(record => record.Day == selectedDate).OrderBy(record => record.Day).ToList()
        };
    }

    private static void SyncObservableCollection<T>(
        ObservableCollection<T> target,
        IReadOnlyList<T> source,
        Func<T, T, bool> equals)
    {
        var sharedCount = Math.Min(target.Count, source.Count);
        for (var index = 0; index < sharedCount; index++)
        {
            if (equals(target[index], source[index]))
            {
                continue;
            }

            target[index] = source[index];
        }

        while (target.Count > source.Count)
        {
            target.RemoveAt(target.Count - 1);
        }

        for (var index = target.Count; index < source.Count; index++)
        {
            target.Add(source[index]);
        }
    }

    private static IReadOnlyList<HistoryDailyRecord> SelectWeekRecords(IReadOnlyList<HistoryDailyRecord> dailyRecords, DateOnly selectedDate)
    {
        var weekStart = GetWeekStart(selectedDate);
        var weekEnd = weekStart.AddDays(6);
        return dailyRecords.Where(record => record.Day >= weekStart && record.Day <= weekEnd).OrderBy(record => record.Day).ToList();
    }

    private static IReadOnlyList<HistoryDailyRecord> SelectMonthRecords(IReadOnlyList<HistoryDailyRecord> dailyRecords, DateOnly displayedMonth)
    {
        var monthEnd = displayedMonth.AddMonths(1).AddDays(-1);
        return dailyRecords.Where(record => record.Day >= displayedMonth && record.Day <= monthEnd).OrderBy(record => record.Day).ToList();
    }

    private static IReadOnlyList<HistoryDailyRecord> SelectCustomRecords(
        IReadOnlyList<HistoryDailyRecord> dailyRecords,
        IReadOnlyCollection<DateOnly> selectedDays)
    {
        if (selectedDays.Count == 0)
        {
            return [];
        }

        return dailyRecords
            .Where(record => selectedDays.Contains(record.Day))
            .OrderBy(record => record.Day)
            .ToList();
    }

    private static HistoryResourceSummary BuildHistorySummary(
        IReadOnlyList<HistoryDailyRecord> selectedRecords,
        HistoryDimension dimension,
        DateOnly selectedDate,
        DateOnly displayedMonth,
        IReadOnlyCollection<DateOnly>? selectedCustomDays = null)
    {
        if (selectedRecords.Count == 0)
        {
            return HistoryResourceSummary.Empty;
        }

        var weekStart = GetWeekStart(selectedDate);
        var caption = dimension switch
        {
            HistoryDimension.Week => $"所选周：{weekStart:yyyy-MM-dd}-{weekStart.AddDays(6):yyyy-MM-dd}",
            HistoryDimension.Month => $"所选月：{displayedMonth:yyyy 年 MM 月}",
            HistoryDimension.Custom => $"所选区间：{BuildCustomRangeDisplay(selectedCustomDays ?? Array.Empty<DateOnly>())}",
            _ => $"所选日：{selectedDate:yyyy-MM-dd}"
        };
        var appDownloadBytes = selectedRecords.Sum(record => record.AppDownloadBytes);
        var appUploadBytes = selectedRecords.Sum(record => record.AppUploadBytes);
        var appIoReadBytes = selectedRecords.Sum(record => record.AppIoReadBytes);
        var appIoWriteBytes = selectedRecords.Sum(record => record.AppIoWriteBytes);
        var systemDownloadBytes = selectedRecords.Sum(record => record.SystemDownloadBytes);
        var systemUploadBytes = selectedRecords.Sum(record => record.SystemUploadBytes);
        var systemIoReadBytes = selectedRecords.Sum(record => record.SystemIoReadBytes);
        var systemIoWriteBytes = selectedRecords.Sum(record => record.SystemIoWriteBytes);
        var appPeakDownloadBytes = selectedRecords.Max(record => record.AppDownloadBytes);
        var appPeakUploadBytes = selectedRecords.Max(record => record.AppUploadBytes);
        var appPeakIoReadBytes = selectedRecords.Max(record => record.AppIoReadBytes);
        var appPeakIoWriteBytes = selectedRecords.Max(record => record.AppIoWriteBytes);
        var systemPeakDownloadBytes = selectedRecords.Max(record => record.SystemDownloadBytes);
        var systemPeakUploadBytes = selectedRecords.Max(record => record.SystemUploadBytes);
        var systemPeakIoReadBytes = selectedRecords.Max(record => record.SystemIoReadBytes);
        var systemPeakIoWriteBytes = selectedRecords.Max(record => record.SystemIoWriteBytes);
        return new HistoryResourceSummary(
            caption,
            appDownloadBytes,
            appUploadBytes,
            appPeakDownloadBytes,
            appPeakUploadBytes,
            appIoReadBytes,
            appIoWriteBytes,
            appPeakIoReadBytes,
            appPeakIoWriteBytes,
            systemDownloadBytes,
            systemUploadBytes,
            systemPeakDownloadBytes,
            systemPeakUploadBytes,
            systemIoReadBytes,
            systemIoWriteBytes,
            systemPeakIoReadBytes,
            systemPeakIoWriteBytes);
    }

    private static DateOnly GetWeekStart(DateOnly date)
    {
        var offset = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-offset);
    }

    private DateOnly ResolveDayHistorySelection(HistoryDimension previousDimension, DateOnly selectedDate, DateOnly displayedMonth)
    {
        var rangeStart = previousDimension switch
        {
            HistoryDimension.Week => GetWeekStart(selectedDate),
            HistoryDimension.Month => displayedMonth,
            _ => selectedDate
        };
        var rangeEnd = previousDimension switch
        {
            HistoryDimension.Week => GetWeekStart(selectedDate).AddDays(6),
            HistoryDimension.Month => displayedMonth.AddMonths(1).AddDays(-1),
            _ => selectedDate
        };

        var earliestDateWithData = ResolveEarliestHistoryDate(_historyCalendarRecords, rangeStart, rangeEnd);

        return earliestDateWithData ?? rangeStart;
    }

    private DateOnly ResolveWeekHistorySelection(HistoryDimension previousDimension, DateOnly selectedDate, DateOnly displayedMonth)
    {
        var rangeStart = previousDimension switch
        {
            HistoryDimension.Month => displayedMonth,
            _ => selectedDate
        };
        var rangeEnd = previousDimension switch
        {
            HistoryDimension.Month => displayedMonth.AddMonths(1).AddDays(-1),
            _ => selectedDate
        };

        var earliestDateWithData = ResolveEarliestHistoryDate(_historyCalendarRecords, rangeStart, rangeEnd);
        return GetWeekStart(earliestDateWithData ?? rangeStart);
    }

    private static DateOnly? ResolveEarliestHistoryDate(
        IReadOnlyList<HistoryDailyRecord> dailyRecords,
        DateOnly rangeStart,
        DateOnly rangeEnd)
    {
        foreach (var day in dailyRecords
                     .Select(static record => record.Day)
                     .Where(day => day >= rangeStart && day <= rangeEnd)
                     .OrderBy(static day => day))
        {
            return day;
        }

        return null;
    }

    private string BuildHistoryNetworkDisplay(long downloadBytes, long uploadBytes, long peakDownloadBytes, long peakUploadBytes)
    {
        return _historyNetworkDisplayMode == HistoryNetworkDisplayMode.Split
            ? $"下载 {FormatBytes(downloadBytes)}\n上传 {FormatBytes(uploadBytes)}"
            : $"总量 {FormatBytes(downloadBytes + uploadBytes)}";
    }

    private string BuildHistoryIoDisplay(long readBytes, long writeBytes, long peakReadBytes, long peakWriteBytes)
    {
        return _historyIoDisplayMode == HistoryIoDisplayMode.Split
            ? $"读取 {FormatBytes(readBytes)}\n写入 {FormatBytes(writeBytes)}"
            : $"总量 {FormatBytes(readBytes + writeBytes)}";
    }

    private ImageSource BuildHistoryPieChartImage(
        IReadOnlyList<HistoryOverviewApplicationAggregate> orderedRecords,
        Func<HistoryOverviewApplicationAggregate, long> valueSelector,
        Func<HistoryOverviewApplicationAggregate, string> valueFormatter)
    {
        const int width = 436;
        const int height = 332;
        const double dpi = 96d;
        var bitmap = new RenderTargetBitmap(width, height, dpi, dpi, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRoundedRectangle(new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFC, 0xF8, 0xF1)), null, new System.Windows.Rect(0, 0, width, height), 20, 20);

            var positiveRecords = orderedRecords
                .Select(record => new { Record = record, Value = Math.Max(0L, valueSelector(record)) })
                .Where(item => item.Value > 0)
                .ToList();

            if (positiveRecords.Count == 0)
            {
                DrawHistoryPieEmptyState(context, width, height);
            }
            else
            {
                var topItems = positiveRecords.Take(_historyTopN).ToList();
                var otherValue = positiveRecords.Skip(_historyTopN).Sum(static item => item.Value);
                var slices = topItems
                    .Select((item, index) => new HistoryPieSlice(
                        BuildHistoryApplicationDisplayName(item.Record.ProcessName, item.Record.ExecutablePath),
                        item.Value,
                        valueFormatter(item.Record),
                        GetHistoryPieBrush(index)))
                    .ToList();

                if (otherValue > 0)
                {
                    slices.Add(new HistoryPieSlice("其他", otherValue, FormatCompactMetric(otherValue), new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xD5, 0xDB, 0xD4))));
                }

                var total = slices.Sum(static item => item.Value);
                var center = new System.Windows.Point(130, 150);
                const double radius = 116;
                const double innerRadius = 60;
                var startAngle = -90d;

                foreach (var slice in slices)
                {
                    var sweepAngle = slice.Value / total * 360d;
                    context.DrawGeometry(slice.Brush, null, CreateDonutSliceGeometry(center, radius, innerRadius, startAngle, sweepAngle));
                    startAngle += sweepAngle;
                }

                context.DrawEllipse(new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xFD, 0xF9)), null, center, innerRadius - 1, innerRadius - 1);
                DrawHistoryPieText(context, "Top", 130, 130, 12, System.Windows.FontWeights.SemiBold, BrushFromRgb(0x7A, 0x82, 0x77), System.Windows.TextAlignment.Center);
                DrawHistoryPieText(context, _historyTopN.ToString(CultureInfo.InvariantCulture), 130, 145, 22, System.Windows.FontWeights.Bold, BrushFromRgb(0x21, 0x3B, 0x35), System.Windows.TextAlignment.Center);

                var legendCount = slices.Count;
                var legendStartTop = legendCount >= 10 ? 18d : 26d;
                var legendStep = legendCount >= 10 ? 28d : 30d;

                for (var i = 0; i < slices.Count; i++)
                {
                    var slice = slices[i];
                    var top = legendStartTop + (i * legendStep);
                    context.DrawRoundedRectangle(slice.Brush, null, new System.Windows.Rect(284, top, 12, 12), 3, 3);
                    var percentage = total == 0 ? 0d : slice.Value / total;
                    DrawHistoryPieText(context, TrimDisplayName(slice.Label, 11), 304, top - 3, 12, System.Windows.FontWeights.SemiBold, BrushFromRgb(0x21, 0x3B, 0x35), System.Windows.TextAlignment.Left);
                    DrawHistoryPieText(context, $"{percentage:P0} · {slice.MetricDisplay}", 304, top + 13, 11, System.Windows.FontWeights.Normal, BrushFromRgb(0x6E, 0x76, 0x6D), System.Windows.TextAlignment.Left);
                }
            }
        }

        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static void DrawHistoryPieEmptyState(DrawingContext context, int width, int height)
    {
        context.DrawEllipse(new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xEE, 0xE8, 0xDC)), null, new System.Windows.Point(130, 150), 116, 116);
        context.DrawEllipse(new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFC, 0xF8, 0xF1)), null, new System.Windows.Point(130, 150), 60, 60);
        DrawHistoryPieText(context, "暂无数据", width / 2d, height / 2d - 10, 16, System.Windows.FontWeights.SemiBold, BrushFromRgb(0x6E, 0x76, 0x6D), System.Windows.TextAlignment.Center);
        DrawHistoryPieText(context, "当前区间没有可用占比", width / 2d, height / 2d + 14, 12, System.Windows.FontWeights.Normal, BrushFromRgb(0x94, 0x9A, 0x91), System.Windows.TextAlignment.Center);
    }

    private static Geometry CreateDonutSliceGeometry(System.Windows.Point center, double outerRadius, double innerRadius, double startAngle, double sweepAngle)
    {
        if (sweepAngle >= 359.99d)
        {
            var geometryGroup = new GeometryGroup { FillRule = FillRule.EvenOdd };
            geometryGroup.Children.Add(new EllipseGeometry(center, outerRadius, outerRadius));
            geometryGroup.Children.Add(new EllipseGeometry(center, innerRadius, innerRadius));
            return geometryGroup;
        }

        var startOuter = PointOnCircle(center, outerRadius, startAngle);
        var endOuter = PointOnCircle(center, outerRadius, startAngle + sweepAngle);
        var startInner = PointOnCircle(center, innerRadius, startAngle);
        var endInner = PointOnCircle(center, innerRadius, startAngle + sweepAngle);
        var isLargeArc = sweepAngle > 180d;

        var figure = new PathFigure { StartPoint = startOuter, IsClosed = true, IsFilled = true };
        figure.Segments.Add(new ArcSegment(endOuter, new System.Windows.Size(outerRadius, outerRadius), 0, isLargeArc, SweepDirection.Clockwise, true));
        figure.Segments.Add(new LineSegment(endInner, true));
        figure.Segments.Add(new ArcSegment(startInner, new System.Windows.Size(innerRadius, innerRadius), 0, isLargeArc, SweepDirection.Counterclockwise, true));

        return new PathGeometry([figure]);
    }

    private static System.Windows.Point PointOnCircle(System.Windows.Point center, double radius, double angleDegrees)
    {
        var angleRadians = angleDegrees * Math.PI / 180d;
        return new System.Windows.Point(
            center.X + radius * Math.Cos(angleRadians),
            center.Y + radius * Math.Sin(angleRadians));
    }

    private static System.Windows.Media.Brush GetHistoryPieBrush(int index)
    {
        return index switch
        {
            0 => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2D, 0x5E, 0x46)),
            1 => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC6, 0x8A, 0x3D)),
            2 => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x5D, 0x83, 0xA7)),
            3 => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xA6, 0x68, 0x4B)),
            4 => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x7D, 0x90, 0x80)),
            5 => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x8C, 0x6B, 0xB0)),
            6 => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4E, 0x8B, 0x8B)),
            7 => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC0, 0x5A, 0x6D)),
            8 => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x9A, 0x7A, 0x39)),
            9 => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x5B, 0x6F, 0xD6)),
            _ => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x6D, 0x7B, 0x75))
        };
    }

    private static System.Windows.Media.Brush BrushFromRgb(byte r, byte g, byte b) => new SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));

    private static void DrawHistoryPieText(
        DrawingContext context,
        string text,
        double x,
        double y,
        double fontSize,
        System.Windows.FontWeight fontWeight,
        System.Windows.Media.Brush brush,
        System.Windows.TextAlignment alignment)
    {
        var formattedText = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            System.Windows.FlowDirection.LeftToRight,
            new Typeface(new System.Windows.Media.FontFamily("Microsoft YaHei"), System.Windows.FontStyles.Normal, fontWeight, System.Windows.FontStretches.Normal),
            fontSize,
            brush,
            1.25)
        {
            TextAlignment = alignment
        };

        context.DrawText(formattedText, new System.Windows.Point(x, y));
    }

    private static string TrimDisplayName(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length <= maxLength)
        {
            return value;
        }

        return $"{value[..(maxLength - 1)]}…";
    }

    private static string FormatCompactMetric(long value)
    {
        const long mega = 1024L * 1024L;
        const long giga = mega * 1024L;
        if (value >= giga)
        {
            return $"{value / (double)giga:0.#} GB";
        }

        if (value >= mega)
        {
            return $"{value / (double)mega:0.#} MB";
        }

        if (value >= 1024)
        {
            return $"{value / 1024d:0.#} KB";
        }

        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static string FormatDuration(long milliseconds)
    {
        var totalMinutes = Math.Max(0, (long)Math.Round(milliseconds / 60000d, MidpointRounding.AwayFromZero));
        var days = totalMinutes / (24 * 60);
        var hours = (totalMinutes % (24 * 60)) / 60;
        var minutes = totalMinutes % 60;

        if (days > 0)
        {
            return $"{days} 天 {hours} 小时";
        }

        if (hours > 0)
        {
            return $"{hours} 小时 {minutes} 分钟";
        }

        return $"{minutes} 分钟";
    }

    private void OnHistoryCalendarDateInvoked(DateOnly date)
    {
        if (_selectedHistoryDimension == HistoryDimension.Custom)
        {
            SetHistoryCustomDateSelection(date, !_historyCustomSelectedDates.Contains(date));
            return;
        }

        SelectHistoryDate(date);
    }

    private void SeedCustomSelectionFromCurrentDimension(HistoryDimension previousDimension)
    {
        _historyCustomSelectedDates.Clear();
        var daysWithData = _historyCalendarRecords
            .Select(static record => record.Day)
            .ToHashSet();

        switch (previousDimension)
        {
            case HistoryDimension.Week:
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
            case HistoryDimension.Month:
            {
                var monthEnd = _historyDisplayedMonth.AddMonths(1).AddDays(-1);
                for (var date = _historyDisplayedMonth; date <= monthEnd; date = date.AddDays(1))
                {
                    if (daysWithData.Contains(date))
                    {
                        _historyCustomSelectedDates.Add(date);
                    }
                }

                break;
            }
            case HistoryDimension.Custom:
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

    private string BuildHistoryCustomRangeDisplay() => BuildCustomRangeDisplay(GetCustomSelectedDays());

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
        var rangeStart = ordered[0];
        var rangeEnd = ordered[0];

        for (var index = 1; index < ordered.Length; index++)
        {
            var date = ordered[index];
            if (date == rangeEnd.AddDays(1))
            {
                rangeEnd = date;
                continue;
            }

            ranges.Add((rangeStart, rangeEnd));
            rangeStart = date;
            rangeEnd = date;
        }

        ranges.Add((rangeStart, rangeEnd));
        return ranges;
    }

    private void RaiseHistoryStateChanged()
    {
        RaisePropertyChanged(nameof(IsRealtimePageActive));
        RaisePropertyChanged(nameof(IsHistoryPageActive));
        RaisePropertyChanged(nameof(IsHistoryDayDimension));
        RaisePropertyChanged(nameof(IsHistoryWeekDimension));
        RaisePropertyChanged(nameof(IsHistoryMonthDimension));
        RaisePropertyChanged(nameof(IsHistoryCustomDimension));
        RaisePropertyChanged(nameof(HistoryDimensionTitle));
        RaisePropertyChanged(nameof(HistoryDimensionHeadline));
        RaisePropertyChanged(nameof(HistoryCalendarMonthDisplay));
        RaisePropertyChanged(nameof(HistoryCalendarSelectionDisplay));
    }

    private void RaiseHistorySummaryChanged()
    {
        RaisePropertyChanged(nameof(HistorySummaryCaption));
        RaisePropertyChanged(nameof(IsHistoryNetworkTotalMode));
        RaisePropertyChanged(nameof(IsHistoryNetworkSplitMode));
        RaisePropertyChanged(nameof(IsHistoryIoTotalMode));
        RaisePropertyChanged(nameof(IsHistoryIoSplitMode));
        RaisePropertyChanged(nameof(HistoryApplicationNetworkDisplay));
        RaisePropertyChanged(nameof(HistoryApplicationIoDisplay));
        RaisePropertyChanged(nameof(HistorySystemNetworkDisplay));
        RaisePropertyChanged(nameof(HistorySystemIoDisplay));
        RaisePropertyChanged(nameof(HistoryTrafficPieChartSource));
        RaisePropertyChanged(nameof(HistoryIoPieChartSource));
        RaisePropertyChanged(nameof(HistoryForegroundPieChartSource));
    }

    private void RaiseHistoryRankingChanged()
    {
        RaisePropertyChanged(nameof(HistoryTopN));
        RaisePropertyChanged(nameof(HistoryTrafficTopTitle));
        RaisePropertyChanged(nameof(HistoryIoTopTitle));
        RaisePropertyChanged(nameof(HistoryForegroundTopTitle));
        RaisePropertyChanged(nameof(HistoryTrafficPieChartSource));
        RaisePropertyChanged(nameof(HistoryIoPieChartSource));
        RaisePropertyChanged(nameof(HistoryForegroundPieChartSource));
    }
}

internal readonly record struct HistoryPieSlice(string Label, double Value, string MetricDisplay, System.Windows.Media.Brush Brush);

public sealed class HistoryCalendarDayViewModel
{
    public HistoryCalendarDayViewModel(
        DateOnly date,
        bool isInDisplayedMonth,
        bool hasData,
        bool isSelected,
        bool isRangeStart,
        bool isRangeEnd,
        bool isSelectable,
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
        IsSelectable = isSelectable;
        SelectCommand = new RelayCommand(() => onSelect(date), () => isSelectable);
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
    public bool IsSelectable { get; }
    public ICommand SelectCommand { get; }
}

public sealed class HistoryRankingItemViewModel
{
    public HistoryRankingItemViewModel(
        string rank,
        string applicationName,
        string metricDisplay,
        string? iconSourcePath,
        string processName = "",
        string executablePath = "",
        ICommand? openDetailsCommand = null)
    {
        Rank = rank;
        ApplicationName = applicationName;
        MetricDisplay = metricDisplay;
        IconSourcePath = iconSourcePath;
        ProcessName = processName;
        ExecutablePath = executablePath;
        OpenDetailsCommand = openDetailsCommand;
    }

    public string Rank { get; }
    public string ApplicationName { get; }
    public string MetricDisplay { get; }
    public string? IconSourcePath { get; }
    public string ProcessName { get; }
    public string ExecutablePath { get; }
    public ICommand? OpenDetailsCommand { get; }
    public bool IsNavigable => OpenDetailsCommand is not null;
}

internal readonly record struct HistoryResourceSummary(
    string Caption,
    long AppDownloadBytes,
    long AppUploadBytes,
    long AppPeakDownloadBytes,
    long AppPeakUploadBytes,
    long AppIoReadBytes,
    long AppIoWriteBytes,
    long AppPeakIoReadBytes,
    long AppPeakIoWriteBytes,
    long SystemDownloadBytes,
    long SystemUploadBytes,
    long SystemPeakDownloadBytes,
    long SystemPeakUploadBytes,
    long SystemIoReadBytes,
    long SystemIoWriteBytes,
    long SystemPeakIoReadBytes,
    long SystemPeakIoWriteBytes)
{
    public static HistoryResourceSummary Empty => new(
        "当前暂无可分析历史",
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0);
}

internal readonly record struct HistoryOverviewSummary(
    string Caption,
    string TotalUsageDisplay,
    string AverageUsageDisplay,
    string PeakNetworkDisplay,
    string PeakIoDisplay)
{
    public static HistoryOverviewSummary Empty => new(
        "当前暂无可分析历史",
        "鎬讳娇鐢?0 鍒嗛挓",
        "骞冲潎浣跨敤 0 鍒嗛挓",
        "缃戠粶宄板€?鏆傛棤",
        "I/O 宄板€?鏆傛棤");
}

