using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using APPvista.Application.Abstractions;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;

namespace APPvista.Desktop.ViewModels;

public sealed class BlacklistCandidateViewModel : ObservableObject
{
    private readonly Action<BlacklistCandidateViewModel> _stateChanged;
    private bool _isBlacklisted;
    private bool _isIgnored;
    private static readonly Brush DefaultNameBrush = CreateFrozenBrush(0x21, 0x3B, 0x35);
    private static readonly Brush IgnoredNameBrush = CreateFrozenBrush(0xC3, 0x34, 0x34);

    public BlacklistCandidateViewModel(
        string processName,
        BlacklistEntryMode? mode,
        Action<BlacklistCandidateViewModel> stateChanged)
    {
        ProcessName = processName;
        DisplayName = processName;
        _isBlacklisted = mode is BlacklistEntryMode.Hidden or BlacklistEntryMode.Ignored;
        _isIgnored = mode == BlacklistEntryMode.Ignored;
        _stateChanged = stateChanged;
        ToggleIgnoredCommand = new RelayCommand(ToggleIgnored);
    }

    public string ProcessName { get; }
    public string DisplayName { get; private set; }
    public ICommand ToggleIgnoredCommand { get; }

    public bool IsBlacklisted
    {
        get => _isBlacklisted;
        set
        {
            if (!SetProperty(ref _isBlacklisted, value))
            {
                return;
            }

            if (!value && _isIgnored)
            {
                _isIgnored = false;
                RaiseIgnoredPropertiesChanged();
            }

            RaisePropertyChanged(nameof(Mode));
            _stateChanged(this);
        }
    }

    public bool IsIgnored => _isIgnored;
    public Visibility CheckboxVisibility => _isIgnored ? Visibility.Collapsed : Visibility.Visible;
    public Brush NameForeground => _isIgnored ? IgnoredNameBrush : DefaultNameBrush;
    public FontWeight NameFontWeight => _isIgnored ? FontWeights.SemiBold : FontWeights.Normal;
    public BlacklistEntryMode? Mode => !_isBlacklisted
        ? null
        : _isIgnored ? BlacklistEntryMode.Ignored : BlacklistEntryMode.Hidden;

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

    public void UpdateMode(BlacklistEntryMode? mode)
    {
        var isBlacklisted = mode is BlacklistEntryMode.Hidden or BlacklistEntryMode.Ignored;
        var isIgnored = mode == BlacklistEntryMode.Ignored;

        if (_isBlacklisted != isBlacklisted)
        {
            _isBlacklisted = isBlacklisted;
            RaisePropertyChanged(nameof(IsBlacklisted));
        }

        if (_isIgnored != isIgnored)
        {
            _isIgnored = isIgnored;
            RaiseIgnoredPropertiesChanged();
        }

        RaisePropertyChanged(nameof(Mode));
    }

    private void ToggleIgnored()
    {
        if (_isIgnored)
        {
            _isIgnored = false;
            _isBlacklisted = false;
        }
        else
        {
            _isBlacklisted = true;
            _isIgnored = true;
        }

        RaisePropertyChanged(nameof(IsBlacklisted));
        RaiseIgnoredPropertiesChanged();
        RaisePropertyChanged(nameof(Mode));
        _stateChanged(this);
    }

    private void RaiseIgnoredPropertiesChanged()
    {
        RaisePropertyChanged(nameof(IsIgnored));
        RaisePropertyChanged(nameof(CheckboxVisibility));
        RaisePropertyChanged(nameof(NameForeground));
        RaisePropertyChanged(nameof(NameFontWeight));
    }

    private static Brush CreateFrozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        if (brush.CanFreeze)
        {
            brush.Freeze();
        }

        return brush;
    }
}
