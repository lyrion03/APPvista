namespace APPvista.Domain.Entities;

public sealed class ProcessNetworkUsageEvent
{
    public string ProcessName { get; init; } = string.Empty;
    public string ExecutablePath { get; init; } = string.Empty;
    public long Bytes { get; init; }
    public bool IsDownload { get; init; }
    public DateTime Timestamp { get; init; }
}
