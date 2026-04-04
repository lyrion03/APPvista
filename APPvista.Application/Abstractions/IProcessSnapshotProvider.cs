using APPvista.Domain.Entities;

namespace APPvista.Application.Abstractions;

public interface IProcessSnapshotProvider
{
    ProcessSnapshotBatch CaptureTopProcesses(int count, bool lightweight = false);
}
