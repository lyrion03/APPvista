using System.IO;
using System.Text.Json;

namespace APPvista.Desktop.Services;

public sealed class WindowedOnlyRecordingStore
{
    private readonly string _filePath;

    public WindowedOnlyRecordingStore(string filePath)
    {
        _filePath = filePath;
    }

    public bool Load()
    {
        if (!File.Exists(_filePath))
        {
            return false;
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var model = JsonSerializer.Deserialize<WindowedOnlyRecordingModel>(json);
            return model?.Enabled ?? false;
        }
        catch
        {
            return false;
        }
    }

    public void Save(bool enabled)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(
            new WindowedOnlyRecordingModel { Enabled = enabled },
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_filePath, json);
    }

    private sealed class WindowedOnlyRecordingModel
    {
        public bool Enabled { get; set; }
    }
}
