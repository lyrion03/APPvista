using WinFormsApp1.Domain.Entities;

namespace WinFormsApp1.Application.Abstractions;

public interface IDailyProcessActivityStore
{
    IReadOnlyList<DailyProcessActivitySummary> Load(DateOnly day);
    void Save(DateOnly day, IEnumerable<DailyProcessActivitySummary> summaries);
}
