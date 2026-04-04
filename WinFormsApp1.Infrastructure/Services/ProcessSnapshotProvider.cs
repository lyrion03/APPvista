using System.Diagnostics;
using System.Runtime.InteropServices;
using WinFormsApp1.Application.Abstractions;
using WinFormsApp1.Domain.Entities;

namespace WinFormsApp1.Infrastructure.Services;

public sealed class ProcessSnapshotProvider : IProcessSnapshotProvider
{
    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessIoCounters(nint hProcess, out IoCounters ioCounters);

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessMemoryInfo(nint hProcess, out ProcessMemoryCountersEx counters, uint size);

    [DllImport("psapi.dll", EntryPoint = "GetProcessMemoryInfo", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessMemoryInfoEx2(nint hProcess, out ProcessMemoryCountersEx2 counters, uint size);

    private readonly Dictionary<int, CpuSample> _cpuSamples = new();
    private readonly Dictionary<int, IoSample> _ioSamples = new();
    private readonly object _sync = new();

    public ProcessSnapshotBatch CaptureTopProcesses(int count)
    {
        lock (_sync)
        {
            var now = DateTime.UtcNow;
            var foregroundPid = TryGetForegroundProcessId();
            var interactiveSessionId = Process.GetCurrentProcess().SessionId;
            var activePids = new HashSet<int>();
            var processSamples = new List<ProcessCandidateSample>();

            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    var appName = string.Empty;

                    try
                    {
                        var sessionId = TryGetSessionId(process);
                        if (!sessionId.HasValue || sessionId.Value == 0)
                        {
                            continue;
                        }

                        if (!ProcessMonitoringFilter.ShouldMonitor(process, interactiveSessionId))
                        {
                            continue;
                        }

                        activePids.Add(process.Id);

                        var processName = TryGetProcessName(process);
                        if (string.IsNullOrWhiteSpace(processName))
                        {
                            continue;
                        }

                        var executablePath = TryGetExecutablePath(process);
                        appName = ApplicationIdentityResolver.Resolve(processName, executablePath);
                        var isForeground = process.Id == foregroundPid;
                        var cpuUsage = GetCpuUsage(process, now, out var cpuAvailable);
                        var memoryUsageAvailable = TryGetMemoryUsage(process, out var memoryUsage);
                        var ioUsage = GetIoUsage(process, now, out var ioAvailable);
                        var hasMainWindow = HasMainWindow(process);
                        var threadCount = TryGetThreadCount(process, out var threadCountAvailable);

                        processSamples.Add(new ProcessCandidateSample
                        {
                            ProcessName = appName,
                            ProcessId = process.Id,
                            ExecutablePath = executablePath,
                            IsForeground = isForeground,
                            HasMainWindow = hasMainWindow,
                            CpuUsagePercent = cpuUsage,
                            IoUsage = ioUsage,
                            MemoryUsage = memoryUsage,
                            ThreadCount = threadCount,
                            IsComplete = cpuAvailable && memoryUsageAvailable && ioAvailable && threadCountAvailable
                        });
                    }
                    catch
                    {
                        // Ignore transient process access failures. Only successfully discovered
                        // process identities participate in group aggregation.
                    }
                }
            }

            CleanupSamples(activePids);

            var incompleteProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var aggregated = new Dictionary<string, AggregatedProcessSample>(StringComparer.OrdinalIgnoreCase);

            foreach (var group in processSamples.GroupBy(item => item.ProcessName, StringComparer.OrdinalIgnoreCase))
            {
                if (!ShouldIncludeApplicationGroup(group))
                {
                    continue;
                }

                if (group.Any(item => !item.IsComplete))
                {
                    incompleteProcessNames.Add(group.Key);
                    continue;
                }

                AggregatedProcessSample? aggregatedSample = null;
                foreach (var sample in group)
                {
                    aggregatedSample ??= new AggregatedProcessSample
                    {
                        ProcessName = sample.ProcessName,
                        ProcessId = sample.ProcessId,
                        ProcessCount = 0,
                        ExecutablePath = sample.ExecutablePath
                    };

                    aggregatedSample.ProcessId = sample.IsForeground ? sample.ProcessId : aggregatedSample.ProcessId;
                    aggregatedSample.ProcessCount += 1;
                    aggregatedSample.ExecutablePath = string.IsNullOrWhiteSpace(aggregatedSample.ExecutablePath) ? sample.ExecutablePath : aggregatedSample.ExecutablePath;
                    aggregatedSample.CpuUsagePercent += sample.CpuUsagePercent;
                    aggregatedSample.WorkingSetBytes += sample.MemoryUsage.WorkingSetBytes;
                    aggregatedSample.PrivateMemoryBytes += sample.MemoryUsage.PrivateMemoryBytes;
                    aggregatedSample.CommitSizeBytes += sample.MemoryUsage.CommitSizeBytes;
                    aggregatedSample.ThreadCount += sample.ThreadCount;
                    aggregatedSample.RealtimeIoReadBytesPerSecond += sample.IoUsage.ReadBytesPerSecond;
                    aggregatedSample.RealtimeIoWriteBytesPerSecond += sample.IoUsage.WriteBytesPerSecond;
                    aggregatedSample.RealtimeIoReadOpsPerSecond += sample.IoUsage.ReadOpsPerSecond;
                    aggregatedSample.RealtimeIoWriteOpsPerSecond += sample.IoUsage.WriteOpsPerSecond;
                    aggregatedSample.IoReadBytesDelta += sample.IoUsage.ReadBytesDelta;
                    aggregatedSample.IoWriteBytesDelta += sample.IoUsage.WriteBytesDelta;
                    aggregatedSample.HasMainWindow |= sample.HasMainWindow;
                    aggregatedSample.IsForeground |= sample.IsForeground;
                }

                if (aggregatedSample is not null)
                {
                    aggregated[group.Key] = aggregatedSample;
                }
            }

            var snapshots = aggregated.Values
                .Select(item => new ProcessResourceSnapshot
                {
                    ProcessName = item.ProcessName,
                    ProcessId = item.ProcessId,
                    ProcessCount = item.ProcessCount,
                    ExecutablePath = item.ExecutablePath,
                    CpuUsagePercent = item.CpuUsagePercent,
                    WorkingSetBytes = item.WorkingSetBytes,
                    PrivateMemoryBytes = item.PrivateMemoryBytes,
                    CommitSizeBytes = item.CommitSizeBytes,
                    ThreadCount = item.ThreadCount,
                    RealtimeIoReadBytesPerSecond = item.RealtimeIoReadBytesPerSecond,
                    RealtimeIoWriteBytesPerSecond = item.RealtimeIoWriteBytesPerSecond,
                    RealtimeIoReadOpsPerSecond = item.RealtimeIoReadOpsPerSecond,
                    RealtimeIoWriteOpsPerSecond = item.RealtimeIoWriteOpsPerSecond,
                    IoReadBytesDelta = item.IoReadBytesDelta,
                    IoWriteBytesDelta = item.IoWriteBytesDelta,
                    HasMainWindow = item.HasMainWindow,
                    IsForeground = item.IsForeground
                })
                .OrderByDescending(item => item.IsForeground)
                .ThenByDescending(item => item.CpuUsagePercent)
                .ThenByDescending(item => item.RealtimeIoReadBytesPerSecond + item.RealtimeIoWriteBytesPerSecond)
                .ThenByDescending(item => item.WorkingSetBytes)
                .Take(count)
                .ToList();

            return new ProcessSnapshotBatch
            {
                Processes = snapshots,
                IncompleteProcessNames = incompleteProcessNames.ToArray()
            };
        }
    }

