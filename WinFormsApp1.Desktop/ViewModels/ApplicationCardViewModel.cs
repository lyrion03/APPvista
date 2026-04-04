using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using WinFormsApp1.Domain.Entities;

namespace WinFormsApp1.Desktop.ViewModels;

public sealed class ApplicationCardViewModel : ObservableObject
{
    private readonly Action<ApplicationCardViewModel, string?> _renameCallback;
    private readonly Action<ApplicationCardViewModel> _openDetailsCallback;
    private readonly ApplicationCardMetricPreferences _metricPreferences;
    private ProcessResourceSnapshot _snapshot;
    private string? _customName;
    private bool _isRenaming;
    private string _editableName;

    public ApplicationCardViewModel(
        ProcessResourceSnapshot snapshot,
        string? customName,
        Action<ApplicationCardViewModel, string?> renameCallback,
        Action<ApplicationCardViewModel> openDetailsCallback,
        ApplicationCardMetricPreferences metricPreferences)
    {
        _snapshot = snapshot;
        _customName = NormalizeName(customName);
        _renameCallback = renameCallback;
        _openDetailsCallback = openDetailsCallback;
        _metricPreferences = metricPreferences;
        _editableName = DisplayName;
        MetricCards = new ObservableCollection<ApplicationCardMetricDisplayItem>();
        RebuildMetricCards();
        _metricPreferences.PropertyChanged += (_, _) =>
        {
            RebuildMetricCards();
            RaisePropertyChanged(nameof(MetricCardColumns));
            RaisePropertyChanged(nameof(CardWidth));
        };

        StartRenameCommand = new RelayCommand(StartRename);
        CommitRenameCommand = new RelayCommand(CommitRename);
        CancelRenameCommand = new RelayCommand(CancelRename);
        OpenDetailsCommand = new RelayCommand(OpenDetails);
    }

    public string AliasKey => Services.ApplicationAliasStore.CreateKey(_snapshot);
    public ProcessResourceSnapshot Snapshot => _snapshot;
    public string OriginalName => _snapshot.ProcessName;
    public string DisplayName => _customName ?? _snapshot.ProcessName;
    public bool HasCustomName => !string.IsNullOrWhiteSpace(_customName);
    public string? IconSourcePath => string.IsNullOrWhiteSpace(_snapshot.IconCachePath) ? null : _snapshot.IconCachePath;
    public bool IsClosed => _snapshot.ProcessCount <= 0;
    public bool WasOpenedInForegroundToday => _snapshot.DailyForegroundMilliseconds > 0;
    public bool IsForeground => _snapshot.IsForeground;
    public string StateDisplay => IsClosed ? "已挂起或关闭" : WasOpenedInForegroundToday ? "有前台记录" : "仅后台记录";
    public string NameToolTip => HasCustomName ? $"原名：{OriginalName}\n点击以重命名" : "点击以重命名";
    public string IconToolTip => "点击进入详情页";
    public string UsageSummary => BuildUsageSummary(_snapshot);
    public string PerformanceSummary => BuildPerformanceSummary(_snapshot);
    public string FocusAccent => IsClosed ? "#7A7A7A" : WasOpenedInForegroundToday ? "#F79A3E" : "#6C8E67";
    public string CardBackground => IsClosed ? "#F0F0F0" : WasOpenedInForegroundToday ? "#FFF8EF" : "#F3FAF1";
    public string BorderBrush => IsClosed ? "#D1D1D1" : WasOpenedInForegroundToday ? "#F2C58B" : "#C8DFC4";
    public string ForegroundDurationDisplay => FormatDuration(_snapshot.DailyForegroundMilliseconds);
    public string TodayTrafficDisplay => FormatBytes(_snapshot.DailyDownloadBytes + _snapshot.DailyUploadBytes);
    public string DailyIoTotalDisplay => FormatBytes(_snapshot.DailyIoReadBytes + _snapshot.DailyIoWriteBytes);
    public string MemoryDisplay => _snapshot.WorkingSetDisplay;
    public ObservableCollection<ApplicationCardMetricDisplayItem> MetricCards { get; }
    public int MetricCardColumns => _metricPreferences.SelectedMetricIds.Count switch
    {
        <= 4 => 2,
        _ => 3
    };
    public double CardWidth => MetricCardColumns == 3 ? 372d : 324d;
    public double HeatScore => CalculateHeatScore(_snapshot);
    public long CurrentBandwidthBytesPerSecond => _snapshot.RealtimeDownloadBytesPerSecond + _snapshot.RealtimeUploadBytesPerSecond;
    public long TodayTrafficBytes => _snapshot.DailyDownloadBytes + _snapshot.DailyUploadBytes;
    public double CpuUsagePercent => _snapshot.CpuUsagePercent;
    public long WorkingSetBytes => _snapshot.WorkingSetBytes;
    public long CurrentIoBytesPerSecond => _snapshot.RealtimeIoReadBytesPerSecond + _snapshot.RealtimeIoWriteBytesPerSecond;
    public double ThreadPressure => _snapshot.AverageThreadCount <= 0 ? _snapshot.ThreadCount : _snapshot.PeakThreadCount / _snapshot.AverageThreadCount;
    public long ForegroundMilliseconds => _snapshot.DailyForegroundMilliseconds;

