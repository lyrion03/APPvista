using System.IO;
using System.Text.Json;
using WinFormsApp1.Desktop.ViewModels;

namespace WinFormsApp1.Desktop.Services;

public sealed class ApplicationCardMetricPreferenceStore
{
    private readonly string _filePath;

    public ApplicationCardMetricPreferenceStore(string filePath)
    {
        _filePath = filePath;
    }

    public IReadOnlyList<string> Load()
    {
        if (!File.Exists(_filePath))
        {
            return ApplicationCardMetricPreferences.DefaultMetricIds;
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var model = JsonSerializer.Deserialize<ApplicationCardMetricPreferenceModel>(json);
            return model?.SelectedMetricIds?.Count > 0
                ? model.SelectedMetricIds
                : ApplicationCardMetricPreferences.DefaultMetricIds;
        }
        catch
        {
            return ApplicationCardMetricPreferences.DefaultMetricIds;
        }
    }

    public void Save(IEnumerable<string> metricIds)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var model = new ApplicationCardMetricPreferenceModel
        {
            SelectedMetricIds = metricIds.ToList()
        };

        var json = JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_filePath, json);
    }

    private sealed class ApplicationCardMetricPreferenceModel
    {
        public List<string> SelectedMetricIds { get; set; } = [];
    }
}
