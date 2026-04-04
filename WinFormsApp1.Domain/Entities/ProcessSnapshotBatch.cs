namespace WinFormsApp1.Domain.Entities;

public sealed class ProcessSnapshotBatch
{
    public IReadOnlyList<ProcessResourceSnapshot> Processes { get; init; } =
        Array.Empty<ProcessResourceSnapshot>();

    public IReadOnlyCollection<string> IncompleteProcessNames { get; init; } =
        Array.Empty<string>();
}
