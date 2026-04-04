using WinFormsApp1.Domain.Entities;

namespace WinFormsApp1.Application.Abstractions;

public interface IProcessSnapshotProvider
{
    ProcessSnapshotBatch CaptureTopProcesses(int count);
}
