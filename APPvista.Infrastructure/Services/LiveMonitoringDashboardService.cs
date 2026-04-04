using APPvista.Application.Abstractions;
using APPvista.Domain.Entities;

namespace APPvista.Infrastructure.Services;

public sealed class LiveMonitoringDashboardService : IMonitoringDashboardService, IDisposable
{
    private static readonly TimeSpan PersistInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(10);
    private readonly IProcessSnapshotProvider _processSnapshotProvider;
    private readonly IWhitelistStore _whitelistStore;
    private readonly IDailyProcessActivityStore _dailyProcessActivityStore;
    private readonly IProcessNetworkUsageSource _networkUsageSource;
    private readonly object _sync = new();
    private const int CloseMissThreshold = 3;
    private bool _windowedOnlyRecording;

    private DateOnly _currentDay;
    private DateTime? _lastSampleTimeUtc;
    private Dictionary<string, DailyProcessActivitySummary> _dailySummaries;
    private Dictionary<string, (long DownloadBytes, long UploadBytes)> _pendingBandwidthByProcess = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, ProcessResourceSnapshot> _lastLiveSnapshots = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, int> _consecutiveMissCounts = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, DateTime> _lastSeenLiveTimesUtc = new(StringComparer.OrdinalIgnoreCase);
    private DateTime? _lastBandwidthWindowUtc;
    private DateTime? _lastPersistTimeUtc;
    private bool _hasDirtySummaries;
    private bool _isFirstCapture = true;

    public LiveMonitoringDashboardService(
        IProcessSnapshotProvider processSnapshotProvider,
        IWhitelistStore whitelistStore,
        IDailyProcessActivityStore dailyProcessActivityStore,
        IProcessNetworkUsageSource networkUsageSource,
        bool windowedOnlyRecording = false)
    {
        _processSnapshotProvider = processSnapshotProvider;
        _whitelistStore = whitelistStore;
        _dailyProcessActivityStore = dailyProcessActivityStore;
        _networkUsageSource = networkUsageSource;
        _windowedOnlyRecording = windowedOnlyRecording;
        _currentDay = DateOnly.FromDateTime(DateTime.Today);
        _dailySummaries = _dailyProcessActivityStore
            .Load(_currentDay)
            .ToDictionary(item => item.ProcessName, StringComparer.OrdinalIgnoreCase);
    }

    public bool IsWindowedOnlyRecording
    {
        get
        {
            lock (_sync)
            {
                return _windowedOnlyRecording;
            }
        }
    }

    public DashboardSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            var nowLocal = DateTime.Now;
            var nowUtc = DateTime.UtcNow;
            var today = DateOnly.FromDateTime(nowLocal);

            EnsureCurrentDay(today);
            ApplyNetworkEvents(today);

            var bandwidthWindowSeconds = GetBandwidthWindowSeconds(nowUtc);
            var bandwidthByProcess = BuildRealtimeBandwidth(bandwidthWindowSeconds);

            var whitelist = _whitelistStore.Load();
            var captureBatch = _processSnapshotProvider.CaptureTopProcesses(
                _isFirstCapture ? 128 : 512,
                lightweight: _isFirstCapture);
            _isFirstCapture = false;
            var incompleteProcessNames = new HashSet<string>(captureBatch.IncompleteProcessNames, StringComparer.OrdinalIgnoreCase);
            var allProcesses = captureBatch.Processes;
            UpdateDailyActivity(allProcesses, nowUtc, today);
            var includedLiveProcessSamples = allProcesses.Where(ShouldIncludeForRecording).ToList();

            var liveProcesses = includedLiveProcessSamples
                .Where(item => !whitelist.Contains(item.ProcessName))
                .Select(item => AttachDailyActivity(item, bandwidthByProcess))
                .ToList();

            foreach (var processName in incompleteProcessNames)
            {
                if (whitelist.Contains(processName))
                {
                    continue;
                }

                if (_lastLiveSnapshots.TryGetValue(processName, out var lastLiveSnapshot))
                {
                    liveProcesses.Add(lastLiveSnapshot);
                }
            }

