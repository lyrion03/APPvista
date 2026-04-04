namespace WinFormsApp1.Domain.Entities;

public sealed class ProcessResourceSnapshot
{
    public string IconCachePath { get; init; } = string.Empty;
    public string ProcessName { get; init; } = string.Empty;
    public int ProcessId { get; init; }
    public int ProcessCount { get; init; }
    public string ExecutablePath { get; init; } = string.Empty;
    public double CpuUsagePercent { get; init; }
    public long WorkingSetBytes { get; init; }
    public long PeakWorkingSetBytes { get; init; }
    public long PrivateMemoryBytes { get; init; }
    public long CommitSizeBytes { get; init; }
    public int ThreadCount { get; init; }
    public double AverageThreadCount { get; init; }
    public int PeakThreadCount { get; init; }
    public bool HasMainWindow { get; init; }
    public bool IsForeground { get; init; }
    public long DailyForegroundMilliseconds { get; init; }
    public long DailyBackgroundMilliseconds { get; init; }
    public double AverageForegroundCpu { get; init; }
    public double AverageForegroundWorkingSetBytes { get; init; }
    public double AverageForegroundIops { get; init; }
    public double AverageBackgroundCpu { get; init; }
    public double AverageBackgroundWorkingSetBytes { get; init; }
    public double AverageBackgroundIops { get; init; }
    public long DailyDownloadBytes { get; init; }
    public long DailyUploadBytes { get; init; }
    public long RealtimeDownloadBytesPerSecond { get; init; }
    public long RealtimeUploadBytesPerSecond { get; init; }
    public long PeakDownloadBytesPerSecond { get; init; }
    public long PeakUploadBytesPerSecond { get; init; }
    public long RealtimeIoReadOpsPerSecond { get; init; }
    public long RealtimeIoWriteOpsPerSecond { get; init; }
    public long RealtimeIoReadBytesPerSecond { get; init; }
    public long RealtimeIoWriteBytesPerSecond { get; init; }
    public long IoReadBytesDelta { get; init; }
    public long IoWriteBytesDelta { get; init; }
    public long DailyIoReadBytes { get; init; }
    public long DailyIoWriteBytes { get; init; }
    public long PeakIoReadBytesPerSecond { get; init; }
    public long PeakIoWriteBytesPerSecond { get; init; }
    public long PeakIoBytesPerSecond { get; init; }
    public double AverageIops { get; init; }

    public string CpuDisplay => $"{CpuUsagePercent:F1}%";
    public string WorkingSetDisplay => FormatBytes(WorkingSetBytes);
    public string PeakWorkingSetDisplay => FormatBytes(PeakWorkingSetBytes);
    public string PrivateMemoryDisplay => FormatBytes(PrivateMemoryBytes);
    public string CommitSizeDisplay => FormatBytes(CommitSizeBytes);
    public string AverageForegroundCpuDisplay => $"{AverageForegroundCpu:F1}%";
    public string AverageForegroundWorkingSetDisplay => FormatBytes((long)Math.Round(AverageForegroundWorkingSetBytes, MidpointRounding.AwayFromZero));
    public string AverageForegroundIopsDisplay => AverageForegroundIops.ToString("F1");
    public string AverageBackgroundCpuDisplay => $"{AverageBackgroundCpu:F1}%";
    public string AverageBackgroundWorkingSetDisplay => FormatBytes((long)Math.Round(AverageBackgroundWorkingSetBytes, MidpointRounding.AwayFromZero));
    public string AverageBackgroundIopsDisplay => AverageBackgroundIops.ToString("F1");
    public string ThreadAverageDisplay => AverageThreadCount > 0 ? AverageThreadCount.ToString("F1") : "0.0";
    public string PeakThreadDisplay => PeakThreadCount.ToString();
    public string ThreadPeakMeanRatioDisplay => FormatPeakMeanRatio(PeakThreadCount, AverageThreadCount);
    public string ForegroundDisplay => IsForeground ? "前台" : "后台";
    public string DailyForegroundDisplay => FormatDuration(DailyForegroundMilliseconds);
    public string DailyBackgroundDisplay => FormatDuration(DailyBackgroundMilliseconds);
    public string DailyTrafficDisplay => FormatBytes(DailyDownloadBytes + DailyUploadBytes);
    public string DailyDownloadDisplay => FormatBytes(DailyDownloadBytes);
    public string DailyUploadDisplay => FormatBytes(DailyUploadBytes);
    public string RealtimeTrafficDisplay => FormatBytesPerSecond(RealtimeDownloadBytesPerSecond + RealtimeUploadBytesPerSecond);
    public string RealtimeDownloadDisplay => FormatBytesPerSecond(RealtimeDownloadBytesPerSecond);
    public string RealtimeUploadDisplay => FormatBytesPerSecond(RealtimeUploadBytesPerSecond);
    public string PeakTrafficDisplay => FormatBytesPerSecond(PeakDownloadBytesPerSecond + PeakUploadBytesPerSecond);
    public string PeakDownloadDisplay => FormatBytesPerSecond(PeakDownloadBytesPerSecond);
    public string PeakUploadDisplay => FormatBytesPerSecond(PeakUploadBytesPerSecond);
    public string RealtimeIopsDisplay => (RealtimeIoReadOpsPerSecond + RealtimeIoWriteOpsPerSecond).ToString();
    public string AverageIopsDisplay => AverageIops.ToString("F1");
    public string RealtimeIoDisplay => FormatBytesPerSecond(RealtimeIoReadBytesPerSecond + RealtimeIoWriteBytesPerSecond);
    public string RealtimeIoReadDisplay => FormatBytesPerSecond(RealtimeIoReadBytesPerSecond);
    public string RealtimeIoWriteDisplay => FormatBytesPerSecond(RealtimeIoWriteBytesPerSecond);
    public string DailyIoDisplay => FormatBytes(DailyIoReadBytes + DailyIoWriteBytes);
    public string DailyIoReadDisplay => FormatBytes(DailyIoReadBytes);
    public string DailyIoWriteDisplay => FormatBytes(DailyIoWriteBytes);
    public string PeakIoDisplay => FormatBytesPerSecond(PeakIoBytesPerSecond);
    public string PeakIoReadDisplay => FormatBytesPerSecond(PeakIoReadBytesPerSecond);
    public string PeakIoWriteDisplay => FormatBytesPerSecond(PeakIoWriteBytesPerSecond);
    public string IoReadWriteRatioDisplay => FormatReadWriteRatio(DailyIoReadBytes, DailyIoWriteBytes);

    private static string FormatDuration(long milliseconds)
    {
        if (milliseconds <= 0)
        {
            return "00:00:00";
        }

        return TimeSpan.FromMilliseconds(milliseconds).ToString(@"hh\:mm\:ss");
    }

    private static string FormatPeakMeanRatio(int peakThreads, double averageThreads)
    {
        if (averageThreads <= 0)
        {
            return "-";
        }

        return (peakThreads / averageThreads).ToString("F2") + "x";
    }

    private static string FormatReadWriteRatio(long readBytes, long writeBytes)
    {
        if (readBytes <= 0 && writeBytes <= 0)
        {
            return "-";
        }

        if (writeBytes <= 0)
        {
            return "∞";
        }

        return (readBytes / (double)writeBytes).ToString("F2");
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