    private static bool TryGetMemoryUsage(Process process, out MemoryUsage memoryUsage)
    {
        memoryUsage = default;

        try
        {
            if (GetProcessMemoryInfoEx2(process.Handle, out ProcessMemoryCountersEx2 countersEx2, (uint)Marshal.SizeOf<ProcessMemoryCountersEx2>()))
            {
                var privateCommitBytes = (long)countersEx2.PrivateUsage;
                var totalCommitBytes = privateCommitBytes + (long)countersEx2.SharedCommitUsage;
                memoryUsage = new MemoryUsage(
                    (long)countersEx2.WorkingSetSize,
                    privateCommitBytes,
                    Math.Max(totalCommitBytes, privateCommitBytes));
                return true;
            }

            if (GetProcessMemoryInfo(process.Handle, out ProcessMemoryCountersEx counters, (uint)Marshal.SizeOf<ProcessMemoryCountersEx>()))
            {
                memoryUsage = new MemoryUsage(
                    (long)counters.WorkingSetSize,
                    (long)counters.PrivateUsage,
                    Math.Max((long)counters.PagefileUsage, (long)counters.PrivateUsage));
                return true;
            }
        }
        catch
        {
        }

        var workingSetAvailable = TryGetProcessLong(process, static item => item.WorkingSet64, out var workingSetBytes);
        var privateAvailable = TryGetProcessLong(process, static item => item.PrivateMemorySize64, out var privateMemoryBytes);
        var commitAvailable = TryGetProcessLong(process, static item => item.PagedMemorySize64, out var commitSizeBytes);
        if (!workingSetAvailable || !privateAvailable || !commitAvailable)
        {
            return false;
        }

        memoryUsage = new MemoryUsage(
            workingSetBytes,
            privateMemoryBytes,
            Math.Max(commitSizeBytes, privateMemoryBytes));
        return true;
    }

