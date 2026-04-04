using APPvista.Application.Abstractions;
using APPvista.Domain.Entities;

namespace APPvista.Infrastructure.Services;

public sealed class DeferredProcessNetworkUsageSource : IProcessNetworkUsageSource
{
    private readonly Func<IProcessNetworkUsageSource> _factory;
    private readonly object _sync = new();
    private IProcessNetworkUsageSource? _inner;
    private Task? _initializationTask;
    private bool _disposed;

    public DeferredProcessNetworkUsageSource(Func<IProcessNetworkUsageSource> factory)
    {
        _factory = factory;
    }

    public string Status
    {
        get
        {
            lock (_sync)
            {
                if (_inner is not null)
                {
                    return _inner.Status;
                }

                EnsureInitializationStarted();
                return "网络采集初始化中";
            }
        }
    }

    public IReadOnlyList<ProcessNetworkUsageEvent> DrainPendingEvents()
    {
        IProcessNetworkUsageSource? inner;
        lock (_sync)
        {
            inner = _inner;
            if (inner is null)
            {
                EnsureInitializationStarted();
                return Array.Empty<ProcessNetworkUsageEvent>();
            }
        }

        return inner.DrainPendingEvents();
    }

    public void Dispose()
    {
        IProcessNetworkUsageSource? innerToDispose = null;

        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            innerToDispose = _inner;
            _inner = null;
        }

        innerToDispose?.Dispose();
    }

    private void EnsureInitializationStarted()
    {
        if (_disposed || _inner is not null || _initializationTask is not null)
        {
            return;
        }

        _initializationTask = Task.Run(() =>
        {
            IProcessNetworkUsageSource? created = null;
            try
            {
                created = _factory();

                lock (_sync)
                {
                    if (_disposed)
                    {
                        created.Dispose();
                        return;
                    }

                    _inner = created;
                    created = null;
                }
            }
            catch
            {
                created?.Dispose();
            }
            finally
            {
                lock (_sync)
                {
                    _initializationTask = null;
                }
            }
        });
    }
}