    public bool IsRenaming
    {
        get => _isRenaming;
        set => SetProperty(ref _isRenaming, value);
    }

    public string EditableName
    {
        get => _editableName;
        set => SetProperty(ref _editableName, value);
    }

    public ICommand StartRenameCommand { get; }
    public ICommand CommitRenameCommand { get; }
    public ICommand CancelRenameCommand { get; }
    public ICommand OpenDetailsCommand { get; }

    public void Update(ProcessResourceSnapshot snapshot, string? customName)
    {
        var previousSnapshot = _snapshot;
        var previousCustomName = _customName;
        _snapshot = snapshot;
        _customName = NormalizeName(customName);

        if (!IsRenaming)
        {
            _editableName = DisplayName;
        }

        RefreshMetricCards();
        RaiseSnapshotChanged(previousSnapshot, previousCustomName);
    }

    private void StartRename()
    {
        EditableName = DisplayName;
        IsRenaming = true;
    }

    private void CommitRename()
    {
        var previousCustomName = _customName;
        var normalized = NormalizeName(EditableName);
        if (string.Equals(normalized, OriginalName, StringComparison.OrdinalIgnoreCase))
        {
            normalized = null;
        }

        _customName = normalized;
        IsRenaming = false;
        EditableName = DisplayName;
        _renameCallback(this, normalized);
        RaiseSnapshotChanged(_snapshot, previousCustomName);
    }

    private void CancelRename()
    {
        EditableName = DisplayName;
        IsRenaming = false;
    }

    private void OpenDetails()
    {
        _openDetailsCallback(this);
    }

    private void RaiseSnapshotChanged(ProcessResourceSnapshot previousSnapshot, string? previousCustomName)
    {
        RaisePropertyChanged(nameof(Snapshot));

        if (!string.Equals(previousSnapshot.ProcessName, _snapshot.ProcessName, StringComparison.Ordinal) ||
            !string.Equals(previousCustomName, _customName, StringComparison.Ordinal))
        {
            RaisePropertyChanged(nameof(AliasKey));
            RaisePropertyChanged(nameof(OriginalName));
            RaisePropertyChanged(nameof(DisplayName));
            RaisePropertyChanged(nameof(HasCustomName));
            RaisePropertyChanged(nameof(NameToolTip));
        }

        if (!string.Equals(previousSnapshot.IconCachePath, _snapshot.IconCachePath, StringComparison.Ordinal))
        {
            RaisePropertyChanged(nameof(IconSourcePath));
        }

        if (previousSnapshot.ProcessCount != _snapshot.ProcessCount ||
            previousSnapshot.IsForeground != _snapshot.IsForeground ||
            previousSnapshot.DailyForegroundMilliseconds != _snapshot.DailyForegroundMilliseconds)
        {
            RaisePropertyChanged(nameof(IsClosed));
            RaisePropertyChanged(nameof(WasOpenedInForegroundToday));
            RaisePropertyChanged(nameof(IsForeground));
            RaisePropertyChanged(nameof(StateDisplay));
            RaisePropertyChanged(nameof(FocusAccent));
            RaisePropertyChanged(nameof(CardBackground));
            RaisePropertyChanged(nameof(BorderBrush));
        }

        RaisePropertyChanged(nameof(UsageSummary));
        RaisePropertyChanged(nameof(PerformanceSummary));
        RaisePropertyChanged(nameof(ForegroundDurationDisplay));
        RaisePropertyChanged(nameof(TodayTrafficDisplay));
        RaisePropertyChanged(nameof(DailyIoTotalDisplay));
        RaisePropertyChanged(nameof(MemoryDisplay));
        RaisePropertyChanged(nameof(HeatScore));
        RaisePropertyChanged(nameof(CurrentBandwidthBytesPerSecond));
        RaisePropertyChanged(nameof(TodayTrafficBytes));
        RaisePropertyChanged(nameof(CpuUsagePercent));
        RaisePropertyChanged(nameof(WorkingSetBytes));
        RaisePropertyChanged(nameof(CurrentIoBytesPerSecond));
        RaisePropertyChanged(nameof(ThreadPressure));
        RaisePropertyChanged(nameof(ForegroundMilliseconds));
    }

    private void RebuildMetricCards()
    {
        MetricCards.Clear();
        foreach (var metricId in _metricPreferences.SelectedMetricIds)
        {
            MetricCards.Add(new ApplicationCardMetricDisplayItem(
                metricId,
                GetMetricLabel(metricId),
                GetMetricValue(metricId)));
        }
    }

