using WinFormsApp1.Application.Abstractions;
using WinFormsApp1.Domain.Entities;

namespace WinFormsApp1.Infrastructure.Services;

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
