using System.Text.Json;
using APPvista.Application.Abstractions;

namespace APPvista.Infrastructure.Persistence;

public sealed class FileBlacklistStore : IBlacklistStore
{
    private readonly string _filePath;
    private readonly object _sync = new();

    public FileBlacklistStore(string filePath)
    {
        _filePath = filePath;
    }

    public IReadOnlyDictionary<string, BlacklistEntryMode> Load()
    {
        lock (_sync)
        {
            EnsureFileExists();

            try
            {
                var json = File.ReadAllText(_filePath);
                var items = JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
                return items
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Select(item => item.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        static item => item,
                        static _ => BlacklistEntryMode.Hidden,
                        StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new Dictionary<string, BlacklistEntryMode>(StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    public void Save(IEnumerable<BlacklistEntry> entries)
    {
        lock (_sync)
        {
            var items = entries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.ProcessName))
                .Select(entry => entry.ProcessName.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(items, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(_filePath, json);
        }
    }

    private void EnsureFileExists()
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (!File.Exists(_filePath))
        {
            File.WriteAllText(_filePath, "[]");
        }
    }
}
