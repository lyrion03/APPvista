using System.Diagnostics;
using System.Text;

namespace APPvista.Infrastructure.Services;

public static class StartupPerformanceTrace
{
    private static readonly object Sync = new();
    private static readonly double TickToMilliseconds = 1000d / Stopwatch.Frequency;
    private static readonly long ProcessStartTimestamp = Stopwatch.GetTimestamp();
    private static string? _logFilePath;
    private static string? _runtimeLogFilePath;
    private static bool _enableStartupLog;
    private static bool _enableRuntimeLog;

    public static void Initialize(string directoryPath, bool enableStartupLog, bool enableRuntimeLog)
    {
        lock (Sync)
        {
            Directory.CreateDirectory(directoryPath);
            _enableStartupLog = enableStartupLog;
            _enableRuntimeLog = enableRuntimeLog;
            _logFilePath = enableStartupLog ? Path.Combine(directoryPath, "startup-performance.log") : null;
            _runtimeLogFilePath = enableRuntimeLog ? Path.Combine(directoryPath, "runtime-performance.log") : null;

            if (enableStartupLog)
            {
                AppendLine($"==== Session {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ====");
                AppendLine($"process_start_ms=0.000 pid={Environment.ProcessId}");
            }

            if (enableRuntimeLog)
            {
                AppendRuntimeLine($"==== Session {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ====");
                AppendRuntimeLine($"process_start_ms=0.000 pid={Environment.ProcessId}");
            }
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

    public static void MarkRuntime(string message)
    {
        lock (Sync)
        {
            AppendRuntimeLine($"{GetElapsedMilliseconds():F3} ms | {message}");
        }
    }

    private static double GetElapsedMilliseconds()
    {
        return (Stopwatch.GetTimestamp() - ProcessStartTimestamp) * TickToMilliseconds;
    }

    private static void AppendLine(string line)
    {
        if (!_enableStartupLog || string.IsNullOrWhiteSpace(_logFilePath))
        {
            return;
        }

        File.AppendAllText(_logFilePath, line + Environment.NewLine, Encoding.UTF8);
    }

    private static void AppendRuntimeLine(string line)
    {
        if (!_enableRuntimeLog || string.IsNullOrWhiteSpace(_runtimeLogFilePath))
        {
            return;
        }

        File.AppendAllText(_runtimeLogFilePath, line + Environment.NewLine, Encoding.UTF8);
    }
}
