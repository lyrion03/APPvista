using System.IO;
using System.Text.Json;
using APPvista.Domain.Entities;

namespace APPvista.Desktop.Services;

public sealed class ApplicationAliasStore
{
    private readonly string _filePath;
    private readonly object _sync = new();

    public ApplicationAliasStore(string filePath)
    {
        _filePath = filePath;
    }

    public IReadOnlyDictionary<string, string> Load()
    {
        lock (_sync)
        {
            if (!File.Exists(_filePath))
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            try
            {
                using var stream = File.OpenRead(_filePath);
                var aliases = JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ??
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                return aliases
                    .Where(static pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                    .ToDictionary(
                        static pair => pair.Key,
                        static pair => pair.Value.Trim(),
                        StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    public void Save(IReadOnlyDictionary<string, string> aliases)
    {
        lock (_sync)
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var normalized = aliases
                .Where(static pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    static pair => pair.Key,
                    static pair => pair.Value.Trim(),
                    StringComparer.OrdinalIgnoreCase);

            using var stream = File.Create(_filePath);
            JsonSerializer.Serialize(stream, normalized, new JsonSerializerOptions { WriteIndented = true });
        }
    }

    public static string CreateKey(ProcessResourceSnapshot process)
    {
        return CreateKey(process.ExecutablePath, process.ProcessName);
    }

    public static string CreateKey(string executablePath, string processName)
    {
        return !string.IsNullOrWhiteSpace(executablePath)
            ? executablePath.Trim()
            : processName.Trim();
    }
}