            UpdateLiveSnapshotCache(liveProcesses);

            var closedProcesses = _dailySummaries.Values
                .Where(summary => !whitelist.Contains(summary.ProcessName))
                .Where(summary => !_windowedOnlyRecording || summary.HasMainWindow)
                .Where(summary => !incompleteProcessNames.Contains(summary.ProcessName))
                .Where(summary => liveProcesses.All(item => !string.Equals(item.ProcessName, summary.ProcessName, StringComparison.OrdinalIgnoreCase)))
                .Where(summary =>
                    summary.ForegroundMilliseconds > 0 ||
                    summary.BackgroundMilliseconds > 0 ||
                    summary.DownloadBytes > 0 ||
                    summary.UploadBytes > 0 ||
                    summary.IoReadBytes > 0 ||
                    summary.IoWriteBytes > 0 ||
                    summary.ThreadSamples > 0)
                .Select(summary => CreateInactiveProcessSnapshot(summary, bandwidthByProcess, nowUtc))
                .ToList();

            var topProcesses = liveProcesses
                .Concat(closedProcesses)
                .OrderByDescending(item => item.ProcessCount > 0)
                .ThenByDescending(item => item.RealtimeDownloadBytesPerSecond + item.RealtimeUploadBytesPerSecond)
                .ThenByDescending(item => item.RealtimeIoReadBytesPerSecond + item.RealtimeIoWriteBytesPerSecond)
                .ThenByDescending(item => item.DailyForegroundMilliseconds + item.DailyBackgroundMilliseconds)
                .ThenByDescending(item => item.CpuUsagePercent)
                .ThenByDescending(item => item.WorkingSetBytes)
                .Take(50)
                .ToList();

            var trackedProcessCount = _dailySummaries.Count(summary =>
                (!_windowedOnlyRecording || summary.Value.HasMainWindow) &&
                (summary.Value.ForegroundMilliseconds > 0 ||
                 summary.Value.BackgroundMilliseconds > 0 ||
                 summary.Value.DownloadBytes > 0 ||
                 summary.Value.UploadBytes > 0 ||
                 summary.Value.IoReadBytes > 0 ||
                 summary.Value.IoWriteBytes > 0 ||
                 summary.Value.ThreadSamples > 0));
            var includedSummaries = _dailySummaries.Values.Where(summary => !_windowedOnlyRecording || summary.HasMainWindow).ToList();
            var totalDownloadBytes = includedSummaries.Sum(item => item.DownloadBytes);
            var totalUploadBytes = includedSummaries.Sum(item => item.UploadBytes);
            var totalIoReadBytes = includedSummaries.Sum(item => item.IoReadBytes);
            var totalIoWriteBytes = includedSummaries.Sum(item => item.IoWriteBytes);
            var realtimeDownloadBytesPerSecond = bandwidthByProcess.Values.Sum(item => item.DownloadBytesPerSecond);
            var realtimeUploadBytesPerSecond = bandwidthByProcess.Values.Sum(item => item.UploadBytesPerSecond);
            var realtimeIoReadBytesPerSecond = includedLiveProcessSamples.Sum(item => item.RealtimeIoReadBytesPerSecond);
            var realtimeIoWriteBytesPerSecond = includedLiveProcessSamples.Sum(item => item.RealtimeIoWriteBytesPerSecond);
            var networkStatus = _networkUsageSource.Status;

            _pendingBandwidthByProcess = new Dictionary<string, (long DownloadBytes, long UploadBytes)>(StringComparer.OrdinalIgnoreCase);
            _lastBandwidthWindowUtc = nowUtc;
            PersistCurrentDayIfDue(nowUtc);

