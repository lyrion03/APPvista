using System.Diagnostics;
using APPvista.Application.Abstractions;
using APPvista.Domain.Entities;

namespace APPvista.Infrastructure.Services;

public sealed class TracingProcessSnapshotProvider : IProcessSnapshotProvider
{
    private readonly IProcessSnapshotProvider _inner;
    private int _captureCount;

    public TracingProcessSnapshotProvider(IProcessSnapshotProvider inner)
    {
        _inner = inner;
    }

    public ProcessSnapshotBatch CaptureTopProcesses(int count, bool lightweight = false)
    {
        var started = Stopwatch.GetTimestamp();
        var result = _inner.CaptureTopProcesses(count, lightweight);
        var captureIndex = Interlocked.Increment(ref _captureCount);
        if (captureIndex <= 2 || lightweight)
        {
            StartupPerformanceTrace.MarkDuration(
                $"CaptureTopProcesses count={count} lightweight={lightweight} result={result.Processes.Count}",
                started);
        }

        return result;
    }
}
