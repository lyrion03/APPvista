using System.IO;
using System.Text.Json;

namespace APPvista.Desktop.Services;

public sealed class DiagnosticsOptionsStore
{
    private readonly string _filePath;

    public DiagnosticsOptionsStore(string filePath)
    {
        _filePath = filePath;
    }

    public DiagnosticsOptions Load()
    {
        EnsureFileExists();

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<DiagnosticsOptions>(json) ?? new DiagnosticsOptions();
        }
        catch
        {
            return new DiagnosticsOptions();
        }
    }

    private void EnsureFileExists()
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(_filePath))
        {
            return;
        }

        var json = JsonSerializer.Serialize(new DiagnosticsOptions(), new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(_filePath, json);
    }
}

public sealed class DiagnosticsOptions
{
    public bool EnableStartupPerformanceLog { get; set; }
    public bool EnableRuntimePerformanceLog { get; set; }
}
