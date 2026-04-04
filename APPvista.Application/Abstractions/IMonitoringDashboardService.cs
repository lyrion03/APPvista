using APPvista.Domain.Entities;

namespace APPvista.Application.Abstractions;

public interface IMonitoringDashboardService
{
    bool IsWindowedOnlyRecording { get; }
    DashboardSnapshot GetSnapshot();
    void SetWindowedOnlyRecording(bool enabled);
}
