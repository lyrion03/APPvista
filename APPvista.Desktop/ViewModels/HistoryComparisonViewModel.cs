using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using APPvista.Desktop.Services;
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushConverter = System.Windows.Media.BrushConverter;
using MediaSolidColorBrush = System.Windows.Media.SolidColorBrush;
using WpfPoint = System.Windows.Point;

namespace APPvista.Desktop.ViewModels;

public sealed class HistoryComparisonViewModel : ObservableObject
{
    private readonly ApplicationIconCache _applicationIconCache;
    private readonly IReadOnlyDictionary<string, string> _applicationAliases;
    private readonly List<HistoryComparisonMetric> _metricOrder = [];
    private static readonly string[] ParallelChartPalette =
    [
        "#2D5E46",
        "#C26A3D",
        "#577590",
        "#A44A3F",
        "#6D597A",
        "#3A7D6B",
        "#8D5A97",
        "#B08968"
    ];

    private const double ParallelChartMinWidth = 620d;
    private const double ParallelChartAxisSpacing = 144d;
    private const double ParallelChartHeightValue = 320d;
    private const double ParallelChartTopPadding = 52d;
    private const double ParallelChartBottomPadding = 52d;
    private const double ParallelChartLeftPadding = 28d;
    private const double ParallelChartRightPadding = 28d;
    private const double ParallelChartMinLabelWidth = 56d;
    private const double ParallelChartViewportSafetyPadding = 12d;

    private bool _isMetricSelectorOpen;
    private string _windowTitle = "详细对比";
    private string _rangeDisplay = string.Empty;
    private double _parallelChartViewportWidth;

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
        ParallelChartAxes = new ObservableCollection<HistoryComparisonParallelAxisViewModel>();
        ParallelChartSeries = new ObservableCollection<HistoryComparisonParallelSeriesViewModel>();
        HighlightedParallelMarkers = new ObservableCollection<HistoryComparisonParallelMarkerViewModel>();

        ToggleMetricSelectorCommand = new RelayCommand(() => IsMetricSelectorOpen = !IsMetricSelectorOpen);
        CloseMetricSelectorCommand = new RelayCommand(() => IsMetricSelectorOpen = false);
        ToggleAllApplicationsCommand = new RelayCommand(ToggleAllApplications);

