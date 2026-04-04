using System.Diagnostics;
using APPvista.Application.Abstractions;
using APPvista.Domain.Entities;

namespace APPvista.Infrastructure.Services;

public sealed class TracingMonitoringDashboardService : IMonitoringDashboardService, IDisposable
{
    private readonly IMonitoringDashboardService _inner;
    private int _snapshotCount;

    public TracingMonitoringDashboardService(IMonitoringDashboardService inner)
    {
        _inner = inner;
    }

    public bool IsWindowedOnlyRecording => _inner.IsWindowedOnlyRecording;

    public DashboardSnapshot GetSnapshot()
    {
        var started = Stopwatch.GetTimestamp();
        var snapshot = _inner.GetSnapshot();
        var snapshotIndex = Interlocked.Increment(ref _snapshotCount);
        if (snapshotIndex <= 2)
        {
            StartupPerformanceTrace.MarkDuration(
                $"GetSnapshot active={snapshot.ActiveProcessCount} top={snapshot.TopProcesses.Count}",
                started);
        }

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
