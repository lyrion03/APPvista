using System.Collections.ObjectModel;

namespace APPvista.Desktop.ViewModels;

public sealed class ApplicationCardMetricPreferences : ObservableObject
{
    public const int MinimumSelectedMetricCount = 2;
    public const int MaximumSelectedMetricCount = 6;
    public const string FocusDurationId = "focus_duration";
    public const string DailyTrafficId = "daily_traffic";
    public const string WorkingSetId = "working_set";
    public const string DailyIoTotalId = "daily_io_total";
    public const string CpuId = "cpu";
    public const string RealtimeTrafficId = "realtime_traffic";
    public const string RealtimeIoId = "realtime_io";
    public const string ThreadPressureId = "thread_pressure";
    public const string ProcessCountId = "process_count";
    public const string PeakWorkingSetId = "peak_working_set";

    public static readonly IReadOnlyList<ApplicationCardMetricDefinition> Definitions =
    [
        new(FocusDurationId, "前台时长", "使用时段"),
        new(ProcessCountId, "进程数", "使用时段"),
        new(CpuId, "CPU", "资源占用"),
        new(WorkingSetId, "内存", "资源占用"),
        new(PeakWorkingSetId, "工作集峰值", "资源占用"),
        new(DailyTrafficId, "总流量", "网络"),
        new(RealtimeTrafficId, "实时网速", "网络"),
        new(DailyIoTotalId, "I/O 总量", "应用 I/O"),
        new(RealtimeIoId, "当前 I/O", "应用 I/O"),
        new(ThreadPressureId, "线程峰均比", "线程")
    ];

    public static readonly string[] DefaultMetricIds =
    [
        FocusDurationId,
        DailyTrafficId,
        WorkingSetId,
        DailyIoTotalId
    ];

    private readonly ObservableCollection<string> _selectedMetricIds;

    public ApplicationCardMetricPreferences(IEnumerable<string>? selectedMetricIds = null)
    {
        _selectedMetricIds = new ObservableCollection<string>(NormalizeSelection(selectedMetricIds));
    }

    public IReadOnlyList<string> SelectedMetricIds => _selectedMetricIds;

    public void SetSelectedMetricIds(IEnumerable<string>? metricIds)
    {
        var normalized = NormalizeSelection(metricIds);
        if (_selectedMetricIds.SequenceEqual(normalized, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        _selectedMetricIds.Clear();
        foreach (var metricId in normalized)
        {
            _selectedMetricIds.Add(metricId);
        }

        RaisePropertyChanged(nameof(SelectedMetricIds));
    }

    private static IReadOnlyList<string> NormalizeSelection(IEnumerable<string>? metricIds)
    {
        var selected = (metricIds ?? [])
            .Where(id => Definitions.Any(definition => string.Equals(definition.Id, id, StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaximumSelectedMetricCount)
            .ToList();

        foreach (var defaultId in DefaultMetricIds)
        {
            if (selected.Count >= MinimumSelectedMetricCount)
            {
                break;
            }

            if (!selected.Contains(defaultId, StringComparer.OrdinalIgnoreCase))
            {
                selected.Add(defaultId);
            }
        }

        return selected;
    }
}

public sealed record ApplicationCardMetricDefinition(string Id, string Label, string Category);
