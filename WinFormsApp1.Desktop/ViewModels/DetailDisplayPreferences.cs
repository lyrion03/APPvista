namespace WinFormsApp1.Desktop.ViewModels;

public sealed class DetailDisplayPreferences : ObservableObject
{
    public const string HiddenOption = "隐藏";
    public const string TotalOption = "总量";
    public const string SplitOption = "分离";
    public const string VisibleOption = "显示";
    public const string ChartScale30SecondsOption = "30秒";
    public const string ChartScale1MinuteOption = "1分钟";
    public const string ChartScale2MinutesOption = "2分钟";

    private string _networkDisplayOption = TotalOption;
    private string _ioDisplayOption = TotalOption;
    private string _foregroundBackgroundOption = HiddenOption;
    private string _chartScaleOption = ChartScale30SecondsOption;

    public string NetworkDisplayOption
    {
        get => _networkDisplayOption;
        set => SetProperty(ref _networkDisplayOption, NormalizeNetworkOption(value));
    }

    public string IoDisplayOption
    {
        get => _ioDisplayOption;
        set => SetProperty(ref _ioDisplayOption, NormalizeIoOption(value));
    }

    public string ForegroundBackgroundOption
    {
        get => _foregroundBackgroundOption;
        set => SetProperty(ref _foregroundBackgroundOption, NormalizeForegroundOption(value));
    }

    public string ChartScaleOption
    {
        get => _chartScaleOption;
        set => SetProperty(ref _chartScaleOption, NormalizeChartScaleOption(value));
    }

    public int ChartHistorySeconds => ChartScaleOption switch
    {
        ChartScale2MinutesOption => 120,
        ChartScale1MinuteOption => 60,
        _ => 30
    };

    public bool IsNetworkHidden => NetworkDisplayOption == HiddenOption;
    public bool IsNetworkSplit => NetworkDisplayOption == SplitOption;
    public bool IsIoHidden => IoDisplayOption == HiddenOption;
    public bool IsIoSplit => IoDisplayOption == SplitOption;
    public bool IsForegroundBackgroundVisible => ForegroundBackgroundOption == VisibleOption;

    private static string NormalizeNetworkOption(string? value)
    {
        return value switch
        {
            HiddenOption => HiddenOption,
            SplitOption => SplitOption,
            _ => TotalOption
        };
    }

    private static string NormalizeIoOption(string? value)
    {
        return value switch
        {
            HiddenOption => HiddenOption,
            SplitOption => SplitOption,
            _ => TotalOption
        };
    }

    private static string NormalizeForegroundOption(string? value)
    {
        return value == VisibleOption ? VisibleOption : HiddenOption;
    }

    private static string NormalizeChartScaleOption(string? value)
    {
        return value switch
        {
            ChartScale1MinuteOption => ChartScale1MinuteOption,
            ChartScale2MinutesOption => ChartScale2MinutesOption,
            _ => ChartScale30SecondsOption
        };
    }

    protected override void RaisePropertyChanged(string? propertyName = null)
    {
        base.RaisePropertyChanged(propertyName);

        if (propertyName is nameof(NetworkDisplayOption))
        {
            base.RaisePropertyChanged(nameof(IsNetworkHidden));
            base.RaisePropertyChanged(nameof(IsNetworkSplit));
        }

        if (propertyName is nameof(IoDisplayOption))
        {
            base.RaisePropertyChanged(nameof(IsIoHidden));
            base.RaisePropertyChanged(nameof(IsIoSplit));
        }

        if (propertyName is nameof(ForegroundBackgroundOption))
        {
            base.RaisePropertyChanged(nameof(IsForegroundBackgroundVisible));
        }

        if (propertyName is nameof(ChartScaleOption))
        {
            base.RaisePropertyChanged(nameof(ChartHistorySeconds));
        }
    }
}
