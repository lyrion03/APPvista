using System.Diagnostics;
using APPvista.Application.Abstractions;
using APPvista.Domain.Entities;

namespace APPvista.Infrastructure.Services;

public sealed class TracingMonitoringDashboardService : IMonitoringDashboardService, IDisposable
{
    private readonly IMonitoringDashboardService _inner;
    private int _snapshotCount;
    private int _lastGen0Collections;
    private int _lastGen1Collections;
    private int _lastGen2Collections;

    public TracingMonitoringDashboardService(IMonitoringDashboardService inner)
    {
        _inner = inner;
        _lastGen0Collections = GC.CollectionCount(0);
        _lastGen1Collections = GC.CollectionCount(1);
        _lastGen2Collections = GC.CollectionCount(2);
    }

    public bool IsWindowedOnlyRecording => _inner.IsWindowedOnlyRecording;

    public DashboardSnapshot GetSnapshot()
    {
        var started = Stopwatch.GetTimestamp();
        var snapshot = _inner.GetSnapshot();
        var duration = Stopwatch.GetElapsedTime(started);
        var snapshotIndex = Interlocked.Increment(ref _snapshotCount);
        var currentGen0Collections = GC.CollectionCount(0);
        var currentGen1Collections = GC.CollectionCount(1);
        var currentGen2Collections = GC.CollectionCount(2);
        var gen0Delta = Math.Max(0, currentGen0Collections - _lastGen0Collections);
        var gen1Delta = Math.Max(0, currentGen1Collections - _lastGen1Collections);
        var gen2Delta = Math.Max(0, currentGen2Collections - _lastGen2Collections);
        _lastGen0Collections = currentGen0Collections;
        _lastGen1Collections = currentGen1Collections;
        _lastGen2Collections = currentGen2Collections;
        var managedMemoryBytes = GC.GetTotalMemory(forceFullCollection: false);

        if (snapshotIndex <= 2)
        {
            StartupPerformanceTrace.MarkDuration(
                $"GetSnapshot active={snapshot.ActiveProcessCount} top={snapshot.TopProcesses.Count}",
                started);
        }

        StartupPerformanceTrace.MarkRuntime(
            $"GetSnapshot idx={snapshotIndex} active={snapshot.ActiveProcessCount} top={snapshot.TopProcesses.Count} duration_ms={duration.TotalMilliseconds:F1} managed_mb={managedMemoryBytes / 1024d / 1024d:F1} gc=({gen0Delta},{gen1Delta},{gen2Delta})");

        return snapshot;
    }

    public void SetWindowedOnlyRecording(bool enabled)
    {
        _inner.SetWindowedOnlyRecording(enabled);
    }

    public void Dispose()
    {
        if (_inner is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