    private double GetCpuUsage(Process process, DateTime now, out bool available)
    {
        available = false;
        var totalProcessorTime = TryGetTotalProcessorTime(process);
        if (!totalProcessorTime.HasValue)
        {
            _cpuSamples.Remove(process.Id);
            return 0;
        }

        available = true;

        if (_cpuSamples.TryGetValue(process.Id, out var previous))
        {
            var cpuDelta = (totalProcessorTime.Value - previous.TotalProcessorTime).TotalMilliseconds;
            var timeDelta = (now - previous.Timestamp).TotalMilliseconds;

            previous.TotalProcessorTime = totalProcessorTime.Value;
            previous.Timestamp = now;

            if (timeDelta <= 0)
            {
                return 0;
            }

            var cpu = cpuDelta / timeDelta / Environment.ProcessorCount * 100d;
            return Math.Max(0, Math.Round(cpu, 1, MidpointRounding.AwayFromZero));
        }

        _cpuSamples[process.Id] = new CpuSample
        {
            TotalProcessorTime = totalProcessorTime.Value,
            Timestamp = now
        };

        return 0;
    }

    private IoUsage GetIoUsage(Process process, DateTime now, out bool available)
    {
        available = false;
        if (!TryGetIoCounters(process, out var ioSample))
        {
            return IoUsage.Empty;
        }

        available = true;

        if (_ioSamples.TryGetValue(process.Id, out var previous))
        {
            var ioDeltaSeconds = (now - previous.Timestamp).TotalSeconds;
            var readOpsDelta = Math.Max(0, ioSample.ReadOps - previous.ReadOps);
            var writeOpsDelta = Math.Max(0, ioSample.WriteOps - previous.WriteOps);
            var readBytesDelta = Math.Max(0, ioSample.ReadBytes - previous.ReadBytes);
            var writeBytesDelta = Math.Max(0, ioSample.WriteBytes - previous.WriteBytes);

            _ioSamples[process.Id] = ioSample;

            if (ioDeltaSeconds <= 0)
            {
                return new IoUsage(0, 0, 0, 0, readBytesDelta, writeBytesDelta);
            }

            return new IoUsage(
                Math.Max(0, (long)Math.Round(readOpsDelta / ioDeltaSeconds, MidpointRounding.AwayFromZero)),
                Math.Max(0, (long)Math.Round(writeOpsDelta / ioDeltaSeconds, MidpointRounding.AwayFromZero)),
                Math.Max(0, (long)Math.Round(readBytesDelta / ioDeltaSeconds, MidpointRounding.AwayFromZero)),
                Math.Max(0, (long)Math.Round(writeBytesDelta / ioDeltaSeconds, MidpointRounding.AwayFromZero)),
                readBytesDelta,
                writeBytesDelta);
        }

        _ioSamples[process.Id] = ioSample;
        return IoUsage.Empty;
    }

    private void CleanupSamples(HashSet<int> activePids)
    {
        foreach (var pid in _cpuSamples.Keys.Where(pid => !activePids.Contains(pid)).ToArray())
        {
            _cpuSamples.Remove(pid);
        }

        foreach (var pid in _ioSamples.Keys.Where(pid => !activePids.Contains(pid)).ToArray())
        {
            _ioSamples.Remove(pid);
        }
    }

