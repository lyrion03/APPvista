using APPvista.Domain.Entities;

namespace APPvista.Application.Abstractions;

public interface IProcessNetworkUsageSource : IDisposable
{
    string Status { get; }
    IReadOnlyList<ProcessNetworkUsageEvent> DrainPendingEvents();
}
