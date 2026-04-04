using WinFormsApp1.Domain.Entities;

namespace WinFormsApp1.Application.Abstractions;

public interface IProcessNetworkUsageSource : IDisposable
{
    string Status { get; }
    IReadOnlyList<ProcessNetworkUsageEvent> DrainPendingEvents();
}
