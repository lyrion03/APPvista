using APPvista.Domain.Entities;

namespace APPvista.Application.Abstractions;

public interface IDailyProcessActivityStore
{
    IReadOnlyList<DailyProcessActivitySummary> Load(DateOnly day);
    void Save(DateOnly day, IEnumerable<DailyProcessActivitySummary> summaries);
}
