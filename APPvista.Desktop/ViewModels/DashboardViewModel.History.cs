using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using APPvista.Desktop.Services;

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
        Month
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
    private HistoryNetworkDisplayMode _historyNetworkDisplayMode = HistoryNetworkDisplayMode.Total;
    private HistoryIoDisplayMode _historyIoDisplayMode = HistoryIoDisplayMode.Total;
    private int _historyAverageApplicationCount;
    private int _historyTopN = 3;
    private IReadOnlyList<HistoryDailyRecord> _historyCalendarRecords = [];
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
    public ICommand ShowPreviousHistoryMonthCommand { get; }
    public ICommand ShowNextHistoryMonthCommand { get; }
    public ICommand SetHistoryNetworkTotalDisplayCommand { get; }
    public ICommand SetHistoryNetworkSplitDisplayCommand { get; }
    public ICommand SetHistoryIoTotalDisplayCommand { get; }
    public ICommand SetHistoryIoSplitDisplayCommand { get; }

    public bool IsRealtimePageActive => _selectedDashboardPage == DashboardPage.Realtime;
    public bool IsHistoryPageActive => _selectedDashboardPage == DashboardPage.History;
    public bool IsHistoryDayDimension => _selectedHistoryDimension == HistoryDimension.Day;
    public bool IsHistoryWeekDimension => _selectedHistoryDimension == HistoryDimension.Week;
    public bool IsHistoryMonthDimension => _selectedHistoryDimension == HistoryDimension.Month;
    public string HistoryDimensionTitle => _selectedHistoryDimension switch
    {
        HistoryDimension.Week => "按周统计",
        HistoryDimension.Month => "按月统计",
        _ => "按日统计"
    };
    public string HistoryDimensionHeadline => $"{HistoryDimensionTitle} · 平均活跃应用 {_historyAverageApplicationCount}";

    public string HistorySummaryCaption => _historySummary.Caption;
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

    public string HistoryCalendarSelectionDisplay => _selectedHistoryDimension switch
    {
        HistoryDimension.Week => $"已选周：{_historySelectedDate.AddDays(-(int)_historySelectedDate.DayOfWeek):yyyy-MM-dd} 起",
        HistoryDimension.Month => $"已选月：{_historyDisplayedMonth:yyyy 年 MM 月}",
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

    private void SetHistoryDimension(HistoryDimension dimension)
    {
        if (_selectedHistoryDimension == dimension)
        {
            return;
        }

        _selectedHistoryDimension = dimension;
        if (dimension == HistoryDimension.Month)
        {
            _historySelectedDate = _historyDisplayedMonth;
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
        var (rangeStart, rangeEnd) = ResolveHistoryRange(selectedDimension, selectedDate, displayedMonth);

        var loaded = await Task.Run(() => new
        {
            DailyRecords = _historyAnalysisProvider.LoadDailyRecords(maxDays: 120),
            ApplicationRecords = _historyAnalysisProvider.LoadApplicationAggregates(rangeStart, rangeEnd)
        });

        if (version != _historyAnalysisLoadVersion)
        {
            return;
        }

        var dailyRecords = MergeLiveTodayRecord(loaded.DailyRecords);
        _historyCalendarRecords = dailyRecords;
        RefreshHistoryCalendar();
        var selectedRecords = SelectHistoryRecords(dailyRecords, selectedDimension, selectedDate, displayedMonth);
        var applicationRecords = MergeLiveTodayApplicationAggregates(
            loaded.ApplicationRecords,
            rangeStart,
            rangeEnd);

        _historySummary = BuildHistorySummary(selectedRecords, selectedDimension, selectedDate, displayedMonth);
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
        var mergedRecord = baselineRecord with
        {
            Day = today,
            ApplicationCount = Snapshot.TopProcesses
                .Select(process => process.ProcessName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count(),
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
        var firstVisible = firstOfMonth.AddDays(-(int)firstOfMonth.DayOfWeek);
        var selectedWeekStart = _historySelectedDate.AddDays(-(int)_historySelectedDate.DayOfWeek);
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
                _ => date == _historySelectedDate
            };

            HistoryCalendarDays.Add(new HistoryCalendarDayViewModel(
                date,
                isInDisplayedMonth: date.Month == _historyDisplayedMonth.Month && date.Year == _historyDisplayedMonth.Year,
                hasData: daysWithData.Contains(date),
                isSelected: isSelected,
                isRangeStart: isSelected && (_selectedHistoryDimension == HistoryDimension.Day || date == selectedWeekStart || date == _historyDisplayedMonth),
                isRangeEnd: isSelected && (_selectedHistoryDimension == HistoryDimension.Day || date == selectedWeekEnd || date == monthEnd),
                isSelectable: _selectedHistoryDimension != HistoryDimension.Month && date.Month == _historyDisplayedMonth.Month && date.Year == _historyDisplayedMonth.Year,
                onSelect: SelectHistoryDate));
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
                selectedDate.AddDays(-(int)selectedDate.DayOfWeek),
                selectedDate.AddDays(-(int)selectedDate.DayOfWeek).AddDays(6)
            ),
            HistoryDimension.Month =>
            (
                displayedMonth,
                displayedMonth.AddMonths(1).AddDays(-1)
            ),
            _ => (selectedDate, selectedDate)
        };
    }

    private IReadOnlyList<HistoryApplicationAggregate> MergeLiveTodayApplicationAggregates(
        IReadOnlyList<HistoryApplicationAggregate> applicationRecords,
        DateOnly rangeStart,
        DateOnly rangeEnd)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (today < rangeStart || today > rangeEnd)
        {
            return applicationRecords;
        }

        var merged = applicationRecords.ToDictionary(
            static record => record.ProcessName,
            StringComparer.OrdinalIgnoreCase);

        foreach (var process in Snapshot.TopProcesses
                     .Where(static process => !string.IsNullOrWhiteSpace(process.ProcessName))
                     .GroupBy(static process => process.ProcessName, StringComparer.OrdinalIgnoreCase)
                     .Select(static group => new HistoryApplicationAggregate
                     {
                         ProcessName = group.First().ProcessName,
                         ExecutablePath = group.Select(static item => item.ExecutablePath).FirstOrDefault(static path => !string.IsNullOrWhiteSpace(path)) ?? string.Empty,
                         ForegroundMilliseconds = group.Sum(static item => item.DailyForegroundMilliseconds),
                         BackgroundMilliseconds = group.Sum(static item => item.DailyBackgroundMilliseconds),
                         DownloadBytes = group.Sum(static item => item.DailyDownloadBytes),
                         UploadBytes = group.Sum(static item => item.DailyUploadBytes),
                         IoReadBytes = group.Sum(static item => item.DailyIoReadBytes),
                         IoWriteBytes = group.Sum(static item => item.DailyIoWriteBytes)
                     }))
        {
            merged[process.ProcessName] = process;
        }

        return merged.Values.ToList();
    }

    private void RefreshHistoryTopApplications(IReadOnlyList<HistoryApplicationAggregate> applicationRecords)
    {
        UpdateHistoryRankingCollection(
            HistoryTrafficTopApplications,
            applicationRecords
                .OrderByDescending(static record => record.TotalTrafficBytes)
                .ThenByDescending(static record => record.ForegroundMilliseconds)
                .ThenBy(static record => record.ProcessName, StringComparer.OrdinalIgnoreCase)
                .Take(_historyTopN)
                .Select((record, index) => CreateHistoryRankingItem(index + 1, record, FormatBytes(record.TotalTrafficBytes)))
                .ToList(),
            "暂无流量数据");

        UpdateHistoryRankingCollection(
            HistoryIoTopApplications,
            applicationRecords
                .OrderByDescending(static record => record.TotalIoBytes)
                .ThenByDescending(static record => record.ForegroundMilliseconds)
                .ThenBy(static record => record.ProcessName, StringComparer.OrdinalIgnoreCase)
                .Take(_historyTopN)
                .Select((record, index) => CreateHistoryRankingItem(index + 1, record, FormatBytes(record.TotalIoBytes)))
                .ToList(),
            "暂无 I/O 数据");

        UpdateHistoryRankingCollection(
            HistoryForegroundTopApplications,
            applicationRecords
                .OrderByDescending(static record => record.ForegroundMilliseconds)
                .ThenByDescending(static record => record.TotalUsageMilliseconds)
                .ThenBy(static record => record.ProcessName, StringComparer.OrdinalIgnoreCase)
                .Take(_historyTopN)
                .Select((record, index) => CreateHistoryRankingItem(index + 1, record, FormatDuration(record.ForegroundMilliseconds)))
                .ToList(),
            "暂无前台时长数据");
    }

    private HistoryRankingItemViewModel CreateHistoryRankingItem(int rank, HistoryApplicationAggregate record, string metricDisplay)
    {
        return new HistoryRankingItemViewModel(
            rank.ToString(CultureInfo.InvariantCulture),
            BuildHistoryApplicationDisplayName(record.ProcessName, record.ExecutablePath),
            metricDisplay,
            string.IsNullOrWhiteSpace(record.ExecutablePath) ? null : _applicationIconCache.GetIconPath(record.ExecutablePath));
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
        target.Clear();

        if (items.Count == 0)
        {
            target.Add(new HistoryRankingItemViewModel("-", "暂无数据", emptyMetricDisplay, null));
            return;
        }

        foreach (var item in items)
        {
            target.Add(item);
        }
    }

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
        DateOnly displayedMonth)
    {
        return dimension switch
        {
            HistoryDimension.Week => SelectWeekRecords(dailyRecords, selectedDate),
            HistoryDimension.Month => SelectMonthRecords(dailyRecords, displayedMonth),
            _ => dailyRecords.Where(record => record.Day == selectedDate).OrderBy(record => record.Day).ToList()
        };
    }

    private static IReadOnlyList<HistoryDailyRecord> SelectWeekRecords(IReadOnlyList<HistoryDailyRecord> dailyRecords, DateOnly selectedDate)
    {
        var weekStart = selectedDate.AddDays(-(int)selectedDate.DayOfWeek);
        var weekEnd = weekStart.AddDays(6);
        return dailyRecords.Where(record => record.Day >= weekStart && record.Day <= weekEnd).OrderBy(record => record.Day).ToList();
    }

    private static IReadOnlyList<HistoryDailyRecord> SelectMonthRecords(IReadOnlyList<HistoryDailyRecord> dailyRecords, DateOnly displayedMonth)
    {
        var monthEnd = displayedMonth.AddMonths(1).AddDays(-1);
        return dailyRecords.Where(record => record.Day >= displayedMonth && record.Day <= monthEnd).OrderBy(record => record.Day).ToList();
    }

    private static HistoryResourceSummary BuildHistorySummary(
        IReadOnlyList<HistoryDailyRecord> selectedRecords,
        HistoryDimension dimension,
        DateOnly selectedDate,
        DateOnly displayedMonth)
    {
        if (selectedRecords.Count == 0)
        {
            return HistoryResourceSummary.Empty;
        }

        var weekStart = selectedDate.AddDays(-(int)selectedDate.DayOfWeek);
        var caption = dimension switch
        {
            HistoryDimension.Week => $"所选周：{weekStart:yyyy-MM-dd} 起",
            HistoryDimension.Month => $"所选月：{displayedMonth:yyyy 年 MM 月}",
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

    private string BuildHistoryNetworkDisplay(long downloadBytes, long uploadBytes, long peakDownloadBytes, long peakUploadBytes)
    {
        return _historyNetworkDisplayMode == HistoryNetworkDisplayMode.Split
            ? $"下行 {FormatBytes(downloadBytes)}\n上行 {FormatBytes(uploadBytes)}"
            : $"总量 {FormatBytes(downloadBytes + uploadBytes)}";
    }

    private string BuildHistoryIoDisplay(long readBytes, long writeBytes, long peakReadBytes, long peakWriteBytes)
    {
        return _historyIoDisplayMode == HistoryIoDisplayMode.Split
            ? $"读取 {FormatBytes(readBytes)}\n写入 {FormatBytes(writeBytes)}"
            : $"总量 {FormatBytes(readBytes + writeBytes)}";
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

    private void RaiseHistoryStateChanged()
    {
        RaisePropertyChanged(nameof(IsRealtimePageActive));
        RaisePropertyChanged(nameof(IsHistoryPageActive));
        RaisePropertyChanged(nameof(IsHistoryDayDimension));
        RaisePropertyChanged(nameof(IsHistoryWeekDimension));
        RaisePropertyChanged(nameof(IsHistoryMonthDimension));
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
    }

    private void RaiseHistoryRankingChanged()
    {
        RaisePropertyChanged(nameof(HistoryTopN));
        RaisePropertyChanged(nameof(HistoryTrafficTopTitle));
        RaisePropertyChanged(nameof(HistoryIoTopTitle));
        RaisePropertyChanged(nameof(HistoryForegroundTopTitle));
    }
}

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
    public HistoryRankingItemViewModel(string rank, string applicationName, string metricDisplay, string? iconSourcePath)
    {
        Rank = rank;
        ApplicationName = applicationName;
        MetricDisplay = metricDisplay;
        IconSourcePath = iconSourcePath;
    }

    public string Rank { get; }
    public string ApplicationName { get; }
    public string MetricDisplay { get; }
    public string? IconSourcePath { get; }
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

