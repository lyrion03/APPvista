using System.Text.Json;
using APPvista.Application.Abstractions;

namespace APPvista.Infrastructure.Persistence;

public sealed class FileWhitelistStore : IWhitelistStore
{
    private readonly string _filePath;
    private readonly object _sync = new();

    public FileWhitelistStore(string filePath)
    {
        _filePath = filePath;
    }

    public IReadOnlySet<string> Load()
    {
        lock (_sync)
        {
            EnsureFileExists();

            try
            {
                var json = File.ReadAllText(_filePath);
                var items = JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
                return new HashSet<string>(items.Where(item => !string.IsNullOrWhiteSpace(item)), StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    public void Save(IEnumerable<string> processNames)
    {
        lock (_sync)
        {
            var items = processNames
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
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
