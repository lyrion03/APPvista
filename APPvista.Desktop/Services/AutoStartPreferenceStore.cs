using System.IO;
using System.Text.Json;

namespace APPvista.Desktop.Services;

public sealed class AutoStartPreferenceStore
{
    private readonly string _filePath;

    public AutoStartPreferenceStore(string filePath)
    {
        _filePath = filePath;
    }

    public bool Load()
    {
        if (!File.Exists(_filePath))
        {
            return true;
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var model = JsonSerializer.Deserialize<AutoStartPreferenceModel>(json);
            return model?.Enabled ?? true;
        }
        catch
        {
            return true;
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
            new AutoStartPreferenceModel { Enabled = enabled },
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_filePath, json);
    }

    private sealed class AutoStartPreferenceModel
    {
        public bool Enabled { get; set; } = true;
    }
}
