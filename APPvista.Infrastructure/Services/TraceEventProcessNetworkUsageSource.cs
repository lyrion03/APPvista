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
            using var process = Process.GetProcessById(processId);
            if (!ProcessMonitoringFilter.ShouldMonitor(process, _interactiveSessionId))
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

            executablePath = TryGetExecutablePath(process);
            processName = ApplicationIdentityResolver.Resolve(process.ProcessName, executablePath);
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
}
