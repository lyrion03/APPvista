using System.Diagnostics;
using System.Text;

namespace APPvista.Infrastructure.Services;

public static class StartupPerformanceTrace
{
    private static readonly object Sync = new();
    private static readonly double TickToMilliseconds = 1000d / Stopwatch.Frequency;
    private static readonly long ProcessStartTimestamp = Stopwatch.GetTimestamp();
    private static string? _logFilePath;

    public static void Initialize(string directoryPath)
    {
        lock (Sync)
        {
            Directory.CreateDirectory(directoryPath);
            _logFilePath = Path.Combine(directoryPath, "startup-performance.log");
            AppendLine($"==== Session {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ====");
            AppendLine($"process_start_ms=0.000 pid={Environment.ProcessId}");
        }
    }

    public static void Mark(string message)
    {
        lock (Sync)
        {
            AppendLine($"{GetElapsedMilliseconds():F3} ms | {message}");
        }
    }

    public static void MarkDuration(string message, long startedTimestamp)
    {
        lock (Sync)
        {
            var elapsed = (Stopwatch.GetTimestamp() - startedTimestamp) * TickToMilliseconds;
            AppendLine($"{GetElapsedMilliseconds():F3} ms | {message} | duration={elapsed:F3} ms");
        }
    }

    private static double GetElapsedMilliseconds()
    {
        return (Stopwatch.GetTimestamp() - ProcessStartTimestamp) * TickToMilliseconds;
    }

    private static void AppendLine(string line)
    {
        if (string.IsNullOrWhiteSpace(_logFilePath))
        {
            return;
        }

        File.AppendAllText(_logFilePath, line + Environment.NewLine, Encoding.UTF8);
    }
}
