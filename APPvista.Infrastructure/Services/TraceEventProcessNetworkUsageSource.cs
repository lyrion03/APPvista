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

    private readonly ConcurrentQueue<ProcessNetworkUsageEvent> _queue = new();
    private readonly Dictionary<int, ProcessIdentity> _processIdentities = new();
    private readonly object _sync = new();
    private readonly string _sessionName = SessionNamePrefix;
    private readonly int _interactiveSessionId = Process.GetCurrentProcess().SessionId;

    private TraceEventSession? _session;
    private Task? _processingTask;
    private bool _disposed;

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

        CleanupProcessIdentities(DateTime.UtcNow);
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

        if (!TryResolveProcessIdentity(processId, out var processName, out var executablePath))
        {
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
                return true;
            }
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            if (!ProcessMonitoringFilter.ShouldMonitor(process, _interactiveSessionId))
            {
                processName = string.Empty;
                executablePath = string.Empty;
                return false;
            }

            executablePath = TryGetExecutablePath(process);
            processName = ApplicationIdentityResolver.Resolve(process.ProcessName, executablePath);

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
        }
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

    private sealed class ProcessIdentity
    {
        public string ProcessName { get; init; } = string.Empty;
        public string ExecutablePath { get; init; } = string.Empty;
        public DateTime LastSeenUtc { get; set; }
    }
}
