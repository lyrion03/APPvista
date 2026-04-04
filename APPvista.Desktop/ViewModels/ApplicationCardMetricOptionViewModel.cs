using System.Collections.ObjectModel;

namespace APPvista.Desktop.ViewModels;

public sealed class ApplicationCardMetricOptionViewModel : ObservableObject
{
    private readonly Action<ApplicationCardMetricOptionViewModel, bool> _selectionChanged;
    private bool _isSelected;

    public ApplicationCardMetricOptionViewModel(string id, string label, Action<ApplicationCardMetricOptionViewModel, bool> selectionChanged)
    {
        Id = id;
        Label = label;
        _selectionChanged = selectionChanged;
    }

    public string Id { get; }
    public string Label { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                _selectionChanged(this, value);
            }
        }
    }

    public void SetSelectedSilently(bool value)
    {
        SetProperty(ref _isSelected, value);
    }
}

public sealed class ApplicationCardMetricGroupViewModel
{
    public ApplicationCardMetricGroupViewModel(string title, IEnumerable<ApplicationCardMetricOptionViewModel> options)
    {
        Title = title;
        Options = new ObservableCollection<ApplicationCardMetricOptionViewModel>(options);
    }

    public string Title { get; }
    public ObservableCollection<ApplicationCardMetricOptionViewModel> Options { get; }
}

public sealed class ApplicationCardMetricDisplayItem : ObservableObject
{
    private string _value;

    public ApplicationCardMetricDisplayItem(string metricId, string label, string value)
    {
        MetricId = metricId;
        Label = label;
        _value = value;
    }

    public string MetricId { get; }
    public string Label { get; }

    public string Value
    {
        get => _value;
        set => SetProperty(ref _value, value);
    }
}
