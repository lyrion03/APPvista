namespace WinFormsApp1.Domain.Entities;

public sealed class DailyProcessActivitySummary
{
    public string Day { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public long ForegroundMilliseconds { get; set; }
    public long BackgroundMilliseconds { get; set; }
    public long DownloadBytes { get; set; }
    public long UploadBytes { get; set; }
    public long PeakDownloadBytesPerSecond { get; set; }
    public long PeakUploadBytesPerSecond { get; set; }
    public double ForegroundCpuTotal { get; set; }
    public double ForegroundWorkingSetTotal { get; set; }
    public int ForegroundSamples { get; set; }
    public double BackgroundCpuTotal { get; set; }
    public double BackgroundWorkingSetTotal { get; set; }
    public int BackgroundSamples { get; set; }
    public long PeakWorkingSetBytes { get; set; }
    public double ThreadCountTotal { get; set; }
    public int ThreadSamples { get; set; }
    public int PeakThreadCount { get; set; }
    public long IoReadBytes { get; set; }
    public long IoWriteBytes { get; set; }
    public long ForegroundIoOperations { get; set; }
    public long BackgroundIoOperations { get; set; }
    public long IoReadOperations { get; set; }
    public long IoWriteOperations { get; set; }
    public long PeakIoReadBytesPerSecond { get; set; }
    public long PeakIoWriteBytesPerSecond { get; set; }
    public long PeakIoBytesPerSecond { get; set; }
    public bool HasMainWindow { get; set; }

    public long TotalNetworkBytes => DownloadBytes + UploadBytes;
    public long TotalIoBytes => IoReadBytes + IoWriteBytes;
    public long TotalIoOperations => IoReadOperations + IoWriteOperations;
    public double AverageForegroundIops => ForegroundMilliseconds > 0 ? ForegroundIoOperations / (ForegroundMilliseconds / 1000d) : 0;
    public double AverageBackgroundIops => BackgroundMilliseconds > 0 ? BackgroundIoOperations / (BackgroundMilliseconds / 1000d) : 0;
    public double AverageIops => (ForegroundMilliseconds + BackgroundMilliseconds) > 0
        ? TotalIoOperations / ((ForegroundMilliseconds + BackgroundMilliseconds) / 1000d)
        : 0;
    public double AverageForegroundCpu => ForegroundSamples > 0 ? ForegroundCpuTotal / ForegroundSamples : 0;
    public double AverageForegroundWorkingSetBytes => ForegroundSamples > 0 ? ForegroundWorkingSetTotal / ForegroundSamples : 0;
    public double AverageBackgroundCpu => BackgroundSamples > 0 ? BackgroundCpuTotal / BackgroundSamples : 0;
    public double AverageBackgroundWorkingSetBytes => BackgroundSamples > 0 ? BackgroundWorkingSetTotal / BackgroundSamples : 0;
    public double AverageThreadCount => ThreadSamples > 0 ? ThreadCountTotal / ThreadSamples : 0;
}