            return new DashboardSnapshot
            {
                CollectionStatus = topProcesses.Count > 0 ? "用户应用采样正常" : "未采到用户应用数据",
                ActiveProcessCount = topProcesses.Count,
                RealtimeTraffic = $"实时带宽 {FormatBytesPerSecond(realtimeDownloadBytesPerSecond + realtimeUploadBytesPerSecond)}",
                TodayTraffic = $"今日流量 {FormatBytes(totalDownloadBytes + totalUploadBytes)} / {networkStatus}",
                RealtimeDiskIo = $"实时IO {FormatBytesPerSecond(realtimeIoReadBytesPerSecond + realtimeIoWriteBytesPerSecond)}",
                TodayDiskIo = $"IO总量 {FormatBytes(totalIoReadBytes + totalIoWriteBytes)}",
                NetworkCaptureStatus = networkStatus,
                RealtimeDownloadBytesPerSecond = realtimeDownloadBytesPerSecond,
                RealtimeUploadBytesPerSecond = realtimeUploadBytesPerSecond,
                TodayDownloadBytes = totalDownloadBytes,
                TodayUploadBytes = totalUploadBytes,
                RealtimeIoReadBytesPerSecond = realtimeIoReadBytesPerSecond,
                RealtimeIoWriteBytesPerSecond = realtimeIoWriteBytesPerSecond,
                TodayIoReadBytes = totalIoReadBytes,
                TodayIoWriteBytes = totalIoWriteBytes,
                WhitelistCount = whitelist.Count,
                StorageStatus = trackedProcessCount > 0 ? $"SQLite 已累计 {trackedProcessCount} 个应用的今日日统计" : "SQLite 仓储已就绪",
                DailyActivityStatus = _lastSampleTimeUtc.HasValue ? $"持续累计中，最后采样 {nowLocal:HH:mm:ss}" : "等待下一次采样",
                TopProcesses = topProcesses
            };
        }
    }

    public void SetWindowedOnlyRecording(bool enabled)
    {
        lock (_sync)
        {
            if (_windowedOnlyRecording == enabled)
            {
                return;
            }

            _windowedOnlyRecording = enabled;
            if (enabled)
            {
                PurgeNonWindowedRecordsForCurrentDay();
            }
        }
    }

    private void EnsureCurrentDay(DateOnly today)
    {
        if (today == _currentDay)
        {
            return;
        }

        PersistCurrentDay(force: true);
        _currentDay = today;
        _lastSampleTimeUtc = null;
        _lastBandwidthWindowUtc = null;
        _lastPersistTimeUtc = null;
        _hasDirtySummaries = false;
        _pendingBandwidthByProcess = new Dictionary<string, (long DownloadBytes, long UploadBytes)>(StringComparer.OrdinalIgnoreCase);
        _lastLiveSnapshots = new Dictionary<string, ProcessResourceSnapshot>(StringComparer.OrdinalIgnoreCase);
        _consecutiveMissCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        _lastSeenLiveTimesUtc = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        _dailySummaries = _dailyProcessActivityStore
            .Load(today)
            .ToDictionary(item => item.ProcessName, StringComparer.OrdinalIgnoreCase);
    }

    private void ApplyNetworkEvents(DateOnly today)
    {
        var events = _networkUsageSource.DrainPendingEvents();
        if (events.Count == 0)
        {
            return;
        }

        foreach (var usageEvent in events)
        {
            if (usageEvent.Bytes <= 0 || string.IsNullOrWhiteSpace(usageEvent.ProcessName))
            {
                continue;
            }

            if (_windowedOnlyRecording &&
                (!_dailySummaries.TryGetValue(usageEvent.ProcessName, out var existingSummary) || !existingSummary.HasMainWindow))
            {
                continue;
            }

            var summary = GetOrCreateSummary(today, usageEvent.ProcessName);
            if (usageEvent.IsDownload)
            {
                summary.DownloadBytes += usageEvent.Bytes;
            }
            else
            {
                summary.UploadBytes += usageEvent.Bytes;
            }

            if (!_pendingBandwidthByProcess.TryGetValue(usageEvent.ProcessName, out var pending))
            {
                pending = (0, 0);
            }

            _pendingBandwidthByProcess[usageEvent.ProcessName] = usageEvent.IsDownload
                ? (pending.DownloadBytes + usageEvent.Bytes, pending.UploadBytes)
                : (pending.DownloadBytes, pending.UploadBytes + usageEvent.Bytes);

            MarkDirty();
        }
    }

    private double GetBandwidthWindowSeconds(DateTime nowUtc)
    {
        if (!_lastBandwidthWindowUtc.HasValue)
        {
            _lastBandwidthWindowUtc = nowUtc;
            return 1d;
        }

        var seconds = (nowUtc - _lastBandwidthWindowUtc.Value).TotalSeconds;
        return seconds <= 0 ? 1d : seconds;
    }

    private Dictionary<string, (long DownloadBytesPerSecond, long UploadBytesPerSecond)> BuildRealtimeBandwidth(double bandwidthWindowSeconds)
    {
        var result = new Dictionary<string, (long DownloadBytesPerSecond, long UploadBytesPerSecond)>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in _pendingBandwidthByProcess)
        {
            var downloadBytesPerSecond = (long)Math.Round(pair.Value.DownloadBytes / bandwidthWindowSeconds, MidpointRounding.AwayFromZero);
            var uploadBytesPerSecond = (long)Math.Round(pair.Value.UploadBytes / bandwidthWindowSeconds, MidpointRounding.AwayFromZero);
            result[pair.Key] = (downloadBytesPerSecond, uploadBytesPerSecond);

            var summary = GetOrCreateSummary(_currentDay, pair.Key);
            summary.PeakDownloadBytesPerSecond = Math.Max(summary.PeakDownloadBytesPerSecond, downloadBytesPerSecond);
            summary.PeakUploadBytesPerSecond = Math.Max(summary.PeakUploadBytesPerSecond, uploadBytesPerSecond);
            MarkDirty();
        }

        return result;
    }

    private void UpdateDailyActivity(IReadOnlyList<ProcessResourceSnapshot> allProcesses, DateTime nowUtc, DateOnly today)
    {
        var elapsed = _lastSampleTimeUtc.HasValue
            ? nowUtc - _lastSampleTimeUtc.Value
            : TimeSpan.Zero;

        if (elapsed > TimeSpan.Zero)
        {
            foreach (var process in allProcesses)
            {
                if (!ShouldIncludeForRecording(process))
                {
                    continue;
                }

                var summary = GetOrCreateSummary(today, process.ProcessName);
                if (string.IsNullOrWhiteSpace(summary.ExecutablePath) && !string.IsNullOrWhiteSpace(process.ExecutablePath))
                {
                    summary.ExecutablePath = process.ExecutablePath;
                }

                summary.HasMainWindow |= process.HasMainWindow;

                if (process.IsForeground)
                {
                    summary.ForegroundMilliseconds += (long)elapsed.TotalMilliseconds;
                    summary.ForegroundCpuTotal += process.CpuUsagePercent;
                    summary.ForegroundWorkingSetTotal += process.WorkingSetBytes;
                    summary.ForegroundSamples += 1;
                    summary.ForegroundIoOperations += process.RealtimeIoReadOpsPerSecond > 0 || process.RealtimeIoWriteOpsPerSecond > 0
                        ? (long)Math.Round((process.RealtimeIoReadOpsPerSecond + process.RealtimeIoWriteOpsPerSecond) * elapsed.TotalSeconds, MidpointRounding.AwayFromZero)
                        : 0;
                }
                else
                {
                    summary.BackgroundMilliseconds += (long)elapsed.TotalMilliseconds;
                    summary.BackgroundCpuTotal += process.CpuUsagePercent;
                    summary.BackgroundWorkingSetTotal += process.WorkingSetBytes;
                    summary.BackgroundSamples += 1;
                    summary.BackgroundIoOperations += process.RealtimeIoReadOpsPerSecond > 0 || process.RealtimeIoWriteOpsPerSecond > 0
                        ? (long)Math.Round((process.RealtimeIoReadOpsPerSecond + process.RealtimeIoWriteOpsPerSecond) * elapsed.TotalSeconds, MidpointRounding.AwayFromZero)
                        : 0;
                }

                summary.PeakWorkingSetBytes = Math.Max(summary.PeakWorkingSetBytes, process.WorkingSetBytes);
                summary.ThreadCountTotal += process.ThreadCount;
                summary.ThreadSamples += 1;
                summary.PeakThreadCount = Math.Max(summary.PeakThreadCount, process.ThreadCount);
                summary.IoReadBytes += process.IoReadBytesDelta;
                summary.IoWriteBytes += process.IoWriteBytesDelta;
                summary.IoReadOperations += process.RealtimeIoReadOpsPerSecond > 0
                    ? (long)Math.Round(process.RealtimeIoReadOpsPerSecond * elapsed.TotalSeconds, MidpointRounding.AwayFromZero)
                    : 0;
                summary.IoWriteOperations += process.RealtimeIoWriteOpsPerSecond > 0
                    ? (long)Math.Round(process.RealtimeIoWriteOpsPerSecond * elapsed.TotalSeconds, MidpointRounding.AwayFromZero)
                    : 0;
                summary.PeakIoReadBytesPerSecond = Math.Max(summary.PeakIoReadBytesPerSecond, process.RealtimeIoReadBytesPerSecond);
                summary.PeakIoWriteBytesPerSecond = Math.Max(summary.PeakIoWriteBytesPerSecond, process.RealtimeIoWriteBytesPerSecond);
                summary.PeakIoBytesPerSecond = Math.Max(
                    summary.PeakIoBytesPerSecond,
                    process.RealtimeIoReadBytesPerSecond + process.RealtimeIoWriteBytesPerSecond);
                MarkDirty();
            }
        }

        _lastSampleTimeUtc = nowUtc;
    }

    private void UpdateLiveSnapshotCache(IReadOnlyCollection<ProcessResourceSnapshot> liveProcesses)
    {
        var nowUtc = DateTime.UtcNow;
        var liveNames = new HashSet<string>(liveProcesses.Select(item => item.ProcessName), StringComparer.OrdinalIgnoreCase);

        foreach (var process in liveProcesses)
        {
            _lastLiveSnapshots[process.ProcessName] = process;
            _consecutiveMissCounts[process.ProcessName] = 0;
            _lastSeenLiveTimesUtc[process.ProcessName] = nowUtc;
        }

        foreach (var processName in _lastLiveSnapshots.Keys.ToList())
        {
            if (liveNames.Contains(processName))
            {
                continue;
            }

            _consecutiveMissCounts.TryGetValue(processName, out var misses);
            _consecutiveMissCounts[processName] = misses + 1;
        }
    }

    private DailyProcessActivitySummary GetOrCreateSummary(DateOnly today, string processName)
    {
        if (_dailySummaries.TryGetValue(processName, out var summary))
        {
            return summary;
        }

        summary = new DailyProcessActivitySummary
        {
            Day = today.ToString("yyyy-MM-dd"),
            ProcessName = processName
        };
        _dailySummaries[processName] = summary;
        return summary;
    }

    private ProcessResourceSnapshot AttachDailyActivity(
        ProcessResourceSnapshot process,
        IReadOnlyDictionary<string, (long DownloadBytesPerSecond, long UploadBytesPerSecond)> bandwidthByProcess)
    {
        _dailySummaries.TryGetValue(process.ProcessName, out var summary);
        bandwidthByProcess.TryGetValue(process.ProcessName, out var bandwidth);

        return new ProcessResourceSnapshot
        {
            ProcessName = process.ProcessName,
            ProcessId = process.ProcessId,
            ProcessCount = process.ProcessCount,
            ExecutablePath = process.ExecutablePath,
            CpuUsagePercent = process.CpuUsagePercent,
            WorkingSetBytes = process.WorkingSetBytes,
            PeakWorkingSetBytes = summary?.PeakWorkingSetBytes ?? process.PeakWorkingSetBytes,
            PrivateMemoryBytes = process.PrivateMemoryBytes,
            CommitSizeBytes = process.CommitSizeBytes,
            ThreadCount = process.ThreadCount,
            AverageThreadCount = summary?.AverageThreadCount ?? process.AverageThreadCount,
            AverageForegroundCpu = summary?.AverageForegroundCpu ?? process.AverageForegroundCpu,
            AverageForegroundWorkingSetBytes = summary?.AverageForegroundWorkingSetBytes ?? process.AverageForegroundWorkingSetBytes,
            AverageForegroundIops = summary?.AverageForegroundIops ?? process.AverageForegroundIops,
            AverageBackgroundCpu = summary?.AverageBackgroundCpu ?? process.AverageBackgroundCpu,
            AverageBackgroundWorkingSetBytes = summary?.AverageBackgroundWorkingSetBytes ?? process.AverageBackgroundWorkingSetBytes,
            AverageBackgroundIops = summary?.AverageBackgroundIops ?? process.AverageBackgroundIops,
            AverageIops = summary?.AverageIops ?? process.AverageIops,
            PeakThreadCount = summary?.PeakThreadCount ?? process.PeakThreadCount,
            HasMainWindow = summary?.HasMainWindow ?? process.HasMainWindow,
            IsForeground = process.IsForeground,
            DailyForegroundMilliseconds = summary?.ForegroundMilliseconds ?? process.DailyForegroundMilliseconds,
            DailyBackgroundMilliseconds = summary?.BackgroundMilliseconds ?? process.DailyBackgroundMilliseconds,
            DailyDownloadBytes = summary?.DownloadBytes ?? process.DailyDownloadBytes,
            DailyUploadBytes = summary?.UploadBytes ?? process.DailyUploadBytes,
            RealtimeDownloadBytesPerSecond = bandwidth.DownloadBytesPerSecond,
            RealtimeUploadBytesPerSecond = bandwidth.UploadBytesPerSecond,
            PeakDownloadBytesPerSecond = summary?.PeakDownloadBytesPerSecond ?? process.PeakDownloadBytesPerSecond,
            PeakUploadBytesPerSecond = summary?.PeakUploadBytesPerSecond ?? process.PeakUploadBytesPerSecond,
            RealtimeIoReadOpsPerSecond = process.RealtimeIoReadOpsPerSecond,
            RealtimeIoWriteOpsPerSecond = process.RealtimeIoWriteOpsPerSecond,
            RealtimeIoReadBytesPerSecond = process.RealtimeIoReadBytesPerSecond,
            RealtimeIoWriteBytesPerSecond = process.RealtimeIoWriteBytesPerSecond,
            IoReadBytesDelta = process.IoReadBytesDelta,
            IoWriteBytesDelta = process.IoWriteBytesDelta,
            DailyIoReadBytes = summary?.IoReadBytes ?? process.DailyIoReadBytes,
            DailyIoWriteBytes = summary?.IoWriteBytes ?? process.DailyIoWriteBytes,
            PeakIoReadBytesPerSecond = summary?.PeakIoReadBytesPerSecond ?? process.PeakIoReadBytesPerSecond,
            PeakIoWriteBytesPerSecond = summary?.PeakIoWriteBytesPerSecond ?? process.PeakIoWriteBytesPerSecond,
            PeakIoBytesPerSecond = summary?.PeakIoBytesPerSecond ?? process.PeakIoBytesPerSecond
        };
    }

    private ProcessResourceSnapshot CreateInactiveProcessSnapshot(
        DailyProcessActivitySummary summary,
        IReadOnlyDictionary<string, (long DownloadBytesPerSecond, long UploadBytesPerSecond)> bandwidthByProcess,
        DateTime nowUtc)
    {
        _consecutiveMissCounts.TryGetValue(summary.ProcessName, out var misses);
        _lastSeenLiveTimesUtc.TryGetValue(summary.ProcessName, out var lastSeenUtc);
        var isClosed = misses >= CloseMissThreshold ||
                       (lastSeenUtc != default && nowUtc - lastSeenUtc >= CloseTimeout);

        if (!isClosed && _lastLiveSnapshots.TryGetValue(summary.ProcessName, out var lastLiveSnapshot))
        {
            bandwidthByProcess.TryGetValue(summary.ProcessName, out var bandwidth);

            return new ProcessResourceSnapshot
            {
                ProcessName = summary.ProcessName,
                ProcessId = lastLiveSnapshot.ProcessId,
                ProcessCount = Math.Max(lastLiveSnapshot.ProcessCount, 1),
                ExecutablePath = lastLiveSnapshot.ExecutablePath,
                CpuUsagePercent = 0,
                WorkingSetBytes = lastLiveSnapshot.WorkingSetBytes,
                PeakWorkingSetBytes = summary.PeakWorkingSetBytes,
                PrivateMemoryBytes = lastLiveSnapshot.PrivateMemoryBytes,
                CommitSizeBytes = lastLiveSnapshot.CommitSizeBytes,
                ThreadCount = lastLiveSnapshot.ThreadCount,
                AverageThreadCount = summary.AverageThreadCount,
                PeakThreadCount = summary.PeakThreadCount,
                HasMainWindow = summary.HasMainWindow,
                IsForeground = false,
                DailyForegroundMilliseconds = summary.ForegroundMilliseconds,
                DailyBackgroundMilliseconds = summary.BackgroundMilliseconds,
                AverageForegroundCpu = summary.AverageForegroundCpu,
                AverageForegroundWorkingSetBytes = summary.AverageForegroundWorkingSetBytes,
                AverageForegroundIops = summary.AverageForegroundIops,
                AverageBackgroundCpu = summary.AverageBackgroundCpu,
                AverageBackgroundWorkingSetBytes = summary.AverageBackgroundWorkingSetBytes,
                AverageBackgroundIops = summary.AverageBackgroundIops,
                DailyDownloadBytes = summary.DownloadBytes,
                DailyUploadBytes = summary.UploadBytes,
                RealtimeDownloadBytesPerSecond = bandwidth.DownloadBytesPerSecond,
                RealtimeUploadBytesPerSecond = bandwidth.UploadBytesPerSecond,
                PeakDownloadBytesPerSecond = summary.PeakDownloadBytesPerSecond,
                PeakUploadBytesPerSecond = summary.PeakUploadBytesPerSecond,
                RealtimeIoReadOpsPerSecond = 0,
                RealtimeIoWriteOpsPerSecond = 0,
                RealtimeIoReadBytesPerSecond = 0,
                RealtimeIoWriteBytesPerSecond = 0,
                IoReadBytesDelta = 0,
                IoWriteBytesDelta = 0,
                DailyIoReadBytes = summary.IoReadBytes,
                DailyIoWriteBytes = summary.IoWriteBytes,
                PeakIoReadBytesPerSecond = summary.PeakIoReadBytesPerSecond,
                PeakIoWriteBytesPerSecond = summary.PeakIoWriteBytesPerSecond,
                PeakIoBytesPerSecond = summary.PeakIoBytesPerSecond,
                AverageIops = summary.AverageIops
            };
        }

        return CreateClosedProcessSnapshot(summary);
    }

    private ProcessResourceSnapshot CreateClosedProcessSnapshot(DailyProcessActivitySummary summary)
    {
        _lastLiveSnapshots.TryGetValue(summary.ProcessName, out var lastLiveSnapshot);

        return new ProcessResourceSnapshot
        {
            ProcessName = summary.ProcessName,
            ProcessId = 0,
            ProcessCount = 0,
            ExecutablePath = !string.IsNullOrWhiteSpace(summary.ExecutablePath)
                ? summary.ExecutablePath
                : lastLiveSnapshot?.ExecutablePath ?? string.Empty,
            CpuUsagePercent = 0,
            WorkingSetBytes = 0,
            PeakWorkingSetBytes = summary.PeakWorkingSetBytes,
            PrivateMemoryBytes = 0,
            CommitSizeBytes = 0,
            ThreadCount = 0,
            AverageThreadCount = summary.AverageThreadCount,
            PeakThreadCount = summary.PeakThreadCount,
            HasMainWindow = summary.HasMainWindow,
            IsForeground = false,
            DailyForegroundMilliseconds = summary.ForegroundMilliseconds,
            DailyBackgroundMilliseconds = summary.BackgroundMilliseconds,
            AverageForegroundCpu = summary.AverageForegroundCpu,
            AverageForegroundWorkingSetBytes = summary.AverageForegroundWorkingSetBytes,
            AverageForegroundIops = summary.AverageForegroundIops,
            AverageBackgroundCpu = summary.AverageBackgroundCpu,
            AverageBackgroundWorkingSetBytes = summary.AverageBackgroundWorkingSetBytes,
            AverageBackgroundIops = summary.AverageBackgroundIops,
            DailyDownloadBytes = summary.DownloadBytes,
            DailyUploadBytes = summary.UploadBytes,
            RealtimeDownloadBytesPerSecond = 0,
            RealtimeUploadBytesPerSecond = 0,
            PeakDownloadBytesPerSecond = summary.PeakDownloadBytesPerSecond,
            PeakUploadBytesPerSecond = summary.PeakUploadBytesPerSecond,
            RealtimeIoReadOpsPerSecond = 0,
            RealtimeIoWriteOpsPerSecond = 0,
            RealtimeIoReadBytesPerSecond = 0,
            RealtimeIoWriteBytesPerSecond = 0,
            IoReadBytesDelta = 0,
            IoWriteBytesDelta = 0,
            DailyIoReadBytes = summary.IoReadBytes,
            DailyIoWriteBytes = summary.IoWriteBytes,
            PeakIoReadBytesPerSecond = summary.PeakIoReadBytesPerSecond,
            PeakIoWriteBytesPerSecond = summary.PeakIoWriteBytesPerSecond,
            PeakIoBytesPerSecond = summary.PeakIoBytesPerSecond,
            AverageIops = summary.AverageIops
        };
    }

    private void PersistCurrentDayIfDue(DateTime nowUtc)
    {
        if (!_hasDirtySummaries)
        {
            return;
        }

        if (_lastPersistTimeUtc.HasValue && nowUtc - _lastPersistTimeUtc.Value < PersistInterval)
        {
            return;
        }

        PersistCurrentDay(force: true);
        _lastPersistTimeUtc = nowUtc;
    }

    private void PersistCurrentDay(bool force)
    {
        if (!force && !_hasDirtySummaries)
        {
            return;
        }

        _dailyProcessActivityStore.Save(_currentDay, _dailySummaries.Values.OrderBy(item => item.ProcessName).ToList());
        _hasDirtySummaries = false;
    }

    private void MarkDirty()
    {
        _hasDirtySummaries = true;
    }

    private bool ShouldIncludeForRecording(ProcessResourceSnapshot process)
    {
        return !_windowedOnlyRecording || process.HasMainWindow;
    }

    private void PurgeNonWindowedRecordsForCurrentDay()
    {
        var removedNames = _dailySummaries.Values
            .Where(summary => !summary.HasMainWindow)
            .Select(summary => summary.ProcessName)
            .ToList();

        if (removedNames.Count == 0)
        {
            return;
        }

        foreach (var processName in removedNames)
        {
            _dailySummaries.Remove(processName);
            _pendingBandwidthByProcess.Remove(processName);
            _lastLiveSnapshots.Remove(processName);
            _consecutiveMissCounts.Remove(processName);
            _lastSeenLiveTimesUtc.Remove(processName);
        }

        PersistCurrentDay(force: true);
        _lastPersistTimeUtc = DateTime.UtcNow;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            PersistCurrentDay(force: true);
        }
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

        return value.ToString(unitIndex == 0 ? "F0" : "F2") + " " + units[unitIndex];
    }
}