    private void RefreshMetricCards()
    {
        foreach (var metricCard in MetricCards)
        {
            metricCard.Value = GetMetricValue(metricCard.MetricId);
        }
    }

    private string GetMetricLabel(string metricId)
    {
        return metricId switch
        {
            ApplicationCardMetricPreferences.CpuId => "CPU",
            ApplicationCardMetricPreferences.RealtimeTrafficId => "实时网速",
            ApplicationCardMetricPreferences.RealtimeIoId => "当前 I/O",
            ApplicationCardMetricPreferences.ThreadPressureId => "线程峰均比",
            ApplicationCardMetricPreferences.ProcessCountId => "进程数",
            ApplicationCardMetricPreferences.PeakWorkingSetId => "工作集峰值",
            ApplicationCardMetricPreferences.DailyTrafficId => "总流量",
            ApplicationCardMetricPreferences.WorkingSetId => "内存",
            ApplicationCardMetricPreferences.DailyIoTotalId => "I/O 总量",
            _ => "前台时长"
        };
    }

    private string GetMetricValue(string metricId)
    {
        return metricId switch
        {
            ApplicationCardMetricPreferences.CpuId => Snapshot.CpuDisplay,
            ApplicationCardMetricPreferences.RealtimeTrafficId => Snapshot.RealtimeTrafficDisplay,
            ApplicationCardMetricPreferences.RealtimeIoId => Snapshot.RealtimeIoDisplay,
            ApplicationCardMetricPreferences.ThreadPressureId => ThreadPressure.ToString("F2", CultureInfo.InvariantCulture) + "x",
            ApplicationCardMetricPreferences.ProcessCountId => Snapshot.ProcessCount.ToString(CultureInfo.InvariantCulture),
            ApplicationCardMetricPreferences.PeakWorkingSetId => Snapshot.PeakWorkingSetDisplay,
            ApplicationCardMetricPreferences.DailyTrafficId => TodayTrafficDisplay,
            ApplicationCardMetricPreferences.WorkingSetId => MemoryDisplay,
            ApplicationCardMetricPreferences.DailyIoTotalId => DailyIoTotalDisplay,
            _ => ForegroundDurationDisplay
        };
    }

    private static string? NormalizeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private static double CalculateHeatScore(ProcessResourceSnapshot snapshot)
    {
        var cpu = Math.Min(snapshot.CpuUsagePercent, 100d);
        var network = Math.Min((snapshot.RealtimeDownloadBytesPerSecond + snapshot.RealtimeUploadBytesPerSecond) / (1024d * 1024d), 50d) * 1.5d;
        var io = Math.Min((snapshot.RealtimeIoReadBytesPerSecond + snapshot.RealtimeIoWriteBytesPerSecond) / (1024d * 1024d), 40d) * 1.8d;
        var threads = Math.Min(snapshot.ThreadCount, 120);
        return cpu * 0.45d + network * 0.22d + io * 0.23d + threads * 0.10d;
    }

    private static string BuildUsageSummary(ProcessResourceSnapshot snapshot)
    {
        var foreground = FormatDuration(snapshot.DailyForegroundMilliseconds);
        var background = FormatDuration(snapshot.DailyBackgroundMilliseconds);
        var behavior = snapshot.DailyForegroundMilliseconds > 0 ? "当天曾在前台使用" : "仅记录到后台活动";
        return $"{behavior}  前台 {foreground} / 后台 {background}";
    }

    private static string BuildPerformanceSummary(ProcessResourceSnapshot snapshot)
    {
        var parts = new List<string>
        {
            $"CPU {snapshot.CpuDisplay}",
            $"内存 {snapshot.WorkingSetDisplay}"
        };

        if (snapshot.RealtimeDownloadBytesPerSecond + snapshot.RealtimeUploadBytesPerSecond > 0)
        {
            parts.Add($"网速 {FormatBytesPerSecond(snapshot.RealtimeDownloadBytesPerSecond + snapshot.RealtimeUploadBytesPerSecond)}");
        }

        if (snapshot.RealtimeIoReadBytesPerSecond + snapshot.RealtimeIoWriteBytesPerSecond > 0)
        {
            parts.Add($"IO {FormatBytesPerSecond(snapshot.RealtimeIoReadBytesPerSecond + snapshot.RealtimeIoWriteBytesPerSecond)}");
        }

        if (snapshot.ProcessCount <= 0)
        {
            parts.Add("当前已关闭");
        }

        return string.Join("  ·  ", parts);
    }

    private static string FormatDuration(long milliseconds)
    {
        if (milliseconds <= 0)
        {
            return "00:00:00";
        }

        return TimeSpan.FromMilliseconds(milliseconds).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
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
}
