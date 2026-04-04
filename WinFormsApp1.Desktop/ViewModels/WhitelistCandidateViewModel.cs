namespace WinFormsApp1.Desktop.ViewModels;

public sealed class WhitelistCandidateViewModel : ObservableObject
{
    private readonly Action<WhitelistCandidateViewModel, bool> _selectionChanged;
    private bool _isWhitelisted;

    public WhitelistCandidateViewModel(string processName, bool isWhitelisted, Action<WhitelistCandidateViewModel, bool> selectionChanged)
    {
        ProcessName = processName;
        DisplayName = processName;
        _isWhitelisted = isWhitelisted;
        _selectionChanged = selectionChanged;
    }

    public string ProcessName { get; }
    public string DisplayName { get; private set; }

    public bool IsWhitelisted
    {
        get => _isWhitelisted;
        set
        {
            if (SetProperty(ref _isWhitelisted, value))
            {
                _selectionChanged(this, value);
            }
        }
    }

    public void UpdateDisplayName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = ProcessName;
        }

        if (!string.Equals(DisplayName, displayName, StringComparison.Ordinal))
        {
            DisplayName = displayName;
            RaisePropertyChanged(nameof(DisplayName));
        }
    }
}
