namespace APPvista.Domain.Entities;

public sealed class DashboardSnapshot
{
    public string CollectionStatus { get; init; } = string.Empty;
    public int ActiveProcessCount { get; init; }
    public string TodayTraffic { get; init; } = string.Empty;
    public string RealtimeTraffic { get; init; } = string.Empty;
    public string TodayDiskIo { get; init; } = string.Empty;
    public string RealtimeDiskIo { get; init; } = string.Empty;
    public string NetworkCaptureStatus { get; init; } = string.Empty;
    public long RealtimeDownloadBytesPerSecond { get; init; }
    public long RealtimeUploadBytesPerSecond { get; init; }
    public long TodayDownloadBytes { get; init; }
    public long TodayUploadBytes { get; init; }
    public long RealtimeIoReadBytesPerSecond { get; init; }
    public long RealtimeIoWriteBytesPerSecond { get; init; }
    public long TodayIoReadBytes { get; init; }
    public long TodayIoWriteBytes { get; init; }
    public int WhitelistCount { get; init; }
    public string StorageStatus { get; init; } = string.Empty;
    public string DailyActivityStatus { get; init; } = string.Empty;
    public IReadOnlyList<ProcessResourceSnapshot> TopProcesses { get; init; } =
        Array.Empty<ProcessResourceSnapshot>();
}