        InitializeMetricOptions();
        Load(windowTitle, rangeDisplay, applicationAggregates);
    }

    public ObservableCollection<HistoryComparisonSelectableApplicationViewModel> AvailableApplications { get; }
    public ObservableCollection<HistoryComparisonMetricOptionViewModel> VisibleMetrics { get; }
    public ObservableCollection<HistoryComparisonApplicationRowViewModel> ComparisonRows { get; }
    public ObservableCollection<HistoryComparisonParallelAxisViewModel> ParallelChartAxes { get; }
    public ObservableCollection<HistoryComparisonParallelSeriesViewModel> ParallelChartSeries { get; }
    public ObservableCollection<HistoryComparisonParallelMarkerViewModel> HighlightedParallelMarkers { get; }

    public ICommand ToggleMetricSelectorCommand { get; }
    public ICommand CloseMetricSelectorCommand { get; }
    public ICommand ToggleAllApplicationsCommand { get; }

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
    public bool AllApplicationsSelected => AvailableApplications.Count > 0 && AvailableApplications.All(static item => item.IsSelected);
    public string ToggleAllApplicationsText => AllApplicationsSelected ? "全不选" : "全选";
    public bool HasParallelChartSelection => ParallelChartSeries.Count > 0;
    public bool HasParallelChart => ParallelChartAxes.Count >= 2 && ParallelChartSeries.Count > 0;
    public double ParallelChartWidth => GetParallelChartWidth(ParallelChartAxes.Count);
    public double ParallelChartHostWidth => Math.Max(ParallelChartWidth, GetParallelChartAvailableWidth());
    public double ParallelChartHeight => ParallelChartHeightValue;
    public string ParallelChartHint =>
        !HasParallelChartSelection
            ? "选择应用后可查看平行坐标图。"
            : ParallelChartAxes.Count < 2
                ? "至少勾选两个指标后才能生成平行坐标图。"
                : "各轴已按当前选中应用归一化，适合比较指标结构差异。";

    public string SelectedApplicationsSummary =>
        $"已选择 {ComparisonRows.Count} / {AvailableApplications.Count} 个应用";

    public double ParallelChartViewportWidth
    {
        get => _parallelChartViewportWidth;
        set
        {
            var normalized = Math.Max(0d, value);
            if (SetProperty(ref _parallelChartViewportWidth, normalized))
            {
                RefreshParallelChartLayout();
            }
        }
    }

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
        ParallelChartAxes.Clear();
        ParallelChartSeries.Clear();
        HighlightedParallelMarkers.Clear();

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
        RaisePropertyChanged(nameof(AllApplicationsSelected));
        RaisePropertyChanged(nameof(ToggleAllApplicationsText));
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
        _metricOrder.Add(metric);
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

    private void ToggleAllApplications()
    {
        var shouldSelect = !AllApplicationsSelected;
        foreach (var application in AvailableApplications)
        {
            application.IsSelected = shouldSelect;
        }
    }

    private void RefreshComparisonRows()
    {
        var selectedMetrics = VisibleMetrics
            .Where(static item => item.IsSelected)
            .Select(static item => item.Metric)
            .OrderBy(GetMetricOrderIndex)
            .ToArray();

        var selectedApplications = AvailableApplications
            .Where(static item => item.IsSelected)
            .OrderBy(static item => item.SortKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var rows = selectedApplications
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

        RefreshParallelChart(selectedApplications, selectedMetrics);

        RaisePropertyChanged(nameof(HasSelectedApplications));
        RaisePropertyChanged(nameof(SelectedApplicationsSummary));
        RaisePropertyChanged(nameof(AllApplicationsSelected));
        RaisePropertyChanged(nameof(ToggleAllApplicationsText));
        RaisePropertyChanged(nameof(HasParallelChart));
        RaisePropertyChanged(nameof(HasParallelChartSelection));
        RaisePropertyChanged(nameof(ParallelChartWidth));
        RaisePropertyChanged(nameof(ParallelChartHostWidth));
        RaisePropertyChanged(nameof(ParallelChartHint));
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

    private void RefreshParallelChart(
        IReadOnlyList<HistoryComparisonSelectableApplicationViewModel> selectedApplications,
        IReadOnlyList<HistoryComparisonMetric> selectedMetrics)
    {
        if (selectedApplications.Count == 0 || selectedMetrics.Count == 0)
        {
            ParallelChartAxes.Clear();
            ParallelChartSeries.Clear();
            HighlightedParallelMarkers.Clear();
            return;
        }

        var metrics = selectedMetrics
            .Select(metric => BuildParallelMetricDescriptor(metric, selectedApplications))
            .Where(static item => item is not null)
            .Cast<ParallelMetricDescriptor>()
            .ToList();

        if (metrics.Count == 0)
        {
            ParallelChartAxes.Clear();
            ParallelChartSeries.Clear();
            HighlightedParallelMarkers.Clear();
            return;
        }

        var chartHeight = ParallelChartHeight;
        var plotTop = ParallelChartTopPadding;
        var plotBottom = chartHeight - ParallelChartBottomPadding;
        var plotHeight = Math.Max(1d, plotBottom - plotTop);
        var axisSpacing = GetParallelChartAxisSpacing(metrics.Count);
        var axisLabelWidth = GetParallelChartAxisLabelWidth(axisSpacing);

        var axes = new List<HistoryComparisonParallelAxisViewModel>(metrics.Count);
        for (var index = 0; index < metrics.Count; index++)
        {
            var descriptor = metrics[index];
            var x = ParallelChartLeftPadding + index * axisSpacing;

            axes.Add(new HistoryComparisonParallelAxisViewModel(
                descriptor.Metric,
                descriptor.DisplayName,
                descriptor.MinLabel,
                descriptor.MaxLabel,
                x,
                plotTop,
                plotHeight,
                x - axisLabelWidth / 2d,
                axisLabelWidth));
        }

        var series = new List<HistoryComparisonParallelSeriesViewModel>(selectedApplications.Count);
        for (var applicationIndex = 0; applicationIndex < selectedApplications.Count; applicationIndex++)
        {
            var application = selectedApplications[applicationIndex];
            var points = new PointCollection(metrics.Count);
            var displayMetrics = BuildMetricItems(application.Aggregate, selectedMetrics);
            var markers = new List<HistoryComparisonParallelMarkerViewModel>(metrics.Count);

            for (var metricIndex = 0; metricIndex < metrics.Count; metricIndex++)
            {
                var descriptor = metrics[metricIndex];
                var normalized = descriptor.Normalizer(application.Aggregate);
                var y = plotBottom - (normalized * plotHeight);
                var point = new WpfPoint(axes[metricIndex].X, y);
                points.Add(point);
                var value = metricIndex < displayMetrics.Count ? displayMetrics[metricIndex].Value : string.Empty;
                markers.Add(new HistoryComparisonParallelMarkerViewModel(
                    axes[metricIndex].X,
                    y,
                    value,
                    axes[metricIndex].X + 8d,
                    Math.Max(0d, Math.Min(chartHeight - 24d, y + (metricIndex % 2 == 0 ? -24d : 6d)))));
            }

            series.Add(new HistoryComparisonParallelSeriesViewModel(
                application.DisplayName,
                CreateFrozenBrush(ParallelChartPalette[applicationIndex % ParallelChartPalette.Length]),
                points,
                markers));
        }

        SyncObservableCollection(
            ParallelChartAxes,
            axes,
            static (left, right) =>
                left.Label == right.Label &&
                left.MinLabel == right.MinLabel &&
                left.MaxLabel == right.MaxLabel &&
                left.Metric == right.Metric &&
                left.X.Equals(right.X) &&
                left.Y.Equals(right.Y) &&
                left.Height.Equals(right.Height) &&
                left.LabelLeft.Equals(right.LabelLeft) &&
                left.LabelWidth.Equals(right.LabelWidth));

        SyncObservableCollection(
            ParallelChartSeries,
            series,
            static (left, right) =>
                left.DisplayName == right.DisplayName &&
                Equals(left.Stroke, right.Stroke) &&
                PointCollectionsEqual(left.Points, right.Points));

        ApplyParallelSeriesHighlight(null);
    }

    private double GetParallelChartWidth(int axisCount)
    {
        var targetWidth = GetParallelChartAvailableWidth();
        if (axisCount <= 1)
        {
            return targetWidth;
        }

        var naturalWidth = ParallelChartLeftPadding + ParallelChartRightPadding + (axisCount - 1) * ParallelChartAxisSpacing;
        return Math.Min(naturalWidth, targetWidth);
    }

    private double GetParallelChartAvailableWidth() =>
        Math.Max(ParallelChartMinWidth, _parallelChartViewportWidth - ParallelChartViewportSafetyPadding);

    private double GetParallelChartAxisSpacing(int axisCount)
    {
        if (axisCount <= 1)
        {
            return 0d;
        }

        var chartWidth = GetParallelChartWidth(axisCount);
        var availableSpacing = Math.Max(0d, (chartWidth - ParallelChartLeftPadding - ParallelChartRightPadding) / (axisCount - 1));
        return Math.Min(ParallelChartAxisSpacing, availableSpacing);
    }

    private static double GetParallelChartAxisLabelWidth(double axisSpacing) =>
        Math.Max(ParallelChartMinLabelWidth, Math.Min(88d, axisSpacing + 24d));

    private void RefreshParallelChartLayout()
    {
        RaisePropertyChanged(nameof(ParallelChartWidth));
        RaisePropertyChanged(nameof(ParallelChartHostWidth));

        var selectedMetrics = GetOrderedSelectedMetrics();

        var selectedApplications = AvailableApplications
            .Where(static item => item.IsSelected)
            .OrderBy(static item => item.SortKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        RefreshParallelChart(selectedApplications, selectedMetrics);
        RaisePropertyChanged(nameof(HasParallelChart));
        RaisePropertyChanged(nameof(HasParallelChartSelection));
        RaisePropertyChanged(nameof(ParallelChartHint));
    }

    public void MoveParallelChartAxis(HistoryComparisonMetric metric, double chartX)
    {
        var orderedSelectedMetrics = GetOrderedSelectedMetrics();
        if (orderedSelectedMetrics.Count < 2)
        {
            return;
        }

        var currentIndex = -1;
        for (var index = 0; index < orderedSelectedMetrics.Count; index++)
        {
            if (orderedSelectedMetrics[index] == metric)
            {
                currentIndex = index;
                break;
            }
        }

        if (currentIndex < 0)
        {
            return;
        }

        var targetIndex = 0;
        var bestDistance = double.MaxValue;
        for (var index = 0; index < ParallelChartAxes.Count; index++)
        {
            var distance = Math.Abs(ParallelChartAxes[index].X - chartX);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                targetIndex = index;
            }
        }

        if (targetIndex == currentIndex)
        {
            return;
        }

        MoveMetricInOrder(metric, orderedSelectedMetrics, currentIndex, targetIndex);
        SyncVisibleMetricOrder();
        RefreshComparisonRows();
    }

    private IReadOnlyList<HistoryComparisonMetric> GetOrderedSelectedMetrics() =>
        VisibleMetrics
            .Where(static item => item.IsSelected)
            .Select(static item => item.Metric)
            .OrderBy(GetMetricOrderIndex)
            .ToArray();

    private int GetMetricOrderIndex(HistoryComparisonMetric metric)
    {
        var index = _metricOrder.IndexOf(metric);
        return index >= 0 ? index : int.MaxValue;
    }

    private void MoveMetricInOrder(
        HistoryComparisonMetric metric,
        IReadOnlyList<HistoryComparisonMetric> orderedSelectedMetrics,
        int currentIndex,
        int targetIndex)
    {
        _metricOrder.Remove(metric);

        if (targetIndex < currentIndex)
        {
            var beforeMetric = orderedSelectedMetrics[targetIndex];
            var insertIndex = _metricOrder.IndexOf(beforeMetric);
            _metricOrder.Insert(insertIndex < 0 ? _metricOrder.Count : insertIndex, metric);
            return;
        }

        var afterMetric = orderedSelectedMetrics[targetIndex];
        var afterIndex = _metricOrder.IndexOf(afterMetric);
        _metricOrder.Insert(afterIndex < 0 ? _metricOrder.Count : afterIndex + 1, metric);
    }

    private void SyncVisibleMetricOrder()
    {
        for (var targetIndex = 0; targetIndex < _metricOrder.Count; targetIndex++)
        {
            var metric = _metricOrder[targetIndex];
            var currentIndex = VisibleMetrics
                .Select((item, index) => new { item.Metric, index })
                .First(item => item.Metric == metric)
                .index;

            if (currentIndex != targetIndex)
            {
                VisibleMetrics.Move(currentIndex, targetIndex);
            }
        }
    }

    public void HighlightParallelSeries(string? displayName)
    {
        ApplyParallelSeriesHighlight(displayName);
    }

    public void ClearParallelSeriesHighlight()
    {
        ApplyParallelSeriesHighlight(null);
    }

    public void HighlightNearestParallelSeries(WpfPoint position, double maxDistance)
    {
        HistoryComparisonParallelSeriesViewModel? nearestSeries = null;
        var bestDistance = maxDistance;

        foreach (var series in ParallelChartSeries)
        {
            var distance = GetDistanceToSeries(position, series.Points);
            if (distance <= bestDistance)
            {
                bestDistance = distance;
                nearestSeries = series;
            }
        }

        ApplyParallelSeriesHighlight(nearestSeries?.DisplayName);
    }

    private void ApplyParallelSeriesHighlight(string? displayName)
    {
        var hasHighlight = !string.IsNullOrWhiteSpace(displayName);
        foreach (var series in ParallelChartSeries)
        {
            series.SetHighlightState(
                isHighlighted: hasHighlight && string.Equals(series.DisplayName, displayName, StringComparison.Ordinal),
                dimOthers: hasHighlight);
        }

        var highlightedSeries = hasHighlight
            ? ParallelChartSeries.FirstOrDefault(item => string.Equals(item.DisplayName, displayName, StringComparison.Ordinal))
            : null;

        SyncObservableCollection(
            HighlightedParallelMarkers,
            highlightedSeries?.Markers ?? Array.Empty<HistoryComparisonParallelMarkerViewModel>(),
            static (left, right) =>
                left.X.Equals(right.X) &&
                left.Y.Equals(right.Y) &&
                left.Value == right.Value &&
                left.LabelLeft.Equals(right.LabelLeft) &&
                left.LabelTop.Equals(right.LabelTop));
    }

    private ParallelMetricDescriptor? BuildParallelMetricDescriptor(
        HistoryComparisonMetric metric,
        IReadOnlyList<HistoryComparisonSelectableApplicationViewModel> selectedApplications)
    {
        var values = selectedApplications
            .Select(item => GetMetricNumericValue(item.Aggregate, metric))
            .ToArray();

        if (values.Length == 0)
        {
            return null;
        }

        var min = values.Min();
        var max = values.Max();
        var range = max - min;
        Func<HistoryApplicationAggregate, double> normalizer = range <= 0d
            ? _ => 0.5d
            : aggregate => Math.Clamp((GetMetricNumericValue(aggregate, metric) - min) / range, 0d, 1d);

        return new ParallelMetricDescriptor(
            metric,
            GetMetricDisplayName(metric),
            FormatMetricAxisValue(metric, min),
            FormatMetricAxisValue(metric, max),
            normalizer);
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

    private static double GetMetricNumericValue(HistoryApplicationAggregate aggregate, HistoryComparisonMetric metric) =>
        metric switch
        {
            HistoryComparisonMetric.ActiveDays => aggregate.ActiveDays,
            HistoryComparisonMetric.ForegroundDuration => aggregate.ForegroundMilliseconds,
            HistoryComparisonMetric.BackgroundDuration => aggregate.BackgroundMilliseconds,
            HistoryComparisonMetric.ForegroundRatio => aggregate.ForegroundRatio * 100d,
            HistoryComparisonMetric.AverageWorkingSet => aggregate.AverageWorkingSetBytes,
            HistoryComparisonMetric.AverageCpu => aggregate.AverageCpu,
            HistoryComparisonMetric.AverageThreadCount => aggregate.AverageThreadCount,
            HistoryComparisonMetric.PeakWorkingSet => aggregate.PeakWorkingSetBytes,
            HistoryComparisonMetric.PeakThreadCount => aggregate.PeakThreadCount,
            HistoryComparisonMetric.ThreadPeakMeanRatio => aggregate.ThreadPeakMeanRatio,
            HistoryComparisonMetric.TotalTraffic => aggregate.TotalTrafficBytes,
            HistoryComparisonMetric.PeakTraffic => aggregate.PeakTrafficBytesPerSecond,
            HistoryComparisonMetric.TotalIo => aggregate.TotalIoBytes,
            HistoryComparisonMetric.PeakIo => aggregate.PeakIoBytesPerSecond,
            HistoryComparisonMetric.AverageIops => aggregate.AverageIops,
            _ => 0d
        };

    private static string GetMetricDisplayName(HistoryComparisonMetric metric) =>
        metric switch
        {
            HistoryComparisonMetric.ActiveDays => "活跃天数",
            HistoryComparisonMetric.ForegroundDuration => "前台时长",
            HistoryComparisonMetric.BackgroundDuration => "后台时长",
            HistoryComparisonMetric.ForegroundRatio => "前台占比",
            HistoryComparisonMetric.AverageWorkingSet => "平均工作集",
            HistoryComparisonMetric.AverageCpu => "平均 CPU",
            HistoryComparisonMetric.AverageThreadCount => "平均线程",
            HistoryComparisonMetric.PeakWorkingSet => "工作集峰值",
            HistoryComparisonMetric.PeakThreadCount => "线程峰值",
            HistoryComparisonMetric.ThreadPeakMeanRatio => "线程峰均比",
            HistoryComparisonMetric.TotalTraffic => "总流量",
            HistoryComparisonMetric.PeakTraffic => "网络峰值",
            HistoryComparisonMetric.TotalIo => "I/O 总量",
            HistoryComparisonMetric.PeakIo => "I/O 峰值",
            HistoryComparisonMetric.AverageIops => "平均 IOPS",
            _ => string.Empty
        };

    private static string FormatMetricAxisValue(HistoryComparisonMetric metric, double value) =>
        metric switch
        {
            HistoryComparisonMetric.ActiveDays => $"{Math.Round(value, MidpointRounding.AwayFromZero):0} 天",
            HistoryComparisonMetric.ForegroundDuration => FormatDuration((long)Math.Round(value, MidpointRounding.AwayFromZero)),
            HistoryComparisonMetric.BackgroundDuration => FormatDuration((long)Math.Round(value, MidpointRounding.AwayFromZero)),
            HistoryComparisonMetric.ForegroundRatio => $"{value:F1}%",
            HistoryComparisonMetric.AverageWorkingSet => FormatBytes(value),
            HistoryComparisonMetric.AverageCpu => $"{value:F1}%",
            HistoryComparisonMetric.AverageThreadCount => value.ToString("F1", CultureInfo.InvariantCulture),
            HistoryComparisonMetric.PeakWorkingSet => FormatBytes(value),
            HistoryComparisonMetric.PeakThreadCount => Math.Round(value, MidpointRounding.AwayFromZero).ToString("F0", CultureInfo.InvariantCulture),
            HistoryComparisonMetric.ThreadPeakMeanRatio => value.ToString("F2", CultureInfo.InvariantCulture) + "x",
            HistoryComparisonMetric.TotalTraffic => FormatBytes(value),
            HistoryComparisonMetric.PeakTraffic => FormatBytesPerSecond((long)Math.Round(value, MidpointRounding.AwayFromZero)),
            HistoryComparisonMetric.TotalIo => FormatBytes(value),
            HistoryComparisonMetric.PeakIo => FormatBytesPerSecond((long)Math.Round(value, MidpointRounding.AwayFromZero)),
            HistoryComparisonMetric.AverageIops => value.ToString("F1", CultureInfo.InvariantCulture),
            _ => value.ToString("F1", CultureInfo.InvariantCulture)
        };

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

    private static MediaBrush CreateFrozenBrush(string hexColor)
    {
        var brush = (MediaSolidColorBrush)new MediaBrushConverter().ConvertFromInvariantString(hexColor)!;
        brush.Freeze();
        return brush;
    }

    private static bool PointCollectionsEqual(PointCollection left, PointCollection right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (!left[index].Equals(right[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static double GetDistanceToSeries(WpfPoint position, PointCollection points)
    {
        if (points.Count == 0)
        {
            return double.MaxValue;
        }

        var bestDistance = GetDistance(position, points[0]);
        for (var index = 1; index < points.Count; index++)
        {
            bestDistance = Math.Min(bestDistance, GetDistanceToSegment(position, points[index - 1], points[index]));
        }

        return bestDistance;
    }

    private static double GetDistanceToSegment(WpfPoint point, WpfPoint start, WpfPoint end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        if (Math.Abs(dx) < double.Epsilon && Math.Abs(dy) < double.Epsilon)
        {
            return GetDistance(point, start);
        }

        var t = ((point.X - start.X) * dx + (point.Y - start.Y) * dy) / (dx * dx + dy * dy);
        t = Math.Clamp(t, 0d, 1d);
        var projection = new WpfPoint(start.X + t * dx, start.Y + t * dy);
        return GetDistance(point, projection);
    }

    private static double GetDistance(WpfPoint left, WpfPoint right)
    {
        var dx = left.X - right.X;
        var dy = left.Y - right.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

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

    public sealed class HistoryComparisonParallelAxisViewModel
    {
        public HistoryComparisonParallelAxisViewModel(
            HistoryComparisonMetric metric,
            string label,
            string minLabel,
            string maxLabel,
            double x,
            double y,
            double height,
            double labelLeft,
            double labelWidth)
        {
            Metric = metric;
            Label = label;
            MinLabel = minLabel;
            MaxLabel = maxLabel;
            X = x;
            Y = y;
            Height = height;
            LabelLeft = labelLeft;
            LabelWidth = labelWidth;
        }

        public HistoryComparisonMetric Metric { get; }
        public string Label { get; }
        public string MinLabel { get; }
        public string MaxLabel { get; }
        public double X { get; }
        public double Y { get; }
        public double Height { get; }
        public double LabelLeft { get; }
        public double LabelWidth { get; }
    }

    public sealed class HistoryComparisonParallelSeriesViewModel : ObservableObject
    {
        private double _strokeThickness = 2.4d;
        private double _opacity = 0.88d;
        private bool _isHighlighted;
        private double _legendOpacity = 1d;
        private MediaBrush _legendBackground;
        private MediaBrush _legendBorderBrush;

        public HistoryComparisonParallelSeriesViewModel(
            string displayName,
            MediaBrush stroke,
            PointCollection points,
            IReadOnlyList<HistoryComparisonParallelMarkerViewModel> markers)
        {
            DisplayName = displayName;
            Stroke = stroke;
            Points = points;
            Markers = markers;
            _legendBackground = CreateFrozenBrush("#FFFDF9");
            _legendBorderBrush = CreateFrozenBrush("#D8CEBE");
        }

        public string DisplayName { get; }
        public MediaBrush Stroke { get; }
        public PointCollection Points { get; }
        public IReadOnlyList<HistoryComparisonParallelMarkerViewModel> Markers { get; }
        public double StrokeThickness
        {
            get => _strokeThickness;
            private set => SetProperty(ref _strokeThickness, value);
        }

        public double Opacity
        {
            get => _opacity;
            private set => SetProperty(ref _opacity, value);
        }

        public bool IsHighlighted
        {
            get => _isHighlighted;
            private set => SetProperty(ref _isHighlighted, value);
        }

        public double LegendOpacity
        {
            get => _legendOpacity;
            private set => SetProperty(ref _legendOpacity, value);
        }

        public MediaBrush LegendBackground
        {
            get => _legendBackground;
            private set => SetProperty(ref _legendBackground, value);
        }

        public MediaBrush LegendBorderBrush
        {
            get => _legendBorderBrush;
            private set => SetProperty(ref _legendBorderBrush, value);
        }

        public void SetHighlightState(bool isHighlighted, bool dimOthers)
        {
            IsHighlighted = isHighlighted;
            if (!dimOthers)
            {
                StrokeThickness = 2.4d;
                Opacity = 0.88d;
                LegendOpacity = 1d;
                LegendBackground = CreateFrozenBrush("#FFFDF9");
                LegendBorderBrush = CreateFrozenBrush("#D8CEBE");
                return;
            }

            StrokeThickness = isHighlighted ? 4d : 1.6d;
            Opacity = isHighlighted ? 1d : 0.22d;
            LegendOpacity = isHighlighted ? 1d : 0.45d;
            LegendBackground = isHighlighted ? CreateFrozenBrush("#EEF5F0") : CreateFrozenBrush("#FFFDF9");
            LegendBorderBrush = isHighlighted ? Stroke : CreateFrozenBrush("#D8CEBE");
        }
    }

    public sealed class HistoryComparisonParallelMarkerViewModel
    {
        public HistoryComparisonParallelMarkerViewModel(double x, double y, string value, double labelLeft, double labelTop)
        {
            X = x;
            Y = y;
            Value = value;
            LabelLeft = labelLeft;
            LabelTop = labelTop;
        }

        public double X { get; }
        public double Y { get; }
        public string Value { get; }
        public double LabelLeft { get; }
        public double LabelTop { get; }
    }

    public readonly record struct HistoryComparisonMetricDisplayItem(string Label, string Value);

    private sealed record ParallelMetricDescriptor(
        HistoryComparisonMetric Metric,
        string DisplayName,
        string MinLabel,
        string MaxLabel,
        Func<HistoryApplicationAggregate, double> Normalizer);

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
