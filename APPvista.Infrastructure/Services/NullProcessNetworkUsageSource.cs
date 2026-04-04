using APPvista.Application.Abstractions;
using APPvista.Domain.Entities;

namespace APPvista.Infrastructure.Services;

public sealed class NullProcessNetworkUsageSource : IProcessNetworkUsageSource
{
    public string Status => "网络采集未启用";

    public IReadOnlyList<ProcessNetworkUsageEvent> DrainPendingEvents()
    {
        return Array.Empty<ProcessNetworkUsageEvent>();
    }

    public void Dispose()
    {
    }
}
