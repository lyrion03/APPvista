using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows.Input;
using APPvista.Desktop.Services;

namespace APPvista.Desktop.ViewModels;

public sealed class HistoryComparisonViewModel : ObservableObject
{
    private readonly ApplicationIconCache _applicationIconCache;
    private readonly IReadOnlyDictionary<string, string> _applicationAliases;
    private bool _isMetricSelectorOpen;
    private string _windowTitle = "详细对比";
    private string _rangeDisplay = string.Empty;

    public HistoryComparisonViewModel(
        ApplicationIconCache applicationIconCache,
        IReadOnlyDictionary<string, string> applicationAliases,
        IReadOnlyList<HistoryApplicationAggregate> applicationAggregates,
        string windowTitle,
        string rangeDisplay)
    {
        _applicationIconCache = applicationIconCache;
        _applicationAliases = applicationAliases;
        AvailableApplications = new ObservableCollection<HistoryComparisonSelectableApplicationViewModel>();
        VisibleMetrics = new ObservableCollection<HistoryComparisonMetricOptionViewModel>();
        ComparisonRows = new ObservableCollection<HistoryComparisonApplicationRowViewModel>();

        ToggleMetricSelectorCommand = new RelayCommand(() => IsMetricSelectorOpen = !IsMetricSelectorOpen);
        CloseMetricSelectorCommand = new RelayCommand(() => IsMetricSelectorOpen = false);

        InitializeMetricOptions();
        Load(windowTitle, rangeDisplay, applicationAggregates);
    }

    public ObservableCollection<HistoryComparisonSelectableApplicationViewModel> AvailableApplications { get; }
    public ObservableCollection<HistoryComparisonMetricOptionViewModel> VisibleMetrics { get; }
    public ObservableCollection<HistoryComparisonApplicationRowViewModel> ComparisonRows { get; }

    public ICommand ToggleMetricSelectorCommand { get; }
    public ICommand CloseMetricSelectorCommand { get; }

    public string WindowTitle
    {
        get => _windowTitle;
        private set => SetProperty(ref _windowTitle, value);
    }

    public string RangeDisplay
    {
        get => _rangeDisplay;
        private set => SetProperty(ref _rangeDisplay, value);
    }

    public bool IsMetricSelectorOpen
    {
        get => _isMetricSelectorOpen;
        set => SetProperty(ref _isMetricSelectorOpen, value);
    }

    public bool HasSelectedApplications => ComparisonRows.Count > 0;
    public string SelectedApplicationsSummary =>
        $"已选择 {ComparisonRows.Count} / {AvailableApplications.Count} 个应用";

