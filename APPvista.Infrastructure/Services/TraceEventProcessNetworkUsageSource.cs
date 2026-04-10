using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;
using APPvista.Application.Abstractions;
using APPvista.Domain.Entities;

namespace APPvista.Infrastructure.Services;

public sealed class TraceEventProcessNetworkUsageSource : IProcessNetworkUsageSource
{
    private const string SessionNamePrefix = "APPvista-Network";
    private static readonly TimeSpan RuntimeStatsLogInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan FilteredProcessCacheRetention = TimeSpan.FromSeconds(30);
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint Th32CsSnapProcess = 0x00000002;
    private static readonly nint InvalidHandleValue = new(-1);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool ProcessIdToSessionId(uint processId, out uint sessionId);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", EntryPoint = "Process32FirstW", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool Process32First(nint snapshot, ref ProcessEntry32 entry);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", EntryPoint = "Process32NextW", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool Process32Next(nint snapshot, ref ProcessEntry32 entry);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(nint hProcess, int flags, System.Text.StringBuilder text, ref int size);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint desiredAccess, [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint CreateToolhelp32Snapshot(uint flags, uint processId);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);

    private readonly ConcurrentQueue<ProcessNetworkUsageEvent> _queue = new();
    private readonly Dictionary<int, ProcessIdentity> _processIdentities = new();
    private readonly Dictionary<int, DateTime> _filteredProcessIds = new();
    private readonly object _sync = new();
    private readonly string _sessionName = SessionNamePrefix;
    private readonly int _interactiveSessionId = Process.GetCurrentProcess().SessionId;

    private TraceEventSession? _session;
    private Task? _processingTask;
    private bool _disposed;
    private DateTime _lastRuntimeStatsLoggedUtc = DateTime.UtcNow;
    private long _eventsObserved;
    private long _eventsEnqueued;
    private long _bytesObserved;
    private long _bytesEnqueued;
    private long _identityCacheHits;
    private long _identityResolves;
    private long _identityResolveFailures;
    private long _identityFiltered;
    private long _identityFilteredCacheHits;
    private long _eventsDrained;
    private long _drains;

    public TraceEventProcessNetworkUsageSource()
    {
        Status = "网络采集初始化中";
        Start();
    }

    public string Status { get; private set; }

