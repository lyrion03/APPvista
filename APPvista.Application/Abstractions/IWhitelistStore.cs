namespace APPvista.Application.Abstractions;

public interface IWhitelistStore
{
    IReadOnlySet<string> Load();
    void Save(IEnumerable<string> processNames);
}
