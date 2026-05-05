namespace APPvista.Desktop.ViewModels;

public sealed class DetailDisplayPreferences : ObservableObject
{
    public const string HiddenOption = "隐藏";
    public const string TotalOption = "总量";
    public const string SplitOption = "分离";
    public const string VisibleOption = "显示";
    public const string WorkingSetOption = "工作集";
    public const string PrivateMemoryOption = "私有内存";
    public const string CommitSizeOption = "提交大小";
    public const string ChartScale30SecondsOption = "30秒";
    public const string ChartScale1MinuteOption = "1分钟";
    public const string ChartScale2MinutesOption = "2分钟";
    public const string HistoryOverlayOffOption = "关闭";
    public const string HistoryOverlayUsageDurationOption = "使用时长";
    public const string HistoryOverlayForegroundDurationOption = "前台时长";

    private string _networkDisplayOption = TotalOption;
    private string _ioDisplayOption = TotalOption;
    private string _cpuDisplayOption = HiddenOption;
    private string _memoryDisplayOption = HiddenOption;
    private string _foregroundBackgroundOption = HiddenOption;
    private string _chartScaleOption = ChartScale30SecondsOption;
    private string _historyOverlayOption = HistoryOverlayOffOption;

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

    public string CpuDisplayOption
    {
        get => _cpuDisplayOption;
        set => SetProperty(ref _cpuDisplayOption, NormalizeCpuOption(value));
    }

    public string MemoryDisplayOption
    {
        get => _memoryDisplayOption;
        set => SetProperty(ref _memoryDisplayOption, NormalizeMemoryOption(value));
    }

    public string ChartScaleOption
    {
        get => _chartScaleOption;
        set => SetProperty(ref _chartScaleOption, NormalizeChartScaleOption(value));
    }

    public string HistoryOverlayOption
    {
        get => _historyOverlayOption;
        set => SetProperty(ref _historyOverlayOption, NormalizeHistoryOverlayOption(value));
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
    public bool IsCpuVisible => CpuDisplayOption == VisibleOption;
    public bool IsMemoryHidden => MemoryDisplayOption == HiddenOption;
    public bool IsMemoryWorkingSet => MemoryDisplayOption == WorkingSetOption;
    public bool IsMemoryPrivate => MemoryDisplayOption == PrivateMemoryOption;
    public bool IsMemoryCommit => MemoryDisplayOption == CommitSizeOption;
    public bool IsForegroundBackgroundVisible => ForegroundBackgroundOption == VisibleOption;
    public bool IsHistoryOverlayEnabled => HistoryOverlayOption != HistoryOverlayOffOption;

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

    private static string NormalizeCpuOption(string? value)
    {
        return value == VisibleOption ? VisibleOption : HiddenOption;
    }

    private static string NormalizeMemoryOption(string? value)
    {
        return value switch
        {
            WorkingSetOption => WorkingSetOption,
            PrivateMemoryOption => PrivateMemoryOption,
            CommitSizeOption => CommitSizeOption,
            _ => HiddenOption
        };
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

    private static string NormalizeHistoryOverlayOption(string? value)
    {
        return value switch
        {
            HistoryOverlayUsageDurationOption => HistoryOverlayUsageDurationOption,
            HistoryOverlayForegroundDurationOption => HistoryOverlayForegroundDurationOption,
            _ => HistoryOverlayOffOption
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

        if (propertyName is nameof(CpuDisplayOption))
        {
            base.RaisePropertyChanged(nameof(IsCpuVisible));
        }

        if (propertyName is nameof(MemoryDisplayOption))
        {
            base.RaisePropertyChanged(nameof(IsMemoryHidden));
            base.RaisePropertyChanged(nameof(IsMemoryWorkingSet));
            base.RaisePropertyChanged(nameof(IsMemoryPrivate));
            base.RaisePropertyChanged(nameof(IsMemoryCommit));
        }

        if (propertyName is nameof(ChartScaleOption))
        {
            base.RaisePropertyChanged(nameof(ChartHistorySeconds));
        }

        if (propertyName is nameof(HistoryOverlayOption))
        {
            base.RaisePropertyChanged(nameof(IsHistoryOverlayEnabled));
        }
    }
}