    private static bool TryGetIoCounters(Process process, out IoSample ioSample)
    {
        ioSample = default;

        try
        {
            if (!GetProcessIoCounters(process.Handle, out var counters))
            {
                return false;
            }

            ioSample = new IoSample(
                (long)counters.ReadOperationCount,
                (long)counters.WriteOperationCount,
                (long)counters.ReadTransferCount,
                (long)counters.WriteTransferCount,
                DateTime.UtcNow);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static int? TryGetForegroundProcessId()
    {
        var handle = GetForegroundWindow();
        if (handle == nint.Zero)
        {
            return null;
        }

        GetWindowThreadProcessId(handle, out var processId);
        return processId > 0 ? (int)processId : null;
    }

    private static string TryGetExecutablePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string TryGetProcessName(Process process)
    {
        try
        {
            return process.ProcessName ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool ShouldIncludeApplicationGroup(IEnumerable<ProcessCandidateSample> group)
    {
        foreach (var sample in group)
        {
            if (sample.IsForeground || sample.HasMainWindow)
            {
                return true;
            }

            if (IsWindowsSystemProcess(sample.ExecutablePath))
            {
                continue;
            }

            if (IsUserApplicationPath(sample.ExecutablePath))
            {
                return true;
            }

            if (sample.CpuUsagePercent >= 1d ||
                sample.IoUsage.ReadBytesPerSecond + sample.IoUsage.WriteBytesPerSecond > 0 ||
                sample.IoUsage.ReadOpsPerSecond + sample.IoUsage.WriteOpsPerSecond > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsWindowsSystemProcess(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrWhiteSpace(windowsDirectory))
        {
            return false;
        }

        return executablePath.StartsWith(windowsDirectory, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUserApplicationPath(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userProfile))
        {
            return false;
        }

        return executablePath.StartsWith(userProfile, StringComparison.OrdinalIgnoreCase);
    }

    private static int? TryGetSessionId(Process process)
    {
        try
        {
            return process.SessionId;
        }
        catch
        {
            return null;
        }
    }

    private static bool HasMainWindow(Process process)
    {
        try
        {
            return process.MainWindowHandle != nint.Zero;
        }
        catch
        {
            return false;
        }
    }

    private static int TryGetThreadCount(Process process, out bool available)
    {
        available = false;

        try
        {
            available = true;
            return process.Threads.Count;
        }
        catch
        {
            return 0;
        }
    }

    private static TimeSpan? TryGetTotalProcessorTime(Process process)
    {
        try
        {
            return process.TotalProcessorTime;
        }
        catch
        {
            return null;
        }
    }

    private static bool TryGetProcessLong(Process process, Func<Process, long> selector, out long value)
    {
        value = 0;

        try
        {
            value = selector(process);
            return true;
        }
        catch
        {
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessMemoryCountersEx
    {
        public uint Cb;
        public uint PageFaultCount;
        public nuint PeakWorkingSetSize;
        public nuint WorkingSetSize;
        public nuint QuotaPeakPagedPoolUsage;
        public nuint QuotaPagedPoolUsage;
        public nuint QuotaPeakNonPagedPoolUsage;
        public nuint QuotaNonPagedPoolUsage;
        public nuint PagefileUsage;
        public nuint PeakPagefileUsage;
        public nuint PrivateUsage;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessMemoryCountersEx2
    {
        public uint Cb;
        public uint PageFaultCount;
        public nuint PeakWorkingSetSize;
        public nuint WorkingSetSize;
        public nuint QuotaPeakPagedPoolUsage;
        public nuint QuotaPagedPoolUsage;
        public nuint QuotaPeakNonPagedPoolUsage;
        public nuint QuotaNonPagedPoolUsage;
        public nuint PagefileUsage;
        public nuint PeakPagefileUsage;
        public nuint PrivateUsage;
        public nuint PrivateWorkingSetSize;
        public nuint SharedCommitUsage;
    }

    private sealed class CpuSample
    {
        public TimeSpan TotalProcessorTime { get; set; }
        public DateTime Timestamp { get; set; }
    }

    private sealed class AggregatedProcessSample
    {
        public string ProcessName { get; set; } = string.Empty;
        public int ProcessId { get; set; }
        public int ProcessCount { get; set; }
        public string ExecutablePath { get; set; } = string.Empty;
        public double CpuUsagePercent { get; set; }
        public long WorkingSetBytes { get; set; }
        public long PrivateMemoryBytes { get; set; }
        public long CommitSizeBytes { get; set; }
        public int ThreadCount { get; set; }
        public long RealtimeIoReadOpsPerSecond { get; set; }
        public long RealtimeIoWriteOpsPerSecond { get; set; }
        public long RealtimeIoReadBytesPerSecond { get; set; }
        public long RealtimeIoWriteBytesPerSecond { get; set; }
        public long IoReadBytesDelta { get; set; }
        public long IoWriteBytesDelta { get; set; }
        public bool HasMainWindow { get; set; }
        public bool IsForeground { get; set; }
    }

    private sealed class ProcessCandidateSample
    {
        public string ProcessName { get; init; } = string.Empty;
        public int ProcessId { get; init; }
        public string ExecutablePath { get; init; } = string.Empty;
        public bool IsForeground { get; init; }
        public bool HasMainWindow { get; init; }
        public double CpuUsagePercent { get; init; }
        public MemoryUsage MemoryUsage { get; init; }
        public IoUsage IoUsage { get; init; }
        public int ThreadCount { get; init; }
        public bool IsComplete { get; init; }
    }

    private readonly record struct IoSample(long ReadOps, long WriteOps, long ReadBytes, long WriteBytes, DateTime Timestamp);
    private readonly record struct MemoryUsage(long WorkingSetBytes, long PrivateMemoryBytes, long CommitSizeBytes);
    private readonly record struct IoUsage(long ReadOpsPerSecond, long WriteOpsPerSecond, long ReadBytesPerSecond, long WriteBytesPerSecond, long ReadBytesDelta, long WriteBytesDelta)
    {
        public static IoUsage Empty => new(0, 0, 0, 0, 0, 0);
    }
}
