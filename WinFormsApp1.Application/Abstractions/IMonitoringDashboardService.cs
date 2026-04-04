using WinFormsApp1.Domain.Entities;

namespace WinFormsApp1.Application.Abstractions;

public interface IMonitoringDashboardService
{
    bool IsWindowedOnlyRecording { get; }
    DashboardSnapshot GetSnapshot();
    void SetWindowedOnlyRecording(bool enabled);
}
