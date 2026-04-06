namespace APPvista.Application.Abstractions;

public enum BlacklistEntryMode
{
    Hidden = 1,
    Ignored = 2
}

public readonly record struct BlacklistEntry(string ProcessName, BlacklistEntryMode Mode);

public interface IBlacklistStore
{
    IReadOnlyDictionary<string, BlacklistEntryMode> Load();
    void Save(IEnumerable<BlacklistEntry> entries);
}