    public IReadOnlyList<ProcessNetworkUsageEvent> DrainPendingEvents()
    {
        var items = new List<ProcessNetworkUsageEvent>();
        while (_queue.TryDequeue(out var item))
        {
            items.Add(item);
        }

        if (items.Count > 0)
        {
            Interlocked.Add(ref _eventsDrained, items.Count);
        }

        Interlocked.Increment(ref _drains);

        CleanupProcessIdentities(DateTime.UtcNow);
        LogRuntimeStatsIfDue(DateTime.UtcNow);
        return items;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            _session?.Dispose();
            _processingTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
        }
    }

    private void Start()
    {
        try
        {
            if (TraceEventSession.IsElevated() != true)
            {
                Status = "网络采集需要管理员权限";
                return;
            }

            CleanupOrphanSessions();
        }
        catch (Exception ex)
        {
            Status = $"网络采集权限检查失败: {ex.Message}";
            return;
        }

        _processingTask = Task.Run(() =>
        {
            try
            {
                using var session = new TraceEventSession(_sessionName);
                _session = session;
                session.StopOnDispose = true;
                session.EnableKernelProvider(
                    KernelTraceEventParser.Keywords.NetworkTCPIP,
                    KernelTraceEventParser.Keywords.None);

                session.Source.Kernel.TcpIpRecv += data => Enqueue(data.ProcessID, data.size, isDownload: true);
                session.Source.Kernel.TcpIpSend += data => Enqueue(data.ProcessID, data.size, isDownload: false);
                session.Source.Kernel.TcpIpRecvIPV6 += data => Enqueue(data.ProcessID, data.size, isDownload: true);
                session.Source.Kernel.TcpIpSendIPV6 += data => Enqueue(data.ProcessID, data.size, isDownload: false);
                session.Source.Kernel.UdpIpRecv += data => Enqueue(data.ProcessID, data.size, isDownload: true);
                session.Source.Kernel.UdpIpSend += data => Enqueue(data.ProcessID, data.size, isDownload: false);
                session.Source.Kernel.UdpIpRecvIPV6 += data => Enqueue(data.ProcessID, data.size, isDownload: true);
                session.Source.Kernel.UdpIpSendIPV6 += data => Enqueue(data.ProcessID, data.size, isDownload: false);

                Status = "网络采集运行中";
                session.Source.Process();
            }
            catch (Exception ex)
            {
                Status = $"网络采集不可用: {ex.Message}";
            }
            finally
            {
                _session = null;
            }
        });
    }

    private static void CleanupOrphanSessions()
    {
        foreach (var activeSessionName in TraceEventSession.GetActiveSessionNames()
                     .Where(name => name.StartsWith(SessionNamePrefix, StringComparison.OrdinalIgnoreCase))
                     .ToArray())
        {
            try
            {
                using var activeSession = TraceEventSession.GetActiveSession(activeSessionName);
                activeSession?.Stop(true);
            }
            catch
            {
            }
        }
    }

    private void Enqueue(int processId, long bytes, bool isDownload)
    {
        if (_disposed || processId <= 0 || bytes <= 0)
        {
            return;
        }

        Interlocked.Increment(ref _eventsObserved);
        Interlocked.Add(ref _bytesObserved, bytes);

        if (!TryResolveProcessIdentity(processId, out var processName, out var executablePath))
        {
            LogRuntimeStatsIfDue(DateTime.UtcNow);
            return;
        }

        _queue.Enqueue(new ProcessNetworkUsageEvent
        {
            ProcessName = processName,
            ExecutablePath = executablePath,
            Bytes = bytes,
            IsDownload = isDownload,
            Timestamp = DateTime.Now
        });

        Interlocked.Increment(ref _eventsEnqueued);
        Interlocked.Add(ref _bytesEnqueued, bytes);
        LogRuntimeStatsIfDue(DateTime.UtcNow);
    }

    private bool TryResolveProcessIdentity(int processId, out string processName, out string executablePath)
    {
        lock (_sync)
        {
            if (_processIdentities.TryGetValue(processId, out var cached))
            {
                cached.LastSeenUtc = DateTime.UtcNow;
                processName = cached.ProcessName;
                executablePath = cached.ExecutablePath;
                Interlocked.Increment(ref _identityCacheHits);
                return true;
            }

            if (_filteredProcessIds.TryGetValue(processId, out var filteredAtUtc) &&
                DateTime.UtcNow - filteredAtUtc <= FilteredProcessCacheRetention)
            {
                processName = string.Empty;
                executablePath = string.Empty;
                Interlocked.Increment(ref _identityFilteredCacheHits);
                return false;
            }
        }

        try
        {
            var sessionId = TryGetSessionId(processId);
            executablePath = TryGetExecutablePath(processId);
            var rawProcessName = TryGetProcessName(processId, executablePath);
            if (!sessionId.HasValue || string.IsNullOrWhiteSpace(rawProcessName) || !ProcessMonitoringFilter.ShouldMonitor(processId, sessionId.Value, rawProcessName, _interactiveSessionId))
            {
                processName = string.Empty;
                executablePath = string.Empty;
                Interlocked.Increment(ref _identityFiltered);

                lock (_sync)
                {
                    _filteredProcessIds[processId] = DateTime.UtcNow;
                }

                return false;
            }

            processName = ApplicationIdentityResolver.Resolve(rawProcessName, executablePath);
            Interlocked.Increment(ref _identityResolves);

            lock (_sync)
            {
                _processIdentities[processId] = new ProcessIdentity
                {
                    ProcessName = processName,
                    ExecutablePath = executablePath,
                    LastSeenUtc = DateTime.UtcNow
                };
            }

            return true;
        }
        catch
        {
            processName = string.Empty;
            executablePath = string.Empty;
            Interlocked.Increment(ref _identityResolveFailures);
            return false;
        }
    }

    private void CleanupProcessIdentities(DateTime nowUtc)
    {
        lock (_sync)
        {
            foreach (var pid in _processIdentities
                         .Where(pair => (nowUtc - pair.Value.LastSeenUtc).TotalMinutes > 10)
                         .Select(pair => pair.Key)
                         .ToArray())
            {
                _processIdentities.Remove(pid);
            }

            foreach (var pid in _filteredProcessIds
                         .Where(pair => nowUtc - pair.Value > FilteredProcessCacheRetention)
                         .Select(pair => pair.Key)
                         .ToArray())
            {
                _filteredProcessIds.Remove(pid);
            }
        }
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

    private static int? TryGetSessionId(int processId)
    {
        return ProcessIdToSessionId((uint)processId, out var sessionId)
            ? (int)sessionId
            : null;
    }

    private static string TryGetProcessName(int processId, string executablePath)
    {
        if (!string.IsNullOrWhiteSpace(executablePath))
        {
            return Path.GetFileNameWithoutExtension(executablePath) ?? string.Empty;
        }

        var snapshot = CreateToolhelp32Snapshot(Th32CsSnapProcess, 0);
        if (snapshot == InvalidHandleValue || snapshot == nint.Zero)
        {
            return string.Empty;
        }

        try
        {
            var entry = new ProcessEntry32
            {
                DwSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<ProcessEntry32>()
            };

            if (!Process32First(snapshot, ref entry))
            {
                return string.Empty;
            }

            do
            {
                if (entry.Th32ProcessId == (uint)processId)
                {
                    return Path.GetFileNameWithoutExtension(entry.SzExeFile) ?? entry.SzExeFile ?? string.Empty;
                }

                entry.DwSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<ProcessEntry32>();
            }
            while (Process32Next(snapshot, ref entry));

            return string.Empty;
        }
        finally
        {
            CloseHandle(snapshot);
        }
    }

    private void LogRuntimeStatsIfDue(DateTime nowUtc)
    {
        if (nowUtc - _lastRuntimeStatsLoggedUtc < RuntimeStatsLogInterval)
        {
            return;
        }

        lock (_sync)
        {
            if (nowUtc - _lastRuntimeStatsLoggedUtc < RuntimeStatsLogInterval)
            {
                return;
            }

            _lastRuntimeStatsLoggedUtc = nowUtc;
            StartupPerformanceTrace.MarkRuntime(
                $"NetworkTrace status=\"{Status}\" observed={Interlocked.Read(ref _eventsObserved)} enqueued={Interlocked.Read(ref _eventsEnqueued)} drained={Interlocked.Read(ref _eventsDrained)} drains={Interlocked.Read(ref _drains)} bytes_observed={Interlocked.Read(ref _bytesObserved)} bytes_enqueued={Interlocked.Read(ref _bytesEnqueued)} identity_cache_hits={Interlocked.Read(ref _identityCacheHits)} identity_filtered_cache_hits={Interlocked.Read(ref _identityFilteredCacheHits)} identity_resolves={Interlocked.Read(ref _identityResolves)} identity_filtered={Interlocked.Read(ref _identityFiltered)} identity_failures={Interlocked.Read(ref _identityResolveFailures)} queue_len={_queue.Count} identity_cache_size={_processIdentities.Count} filtered_pid_cache_size={_filteredProcessIds.Count}");
        }
    }

    private sealed class ProcessIdentity
    {
        public string ProcessName { get; init; } = string.Empty;
        public string ExecutablePath { get; init; } = string.Empty;
        public DateTime LastSeenUtc { get; set; }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
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

        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 260)]
        public string SzExeFile;
    }
}
