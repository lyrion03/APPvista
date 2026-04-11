using System.Diagnostics;
using System.Runtime.InteropServices;
using APPvista.Application.Abstractions;
using APPvista.Domain.Entities;

namespace APPvista.Infrastructure.Services;

public sealed class ProcessSnapshotProvider : IProcessSnapshotProvider
{
    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc enumProc, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint hWnd, uint command);

    private delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Thread32First(nint snapshot, ref ThreadEntry32 entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Thread32Next(nint snapshot, ref ThreadEntry32 entry);

    [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32First(nint snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", EntryPoint = "Process32NextW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32Next(nint snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessIoCounters(nint hProcess, out IoCounters ioCounters);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ProcessIdToSessionId(uint processId, out uint sessionId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(nint hProcess, int flags, System.Text.StringBuilder text, ref int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(
        nint hProcess,
        out long creationTime,
        out long exitTime,
        out long kernelTime,
        out long userTime);

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessMemoryInfo(nint hProcess, out ProcessMemoryCountersEx counters, uint size);

    [DllImport("psapi.dll", EntryPoint = "GetProcessMemoryInfo", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessMemoryInfoEx2(nint hProcess, out ProcessMemoryCountersEx2 counters, uint size);

    private readonly Dictionary<int, CpuSample> _cpuSamples = new();
    private readonly Dictionary<int, IoSample> _ioSamples = new();
    private readonly object _sync = new();
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint ProcessVmRead = 0x0010;
    private const uint Th32CsSnapThread = 0x00000004;
    private const uint Th32CsSnapProcess = 0x00000002;
    private const uint GwOwner = 4;
    private static readonly nint InvalidHandleValue = new(-1);

    public ProcessSnapshotBatch CaptureTopProcesses(int count, bool lightweight = false)
    {
        lock (_sync)
        {
            var now = DateTime.UtcNow;
            var foregroundPid = TryGetForegroundProcessId();
            var interactiveSessionId = Process.GetCurrentProcess().SessionId;
            var activePids = new HashSet<int>();
            var aggregated = new Dictionary<string, AggregatedProcessSample>(StringComparer.OrdinalIgnoreCase);
            var windowProcessIds = lightweight ? null : GetWindowProcessIds();
            var threadCountsByPid = lightweight ? null : GetThreadCountsByProcessId();
            var processNamesByPid = GetProcessNamesByProcessId();

            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    var appName = string.Empty;

                    try
                    {
                        var processId = process.Id;
                        var sessionId = TryGetSessionId(processId);
                        if (!sessionId.HasValue || sessionId.Value == 0)
                        {
                            continue;
                        }

                        if (!processNamesByPid.TryGetValue(processId, out var processName) || string.IsNullOrWhiteSpace(processName))
                        {
                            continue;
                        }

                        if (!ProcessMonitoringFilter.ShouldMonitor(processId, sessionId.Value, processName, interactiveSessionId))
                        {
                            continue;
                        }

                        activePids.Add(processId);
                        var executablePath = lightweight ? string.Empty : TryGetExecutablePath(processId);
                        appName = ApplicationIdentityResolver.Resolve(processName, executablePath);
                        var isForeground = processId == foregroundPid;
                        var cpuAvailable = lightweight;
                        var ioAvailable = lightweight;
                        var threadCountAvailable = lightweight;
                        var cpuUsage = lightweight ? 0d : GetCpuUsage(processId, now, out cpuAvailable);
                        var memoryUsageAvailable = lightweight
                            ? TryGetWorkingSetFast(processId, out var memoryUsage)
                            : TryGetMemoryUsage(processId, out memoryUsage);
                        var ioUsage = lightweight ? IoUsage.Empty : GetIoUsage(processId, now, out ioAvailable);
                        var hasMainWindow = !lightweight && (
                            (windowProcessIds is not null && windowProcessIds.Contains(processId)) ||
                            HasMainWindow(process));
                        var threadCount = lightweight ? 0 : TryGetThreadCount(processId, threadCountsByPid, out threadCountAvailable);
                        var isComplete = lightweight
                            ? memoryUsageAvailable
                            : cpuAvailable && memoryUsageAvailable && ioAvailable && threadCountAvailable;

                        if (!aggregated.TryGetValue(appName, out var aggregatedSample))
                        {
                            aggregatedSample = new AggregatedProcessSample
                            {
                                ProcessName = appName,
                                ProcessId = processId,
                                ExecutablePath = executablePath
                            };
                            aggregated[appName] = aggregatedSample;
                        }

                        aggregatedSample.ProcessId = isForeground ? processId : aggregatedSample.ProcessId;
                        aggregatedSample.ProcessCount += 1;
                        aggregatedSample.ExecutablePath = string.IsNullOrWhiteSpace(aggregatedSample.ExecutablePath)
                            ? executablePath
                            : aggregatedSample.ExecutablePath;
                        aggregatedSample.CpuUsagePercent += cpuUsage;
                        aggregatedSample.WorkingSetBytes += memoryUsage.WorkingSetBytes;
                        aggregatedSample.PrivateMemoryBytes += memoryUsage.PrivateMemoryBytes;
                        aggregatedSample.CommitSizeBytes += memoryUsage.CommitSizeBytes;
                        aggregatedSample.ThreadCount += threadCount;
                        aggregatedSample.RealtimeIoReadBytesPerSecond += ioUsage.ReadBytesPerSecond;
                        aggregatedSample.RealtimeIoWriteBytesPerSecond += ioUsage.WriteBytesPerSecond;
                        aggregatedSample.RealtimeIoReadOpsPerSecond += ioUsage.ReadOpsPerSecond;
                        aggregatedSample.RealtimeIoWriteOpsPerSecond += ioUsage.WriteOpsPerSecond;
                        aggregatedSample.IoReadBytesDelta += ioUsage.ReadBytesDelta;
                        aggregatedSample.IoWriteBytesDelta += ioUsage.WriteBytesDelta;
                        aggregatedSample.HasMainWindow |= hasMainWindow;
                        aggregatedSample.IsForeground |= isForeground;
                        aggregatedSample.HasIncompleteSample |= !isComplete;
                        aggregatedSample.ShouldInclude |= ShouldIncludeApplication(processId == foregroundPid, hasMainWindow, executablePath, cpuUsage, ioUsage);
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

            var snapshots = aggregated.Values
                .Where(static item => item.ShouldInclude)
                .Where(item =>
                {
                    if (!item.HasIncompleteSample)
                    {
                        return true;
                    }

                    incompleteProcessNames.Add(item.ProcessName);
                    return false;
                })
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

    private static bool TryGetMemoryUsage(int processId, out MemoryUsage memoryUsage)
    {
        memoryUsage = default;
        var processHandle = OpenProcess(ProcessQueryLimitedInformation | ProcessVmRead, inheritHandle: false, (uint)processId);
        if (processHandle == nint.Zero)
        {
            processHandle = OpenProcess(ProcessQueryLimitedInformation, inheritHandle: false, (uint)processId);
        }

        if (processHandle == nint.Zero)
        {
            return false;
        }

        try
        {
            if (GetProcessMemoryInfoEx2(processHandle, out ProcessMemoryCountersEx2 countersEx2, (uint)Marshal.SizeOf<ProcessMemoryCountersEx2>()))
            {
                var privateCommitBytes = (long)countersEx2.PrivateUsage;
                var totalCommitBytes = privateCommitBytes + (long)countersEx2.SharedCommitUsage;
                memoryUsage = new MemoryUsage(
                    (long)countersEx2.WorkingSetSize,
                    privateCommitBytes,
                    Math.Max(totalCommitBytes, privateCommitBytes));
                return true;
            }

            if (GetProcessMemoryInfo(processHandle, out ProcessMemoryCountersEx counters, (uint)Marshal.SizeOf<ProcessMemoryCountersEx>()))
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
        finally
        {
            CloseHandle(processHandle);
        }

        return false;
    }

    private static bool TryGetWorkingSetFast(int processId, out MemoryUsage memoryUsage)
    {
        memoryUsage = default;
        var processHandle = OpenProcess(ProcessQueryLimitedInformation | ProcessVmRead, inheritHandle: false, (uint)processId);
        if (processHandle == nint.Zero)
        {
            processHandle = OpenProcess(ProcessQueryLimitedInformation, inheritHandle: false, (uint)processId);
        }

        if (processHandle == nint.Zero)
        {
            return false;
        }

        try
        {
            if (GetProcessMemoryInfoEx2(processHandle, out ProcessMemoryCountersEx2 countersEx2, (uint)Marshal.SizeOf<ProcessMemoryCountersEx2>()))
            {
                memoryUsage = new MemoryUsage((long)countersEx2.WorkingSetSize, 0, 0);
                return true;
            }

            if (GetProcessMemoryInfo(processHandle, out ProcessMemoryCountersEx counters, (uint)Marshal.SizeOf<ProcessMemoryCountersEx>()))
            {
                memoryUsage = new MemoryUsage((long)counters.WorkingSetSize, 0, 0);
                return true;
            }

            return false;
        }
        finally
        {
            CloseHandle(processHandle);
        }
    }

    private double GetCpuUsage(int processId, DateTime now, out bool available)
    {
        available = false;
        var totalProcessorTime = TryGetTotalProcessorTime(processId);
        if (!totalProcessorTime.HasValue)
        {
            _cpuSamples.Remove(processId);
            return 0;
        }

        available = true;

        if (_cpuSamples.TryGetValue(processId, out var previous))
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

        _cpuSamples[processId] = new CpuSample
        {
            TotalProcessorTime = totalProcessorTime.Value,
            Timestamp = now
        };

        return 0;
    }

    private IoUsage GetIoUsage(int processId, DateTime now, out bool available)
    {
        available = false;
        if (!TryGetIoCounters(processId, out var ioSample))
        {
            return IoUsage.Empty;
        }

        available = true;

        if (_ioSamples.TryGetValue(processId, out var previous))
        {
            var ioDeltaSeconds = (now - previous.Timestamp).TotalSeconds;
            var readOpsDelta = Math.Max(0, ioSample.ReadOps - previous.ReadOps);
            var writeOpsDelta = Math.Max(0, ioSample.WriteOps - previous.WriteOps);
            var readBytesDelta = Math.Max(0, ioSample.ReadBytes - previous.ReadBytes);
            var writeBytesDelta = Math.Max(0, ioSample.WriteBytes - previous.WriteBytes);

            _ioSamples[processId] = ioSample;

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

        _ioSamples[processId] = ioSample;
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

    private static bool TryGetIoCounters(int processId, out IoSample ioSample)
    {
        ioSample = default;
        var processHandle = OpenProcess(ProcessQueryLimitedInformation, inheritHandle: false, (uint)processId);
        if (processHandle == nint.Zero)
        {
            return false;
        }

        try
        {
            if (!GetProcessIoCounters(processHandle, out var counters))
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
        finally
        {
            CloseHandle(processHandle);
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

    private static string TryGetExecutablePath(int processId)
    {
        var processHandle = OpenProcess(ProcessQueryLimitedInformation, inheritHandle: false, (uint)processId);
        if (processHandle == nint.Zero)
        {
            return string.Empty;
        }

        try
        {
            var size = 1024;
            var builder = new System.Text.StringBuilder(size);
            return QueryFullProcessImageName(processHandle, 0, builder, ref size)
                ? builder.ToString()
                : string.Empty;
        }
        finally
        {
            CloseHandle(processHandle);
        }
    }

    private static bool ShouldIncludeApplication(
        bool isForeground,
        bool hasMainWindow,
        string executablePath,
        double cpuUsagePercent,
        IoUsage ioUsage)
    {
        if (isForeground || hasMainWindow)
        {
            return true;
        }

        if (IsWindowsSystemProcess(executablePath))
        {
            return false;
        }

        return IsUserApplicationPath(executablePath) ||
               cpuUsagePercent >= 1d ||
               ioUsage.ReadBytesPerSecond + ioUsage.WriteBytesPerSecond > 0 ||
               ioUsage.ReadOpsPerSecond + ioUsage.WriteOpsPerSecond > 0;
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

    private static int? TryGetSessionId(int processId)
    {
        return ProcessIdToSessionId((uint)processId, out var sessionId)
            ? (int)sessionId
            : null;
    }

    private static int TryGetThreadCount(int processId, IReadOnlyDictionary<int, int>? threadCountsByPid, out bool available)
    {
        if (threadCountsByPid is not null && threadCountsByPid.TryGetValue(processId, out var threadCount))
        {
            available = true;
            return threadCount;
        }

        available = false;
        return 0;
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

    private static HashSet<int> GetWindowProcessIds()
    {
        var result = new HashSet<int>();
        var handle = GCHandle.Alloc(result);
        try
        {
            EnumWindows(static (hWnd, lParam) =>
            {
                if (!IsWindowVisible(hWnd) || GetWindow(hWnd, GwOwner) != nint.Zero)
                {
                    return true;
                }

                if (GetWindowThreadProcessId(hWnd, out var processId) > 0 && processId > 0)
                {
                    var targetHandle = GCHandle.FromIntPtr(lParam);
                    ((HashSet<int>)targetHandle.Target!).Add((int)processId);
                }

                return true;
            }, GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }

        return result;
    }

    private static Dictionary<int, int> GetThreadCountsByProcessId()
    {
        var result = new Dictionary<int, int>();
        var snapshot = CreateToolhelp32Snapshot(Th32CsSnapThread, 0);
        if (snapshot == InvalidHandleValue || snapshot == nint.Zero)
        {
            return result;
        }

        try
        {
            var entry = new ThreadEntry32
            {
                DwSize = (uint)Marshal.SizeOf<ThreadEntry32>()
            };

            if (!Thread32First(snapshot, ref entry))
            {
                return result;
            }

            do
            {
                var ownerPid = unchecked((int)entry.Th32OwnerProcessId);
                if (ownerPid <= 0)
                {
                    continue;
                }

                result.TryGetValue(ownerPid, out var count);
                result[ownerPid] = count + 1;
                entry.DwSize = (uint)Marshal.SizeOf<ThreadEntry32>();
            }
            while (Thread32Next(snapshot, ref entry));
        }
        finally
        {
            CloseHandle(snapshot);
        }

        return result;
    }

    private static TimeSpan? TryGetTotalProcessorTime(int processId)
    {
        var processHandle = OpenProcess(ProcessQueryLimitedInformation, inheritHandle: false, (uint)processId);
        if (processHandle == nint.Zero)
        {
            return null;
        }

        try
        {
            if (!GetProcessTimes(processHandle, out _, out _, out var kernelTime, out var userTime))
            {
                return null;
            }

            return TimeSpan.FromTicks(kernelTime + userTime);
        }
        finally
        {
            CloseHandle(processHandle);
        }
    }

    private static Dictionary<int, string> GetProcessNamesByProcessId()
    {
        var result = new Dictionary<int, string>();
        var snapshot = CreateToolhelp32Snapshot(Th32CsSnapProcess, 0);
        if (snapshot == InvalidHandleValue || snapshot == nint.Zero)
        {
            return result;
        }

        try
        {
            var entry = new ProcessEntry32
            {
                DwSize = (uint)Marshal.SizeOf<ProcessEntry32>()
            };

            if (!Process32First(snapshot, ref entry))
            {
                return result;
            }

            do
            {
                var processId = unchecked((int)entry.Th32ProcessId);
                if (processId > 0 && !string.IsNullOrWhiteSpace(entry.SzExeFile))
                {
                    result[processId] = Path.GetFileNameWithoutExtension(entry.SzExeFile) ?? entry.SzExeFile;
                }

                entry.DwSize = (uint)Marshal.SizeOf<ProcessEntry32>();
            }
            while (Process32Next(snapshot, ref entry));
        }
        finally
        {
            CloseHandle(snapshot);
        }

        return result;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct ThreadEntry32
    {
        public uint DwSize;
        public uint CntUsage;
        public uint Th32ThreadId;
        public uint Th32OwnerProcessId;
        public int TpBasePri;
        public int TpDeltaPri;
        public uint DwFlags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        public uint DwSize;
        public uint CntUsage;
        public uint Th32ProcessId;
        public nuint Th32DefaultHeapId;
        public uint Th32ModuleId;
        public uint CntThreads;
        public uint Th32ParentProcessID;
        public int PcPriClassBase;
        public uint DwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string SzExeFile;
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
        public bool HasIncompleteSample { get; set; }
        public bool ShouldInclude { get; set; }
    }

    private readonly record struct IoSample(long ReadOps, long WriteOps, long ReadBytes, long WriteBytes, DateTime Timestamp);
    private readonly record struct MemoryUsage(long WorkingSetBytes, long PrivateMemoryBytes, long CommitSizeBytes);
    private readonly record struct IoUsage(long ReadOpsPerSecond, long WriteOpsPerSecond, long ReadBytesPerSecond, long WriteBytesPerSecond, long ReadBytesDelta, long WriteBytesDelta)
    {
        public static IoUsage Empty => new(0, 0, 0, 0, 0, 0);
    }
}