    public void Load(string windowTitle, string rangeDisplay, IReadOnlyList<HistoryApplicationAggregate> applicationAggregates)
    {
        WindowTitle = windowTitle;
        RangeDisplay = rangeDisplay;

        foreach (var item in AvailableApplications)
        {
            item.PropertyChanged -= OnApplicationSelectionChanged;
        }

        AvailableApplications.Clear();
        ComparisonRows.Clear();

        foreach (var item in applicationAggregates
                     .Where(static item => !string.IsNullOrWhiteSpace(item.ProcessName))
                     .Select(CreateSelectableApplication)
                     .OrderBy(static item => item.SortKey, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(static item => item.ProcessName, StringComparer.OrdinalIgnoreCase))
        {
            item.PropertyChanged += OnApplicationSelectionChanged;
            AvailableApplications.Add(item);
        }

        RefreshComparisonRows();
        RaisePropertyChanged(nameof(SelectedApplicationsSummary));
    }

    private void InitializeMetricOptions()
    {
        AddMetricOption(HistoryComparisonMetric.ActiveDays, "活跃天数", isSelectedByDefault: true);
        AddMetricOption(HistoryComparisonMetric.ForegroundDuration, "前台时长", isSelectedByDefault: true);
        AddMetricOption(HistoryComparisonMetric.BackgroundDuration, "后台时长", isSelectedByDefault: false);
        AddMetricOption(HistoryComparisonMetric.ForegroundRatio, "前台占比", isSelectedByDefault: true);
        AddMetricOption(HistoryComparisonMetric.AverageWorkingSet, "平均工作集", isSelectedByDefault: true);
        AddMetricOption(HistoryComparisonMetric.AverageCpu, "平均 CPU", isSelectedByDefault: true);
        AddMetricOption(HistoryComparisonMetric.AverageThreadCount, "平均线程", isSelectedByDefault: false);
        AddMetricOption(HistoryComparisonMetric.PeakWorkingSet, "工作集峰值", isSelectedByDefault: false);
        AddMetricOption(HistoryComparisonMetric.PeakThreadCount, "线程峰值", isSelectedByDefault: false);
        AddMetricOption(HistoryComparisonMetric.ThreadPeakMeanRatio, "线程峰均比", isSelectedByDefault: false);
        AddMetricOption(HistoryComparisonMetric.TotalTraffic, "总流量", isSelectedByDefault: true);
        AddMetricOption(HistoryComparisonMetric.PeakTraffic, "网络峰值", isSelectedByDefault: false);
        AddMetricOption(HistoryComparisonMetric.TotalIo, "I/O 总量", isSelectedByDefault: true);
        AddMetricOption(HistoryComparisonMetric.PeakIo, "I/O 峰值", isSelectedByDefault: false);
        AddMetricOption(HistoryComparisonMetric.AverageIops, "平均 IOPS", isSelectedByDefault: false);
    }

    private void AddMetricOption(HistoryComparisonMetric metric, string displayName, bool isSelectedByDefault)
    {
        var option = new HistoryComparisonMetricOptionViewModel(metric, displayName, isSelectedByDefault);
        option.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(HistoryComparisonMetricOptionViewModel.IsSelected))
            {
                RefreshComparisonRows();
            }
        };
        VisibleMetrics.Add(option);
    }

    private HistoryComparisonSelectableApplicationViewModel CreateSelectableApplication(HistoryApplicationAggregate aggregate)
    {
        var displayName = BuildDisplayName(aggregate.ProcessName, aggregate.ExecutablePath);
        return new HistoryComparisonSelectableApplicationViewModel(
            aggregate,
            displayName,
            aggregate.ProcessName,
            string.IsNullOrWhiteSpace(aggregate.ExecutablePath) ? null : _applicationIconCache.GetIconPath(aggregate.ExecutablePath));
    }

    private void OnApplicationSelectionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(HistoryComparisonSelectableApplicationViewModel.IsSelected))
        {
            return;
        }

        RefreshComparisonRows();
    }

    private void RefreshComparisonRows()
    {
        var selectedMetrics = VisibleMetrics
            .Where(static item => item.IsSelected)
            .Select(static item => item.Metric)
            .ToArray();

        var rows = AvailableApplications
            .Where(static item => item.IsSelected)
            .OrderBy(static item => item.SortKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.ProcessName, StringComparer.OrdinalIgnoreCase)
            .Select(item => new HistoryComparisonApplicationRowViewModel(
                item.DisplayName,
                item.ProcessName,
                item.IconSourcePath,
                BuildMetricItems(item.Aggregate, selectedMetrics)))
            .ToList();

        SyncObservableCollection(
            ComparisonRows,
            rows,
            static (left, right) =>
                left.DisplayName == right.DisplayName &&
                left.ProcessName == right.ProcessName &&
                left.IconSourcePath == right.IconSourcePath &&
                MetricsEqual(left.VisibleMetrics, right.VisibleMetrics));

        RaisePropertyChanged(nameof(HasSelectedApplications));
        RaisePropertyChanged(nameof(SelectedApplicationsSummary));
    }

    private IReadOnlyList<HistoryComparisonMetricDisplayItem> BuildMetricItems(
        HistoryApplicationAggregate aggregate,
        IReadOnlyList<HistoryComparisonMetric> selectedMetrics)
    {
        var items = new List<HistoryComparisonMetricDisplayItem>(selectedMetrics.Count);
        foreach (var metric in selectedMetrics)
        {
            items.Add(metric switch
            {
                HistoryComparisonMetric.ActiveDays => new HistoryComparisonMetricDisplayItem("活跃天数", $"{aggregate.ActiveDays} 天"),
                HistoryComparisonMetric.ForegroundDuration => new HistoryComparisonMetricDisplayItem("前台时长", FormatDuration(aggregate.ForegroundMilliseconds)),
                HistoryComparisonMetric.BackgroundDuration => new HistoryComparisonMetricDisplayItem("后台时长", FormatDuration(aggregate.BackgroundMilliseconds)),
                HistoryComparisonMetric.ForegroundRatio => new HistoryComparisonMetricDisplayItem("前台占比", $"{aggregate.ForegroundRatio * 100d:F1}%"),
                HistoryComparisonMetric.AverageWorkingSet => new HistoryComparisonMetricDisplayItem("平均工作集", FormatBytes(aggregate.AverageWorkingSetBytes)),
                HistoryComparisonMetric.AverageCpu => new HistoryComparisonMetricDisplayItem("平均 CPU", $"{aggregate.AverageCpu:F1}%"),
                HistoryComparisonMetric.AverageThreadCount => new HistoryComparisonMetricDisplayItem("平均线程", aggregate.AverageThreadCount.ToString("F1", CultureInfo.InvariantCulture)),
                HistoryComparisonMetric.PeakWorkingSet => new HistoryComparisonMetricDisplayItem("工作集峰值", FormatBytes(aggregate.PeakWorkingSetBytes)),
                HistoryComparisonMetric.PeakThreadCount => new HistoryComparisonMetricDisplayItem("线程峰值", aggregate.PeakThreadCount.ToString(CultureInfo.InvariantCulture)),
                HistoryComparisonMetric.ThreadPeakMeanRatio => new HistoryComparisonMetricDisplayItem("线程峰均比", aggregate.AverageThreadCount > 0 ? $"{aggregate.ThreadPeakMeanRatio:F2}x" : "-"),
                HistoryComparisonMetric.TotalTraffic => new HistoryComparisonMetricDisplayItem("总流量", FormatBytes(aggregate.TotalTrafficBytes)),
                HistoryComparisonMetric.PeakTraffic => new HistoryComparisonMetricDisplayItem("网络峰值", FormatBytesPerSecond(aggregate.PeakTrafficBytesPerSecond)),
                HistoryComparisonMetric.TotalIo => new HistoryComparisonMetricDisplayItem("I/O 总量", FormatBytes(aggregate.TotalIoBytes)),
                HistoryComparisonMetric.PeakIo => new HistoryComparisonMetricDisplayItem("I/O 峰值", FormatBytesPerSecond(aggregate.PeakIoBytesPerSecond)),
                HistoryComparisonMetric.AverageIops => new HistoryComparisonMetricDisplayItem("平均 IOPS", aggregate.AverageIops.ToString("F1", CultureInfo.InvariantCulture)),
                _ => new HistoryComparisonMetricDisplayItem(string.Empty, string.Empty)
            });
        }

        return items;
    }

    private string BuildDisplayName(string processName, string executablePath)
    {
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

    private static bool MetricsEqual(
        IReadOnlyList<HistoryComparisonMetricDisplayItem> left,
        IReadOnlyList<HistoryComparisonMetricDisplayItem> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (left[index].Label != right[index].Label || left[index].Value != right[index].Value)
            {
                return false;
            }
        }

        return true;
    }

    private static void SyncObservableCollection<T>(
        ObservableCollection<T> target,
        IReadOnlyList<T> source,
        Func<T, T, bool> equals)
    {
        var sharedCount = Math.Min(target.Count, source.Count);
        for (var index = 0; index < sharedCount; index++)
        {
            if (!equals(target[index], source[index]))
            {
                target[index] = source[index];
            }
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

    private static string FormatDuration(long milliseconds)
    {
        if (milliseconds <= 0)
        {
            return "00:00:00";
        }

        return TimeSpan.FromMilliseconds(milliseconds).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
    }

    private static string FormatBytesPerSecond(long bytesPerSecond) => $"{FormatBytes(bytesPerSecond)}/s";

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

    public sealed class HistoryComparisonSelectableApplicationViewModel : ObservableObject
    {
        private bool _isSelected;

        public HistoryComparisonSelectableApplicationViewModel(
            HistoryApplicationAggregate aggregate,
            string displayName,
            string processName,
            string? iconSourcePath)
        {
            Aggregate = aggregate;
            DisplayName = displayName;
            ProcessName = processName;
            IconSourcePath = iconSourcePath;
            SortKey = displayName;
        }

        public HistoryApplicationAggregate Aggregate { get; }
        public string DisplayName { get; }
        public string ProcessName { get; }
        public string SortKey { get; }
        public string? IconSourcePath { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }
    }

    public sealed class HistoryComparisonMetricOptionViewModel : ObservableObject
    {
        private bool _isSelected;

        public HistoryComparisonMetricOptionViewModel(HistoryComparisonMetric metric, string displayName, bool isSelected)
        {
            Metric = metric;
            DisplayName = displayName;
            _isSelected = isSelected;
        }

        public HistoryComparisonMetric Metric { get; }
        public string DisplayName { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }
    }

    public sealed class HistoryComparisonApplicationRowViewModel
    {
        public HistoryComparisonApplicationRowViewModel(
            string displayName,
            string processName,
            string? iconSourcePath,
            IReadOnlyList<HistoryComparisonMetricDisplayItem> visibleMetrics)
        {
            DisplayName = displayName;
            ProcessName = processName;
            IconSourcePath = iconSourcePath;
            VisibleMetrics = visibleMetrics;
        }

        public string DisplayName { get; }
        public string ProcessName { get; }
        public string? IconSourcePath { get; }
        public IReadOnlyList<HistoryComparisonMetricDisplayItem> VisibleMetrics { get; }
    }

    public readonly record struct HistoryComparisonMetricDisplayItem(string Label, string Value);

    public enum HistoryComparisonMetric
    {
        ActiveDays,
        ForegroundDuration,
        BackgroundDuration,
        ForegroundRatio,
        AverageWorkingSet,
        AverageCpu,
        AverageThreadCount,
        PeakWorkingSet,
        PeakThreadCount,
        ThreadPeakMeanRatio,
        TotalTraffic,
        PeakTraffic,
        TotalIo,
        PeakIo,
        AverageIops
    }
}
