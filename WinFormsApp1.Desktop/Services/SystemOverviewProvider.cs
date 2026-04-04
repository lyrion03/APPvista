using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using Microsoft.Data.Sqlite;

namespace WinFormsApp1.Desktop.Services;

public sealed class SystemOverviewProvider
{
    private static readonly TimeSpan BackgroundCaptureInterval = TimeSpan.FromSeconds(10);
    private readonly string _databasePath;
    private readonly object _sync = new();
    private readonly PerformanceCounter? _diskReadBytesCounter;
    private readonly PerformanceCounter? _diskWriteBytesCounter;
    private readonly Dictionary<string, NetworkInterfaceUsageState> _networkInterfaceStates = new(StringComparer.OrdinalIgnoreCase);
    private DateOnly _currentDay;
    private long _ioReadBaseline;
    private long _ioWriteBaseline;
    private long _todayIoReadBytes;
    private long _todayIoWriteBytes;
    private DateTime? _lastSampleTimeUtc;
    private DateTime? _lastBackgroundCaptureUtc;
    private SystemOverviewSnapshot _lastSnapshot = new();

    public SystemOverviewProvider(string databasePath)
    {
        _databasePath = databasePath;
        _currentDay = DateOnly.FromDateTime(DateTime.Today);
        EnsureStorage();

        SeedNetworkInterfaceStates();

        try
        {
            _diskReadBytesCounter = new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", "_Total", readOnly: true);
            _diskWriteBytesCounter = new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", "_Total", readOnly: true);
            _ = _diskReadBytesCounter.NextValue();
            _ = _diskWriteBytesCounter.NextValue();

            if (TryReadCounterRawValue(_diskReadBytesCounter, out var currentIoReadTotal) &&
                TryReadCounterRawValue(_diskWriteBytesCounter, out var currentIoWriteTotal))
            {
                var ioBaseline = LoadOrCreateIoBaseline(_currentDay, currentIoReadTotal, currentIoWriteTotal);
                _ioReadBaseline = ioBaseline.ReadBaseline;
                _ioWriteBaseline = ioBaseline.WriteBaseline;
                if (currentIoReadTotal >= ioBaseline.LastTotalReadBytes)
                {
                    _ioReadBaseline += currentIoReadTotal - ioBaseline.LastTotalReadBytes;
                }

                if (currentIoWriteTotal >= ioBaseline.LastTotalWriteBytes)
                {
                    _ioWriteBaseline += currentIoWriteTotal - ioBaseline.LastTotalWriteBytes;
                }

                _todayIoReadBytes = Math.Max(0, currentIoReadTotal - _ioReadBaseline);
                _todayIoWriteBytes = Math.Max(0, currentIoWriteTotal - _ioWriteBaseline);
                SaveIoBaseline(_currentDay, _ioReadBaseline, _ioWriteBaseline, currentIoReadTotal, currentIoWriteTotal);
            }
        }
        catch
        {
            _diskReadBytesCounter = null;
            _diskWriteBytesCounter = null;
        }
    }

    public SystemOverviewSnapshot Capture(bool includeRealtime)
    {
        lock (_sync)
        {
            var nowLocal = DateTime.Now;
            var nowUtc = DateTime.UtcNow;
            var today = DateOnly.FromDateTime(nowLocal);

            if (!includeRealtime &&
                _lastBackgroundCaptureUtc.HasValue &&
                nowUtc - _lastBackgroundCaptureUtc.Value < BackgroundCaptureInterval)
            {
                return _lastSnapshot;
            }

            if (today != _currentDay)
            {
                _currentDay = today;
                _networkInterfaceStates.Clear();
                SeedNetworkInterfaceStates();
                _todayIoReadBytes = 0;
                _todayIoWriteBytes = 0;
                _ioReadBaseline = 0;
                _ioWriteBaseline = 0;
                _lastSampleTimeUtc = null;
            }

            var interfaceTotals = ReadNetworkInterfaceTotals();
            var (todayDownloadBytes, todayUploadBytes) = UpdateNetworkTotals(interfaceTotals);
            var realtimeIoReadBytesPerSecond = includeRealtime ? ReadCounterValue(_diskReadBytesCounter) : 0L;
            var realtimeIoWriteBytesPerSecond = includeRealtime ? ReadCounterValue(_diskWriteBytesCounter) : 0L;
            var currentIoReadTotal = 0L;
            var currentIoWriteTotal = 0L;
            var hasIoRawTotals = TryReadCounterRawValue(_diskReadBytesCounter, out currentIoReadTotal)
                && TryReadCounterRawValue(_diskWriteBytesCounter, out currentIoWriteTotal);
            var realtimeDownloadBytesPerSecond = includeRealtime ? 0L : _lastSnapshot.RealtimeDownloadBytesPerSecond;
            var realtimeUploadBytesPerSecond = includeRealtime ? 0L : _lastSnapshot.RealtimeUploadBytesPerSecond;

            if (_lastSampleTimeUtc.HasValue)
            {
                var elapsedSeconds = (nowUtc - _lastSampleTimeUtc.Value).TotalSeconds;
                if (elapsedSeconds > 0)
                {
                    if (includeRealtime)
                    {
                        realtimeDownloadBytesPerSecond = Math.Max(0, (long)Math.Round((todayDownloadBytes - _lastSnapshot.TodayDownloadBytes) / elapsedSeconds, MidpointRounding.AwayFromZero));
                        realtimeUploadBytesPerSecond = Math.Max(0, (long)Math.Round((todayUploadBytes - _lastSnapshot.TodayUploadBytes) / elapsedSeconds, MidpointRounding.AwayFromZero));
                    }

                    if (!hasIoRawTotals)
                    {
                        var ioReadForAccumulation = includeRealtime
                            ? realtimeIoReadBytesPerSecond
                            : ReadCounterValue(_diskReadBytesCounter);
                        var ioWriteForAccumulation = includeRealtime
                            ? realtimeIoWriteBytesPerSecond
                            : ReadCounterValue(_diskWriteBytesCounter);
                        _todayIoReadBytes += (long)Math.Round(ioReadForAccumulation * elapsedSeconds, MidpointRounding.AwayFromZero);
                        _todayIoWriteBytes += (long)Math.Round(ioWriteForAccumulation * elapsedSeconds, MidpointRounding.AwayFromZero);
                    }
                }
            }

            if (hasIoRawTotals)
            {
                var ioBaseline = LoadOrCreateIoBaseline(today, currentIoReadTotal, currentIoWriteTotal);
                _ioReadBaseline = ioBaseline.ReadBaseline;
                _ioWriteBaseline = ioBaseline.WriteBaseline;
                if (currentIoReadTotal < ioBaseline.LastTotalReadBytes)
                {
                    var previousTodayIoReadBytes = Math.Max(0, ioBaseline.LastTotalReadBytes - ioBaseline.ReadBaseline);
                    _ioReadBaseline = previousTodayIoReadBytes > 0 ? -previousTodayIoReadBytes : 0;
                }

                if (currentIoWriteTotal < ioBaseline.LastTotalWriteBytes)
                {
                    var previousTodayIoWriteBytes = Math.Max(0, ioBaseline.LastTotalWriteBytes - ioBaseline.WriteBaseline);
                    _ioWriteBaseline = previousTodayIoWriteBytes > 0 ? -previousTodayIoWriteBytes : 0;
                }

                if (currentIoReadTotal < _ioReadBaseline)
                {
                    _ioReadBaseline = 0;
                }

                if (currentIoWriteTotal < _ioWriteBaseline)
                {
                    _ioWriteBaseline = 0;
                }

                _todayIoReadBytes = Math.Max(0, currentIoReadTotal - _ioReadBaseline);
                _todayIoWriteBytes = Math.Max(0, currentIoWriteTotal - _ioWriteBaseline);
                SaveIoBaseline(today, _ioReadBaseline, _ioWriteBaseline, currentIoReadTotal, currentIoWriteTotal);
            }

            _lastSampleTimeUtc = nowUtc;
            if (!includeRealtime)
            {
                _lastBackgroundCaptureUtc = nowUtc;
            }

            _lastSnapshot = new SystemOverviewSnapshot
            {
                RealtimeDownloadBytesPerSecond = realtimeDownloadBytesPerSecond,
                RealtimeUploadBytesPerSecond = realtimeUploadBytesPerSecond,
                TodayDownloadBytes = todayDownloadBytes,
                TodayUploadBytes = todayUploadBytes,
                RealtimeIoReadBytesPerSecond = realtimeIoReadBytesPerSecond,
                RealtimeIoWriteBytesPerSecond = realtimeIoWriteBytesPerSecond,
                TodayIoReadBytes = Math.Max(0, _todayIoReadBytes),
                TodayIoWriteBytes = Math.Max(0, _todayIoWriteBytes),
                NetworkDebugSummary = $"网络累计=按网卡分别累计并求和；活跃网卡 {_networkInterfaceStates.Count}，今日 D/U {todayDownloadBytes}/{todayUploadBytes}",
                IoDebugSummary = hasIoRawTotals
                    ? $"IO累计=物理盘原始累计-当日基线；基线 R/W {_ioReadBaseline}/{_ioWriteBaseline}，当前总计 R/W {currentIoReadTotal}/{currentIoWriteTotal}，今日 R/W {Math.Max(0, _todayIoReadBytes)}/{Math.Max(0, _todayIoWriteBytes)}"
                    : $"IO累计=按实时字节速率积分；基线 R/W {_ioReadBaseline}/{_ioWriteBaseline}，今日 R/W {Math.Max(0, _todayIoReadBytes)}/{Math.Max(0, _todayIoWriteBytes)}"
            };
            return _lastSnapshot;
        }
    }

    private void EnsureStorage()
    {
        var directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
CREATE TABLE IF NOT EXISTS system_daily_network_totals (
    day TEXT NOT NULL PRIMARY KEY,
    baseline_download_bytes INTEGER NOT NULL,
    baseline_upload_bytes INTEGER NOT NULL,
    earliest_recorded_at TEXT NOT NULL,
    last_total_download_bytes INTEGER NOT NULL,
    last_total_upload_bytes INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS system_daily_io_totals (
    day TEXT NOT NULL PRIMARY KEY,
    baseline_read_bytes INTEGER NOT NULL,
    baseline_write_bytes INTEGER NOT NULL,
    earliest_recorded_at TEXT NOT NULL,
    last_total_read_bytes INTEGER NOT NULL,
    last_total_write_bytes INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS system_daily_network_interfaces (
    day TEXT NOT NULL,
    interface_id TEXT NOT NULL,
    baseline_download_bytes INTEGER NOT NULL,
    baseline_upload_bytes INTEGER NOT NULL,
    last_total_download_bytes INTEGER NOT NULL,
    last_total_upload_bytes INTEGER NOT NULL,
    PRIMARY KEY (day, interface_id)
);";
        command.ExecuteNonQuery();
    }

    private void SeedNetworkInterfaceStates()
    {
        foreach (var (interfaceId, usage) in ReadNetworkInterfaceTotals())
        {
            if (TryLoadNetworkInterfaceState(_currentDay, interfaceId, out var persistedState))
            {
                var downloadBaseline = persistedState.DownloadBaseline;
                var uploadBaseline = persistedState.UploadBaseline;

                if (usage.DownloadBytes >= persistedState.LastDownloadBytes)
                {
                    downloadBaseline += usage.DownloadBytes - persistedState.LastDownloadBytes;
                }
                else
                {
                    var previousTodayDownloadBytes = Math.Max(0, persistedState.LastDownloadBytes - persistedState.DownloadBaseline);
                    downloadBaseline = previousTodayDownloadBytes > 0 ? -previousTodayDownloadBytes : 0;
                }

                if (usage.UploadBytes >= persistedState.LastUploadBytes)
                {
                    uploadBaseline += usage.UploadBytes - persistedState.LastUploadBytes;
                }
                else
                {
                    var previousTodayUploadBytes = Math.Max(0, persistedState.LastUploadBytes - persistedState.UploadBaseline);
                    uploadBaseline = previousTodayUploadBytes > 0 ? -previousTodayUploadBytes : 0;
                }

                _networkInterfaceStates[interfaceId] = new NetworkInterfaceUsageState(
                    downloadBaseline,
                    uploadBaseline,
                    usage.DownloadBytes,
                    usage.UploadBytes);
            }
            else
            {
                _networkInterfaceStates[interfaceId] = new NetworkInterfaceUsageState(
                    usage.DownloadBytes,
                    usage.UploadBytes,
                    usage.DownloadBytes,
                    usage.UploadBytes);
            }

            SaveNetworkInterfaceState(_currentDay, interfaceId, _networkInterfaceStates[interfaceId]);
        }
    }

    private (long TodayDownloadBytes, long TodayUploadBytes) UpdateNetworkTotals(IReadOnlyDictionary<string, NetworkInterfaceUsage> interfaceTotals)
    {
        foreach (var (interfaceId, usage) in interfaceTotals)
        {
            if (!_networkInterfaceStates.TryGetValue(interfaceId, out var state))
            {
                _networkInterfaceStates[interfaceId] = new NetworkInterfaceUsageState(
                    usage.DownloadBytes,
                    usage.UploadBytes,
                    usage.DownloadBytes,
                    usage.UploadBytes);
                continue;
            }

            var downloadBaseline = state.DownloadBaseline;
            var uploadBaseline = state.UploadBaseline;

            if (usage.DownloadBytes < state.LastDownloadBytes)
            {
                var previousTodayDownloadBytes = Math.Max(0, state.LastDownloadBytes - state.DownloadBaseline);
                downloadBaseline = previousTodayDownloadBytes > 0 ? -previousTodayDownloadBytes : 0;
            }

            if (usage.UploadBytes < state.LastUploadBytes)
            {
                var previousTodayUploadBytes = Math.Max(0, state.LastUploadBytes - state.UploadBaseline);
                uploadBaseline = previousTodayUploadBytes > 0 ? -previousTodayUploadBytes : 0;
            }

            if (usage.DownloadBytes < downloadBaseline)
            {
                downloadBaseline = 0;
            }

            if (usage.UploadBytes < uploadBaseline)
            {
                uploadBaseline = 0;
            }

            _networkInterfaceStates[interfaceId] = state with
            {
                DownloadBaseline = downloadBaseline,
                UploadBaseline = uploadBaseline,
                LastDownloadBytes = usage.DownloadBytes,
                LastUploadBytes = usage.UploadBytes
            };
            SaveNetworkInterfaceState(_currentDay, interfaceId, _networkInterfaceStates[interfaceId]);
        }

        long todayDownloadBytes = 0;
        long todayUploadBytes = 0;
        foreach (var state in _networkInterfaceStates.Values)
        {
            todayDownloadBytes += Math.Max(0, state.LastDownloadBytes - state.DownloadBaseline);
            todayUploadBytes += Math.Max(0, state.LastUploadBytes - state.UploadBaseline);
        }

        return (todayDownloadBytes, todayUploadBytes);
    }

    private bool TryLoadNetworkInterfaceState(DateOnly day, string interfaceId, out NetworkInterfaceUsageState state)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT baseline_download_bytes,
       baseline_upload_bytes,
       last_total_download_bytes,
       last_total_upload_bytes
FROM system_daily_network_interfaces
WHERE day = $day AND interface_id = $interfaceId;";
        command.Parameters.AddWithValue("$day", day.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$interfaceId", interfaceId);

        using var reader = command.ExecuteReader();
        if (reader.Read())
        {
            state = new NetworkInterfaceUsageState(
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetInt64(2),
                reader.GetInt64(3));
            return true;
        }

        state = default;
        return false;
    }

    private void SaveNetworkInterfaceState(DateOnly day, string interfaceId, NetworkInterfaceUsageState state)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO system_daily_network_interfaces (
    day,
    interface_id,
    baseline_download_bytes,
    baseline_upload_bytes,
    last_total_download_bytes,
    last_total_upload_bytes
)
VALUES (
    $day,
    $interfaceId,
    $baselineDownloadBytes,
    $baselineUploadBytes,
    $lastTotalDownloadBytes,
    $lastTotalUploadBytes
)
ON CONFLICT(day, interface_id) DO UPDATE SET
    baseline_download_bytes = excluded.baseline_download_bytes,
    baseline_upload_bytes = excluded.baseline_upload_bytes,
    last_total_download_bytes = excluded.last_total_download_bytes,
    last_total_upload_bytes = excluded.last_total_upload_bytes;";
        command.Parameters.AddWithValue("$day", day.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$interfaceId", interfaceId);
        command.Parameters.AddWithValue("$baselineDownloadBytes", state.DownloadBaseline);
        command.Parameters.AddWithValue("$baselineUploadBytes", state.UploadBaseline);
        command.Parameters.AddWithValue("$lastTotalDownloadBytes", state.LastDownloadBytes);
        command.Parameters.AddWithValue("$lastTotalUploadBytes", state.LastUploadBytes);
        command.ExecuteNonQuery();
    }

    private (long ReadBaseline, long WriteBaseline, long LastTotalReadBytes, long LastTotalWriteBytes) LoadOrCreateIoBaseline(DateOnly day, long currentReadTotal, long currentWriteTotal)
    {
        using var connection = OpenConnection();
        using var selectCommand = connection.CreateCommand();
        selectCommand.CommandText = @"
SELECT baseline_read_bytes,
       baseline_write_bytes,
       last_total_read_bytes,
       last_total_write_bytes
FROM system_daily_io_totals
WHERE day = $day;";
        selectCommand.Parameters.AddWithValue("$day", day.ToString("yyyy-MM-dd"));

        using var reader = selectCommand.ExecuteReader();
        if (reader.Read())
        {
            return (reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3));
        }

        SaveIoBaseline(day, currentReadTotal, currentWriteTotal, currentReadTotal, currentWriteTotal);
        return (currentReadTotal, currentWriteTotal, currentReadTotal, currentWriteTotal);
    }

    private void SaveIoBaseline(DateOnly day, long baselineReadBytes, long baselineWriteBytes, long currentReadTotal, long currentWriteTotal)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO system_daily_io_totals (
    day,
    baseline_read_bytes,
    baseline_write_bytes,
    earliest_recorded_at,
    last_total_read_bytes,
    last_total_write_bytes
)
VALUES (
    $day,
    $baselineReadBytes,
    $baselineWriteBytes,
    $earliestRecordedAt,
    $lastTotalReadBytes,
    $lastTotalWriteBytes
)
ON CONFLICT(day) DO UPDATE SET
    baseline_read_bytes = excluded.baseline_read_bytes,
    baseline_write_bytes = excluded.baseline_write_bytes,
    last_total_read_bytes = excluded.last_total_read_bytes,
    last_total_write_bytes = excluded.last_total_write_bytes;";
        command.Parameters.AddWithValue("$day", day.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$baselineReadBytes", baselineReadBytes);
        command.Parameters.AddWithValue("$baselineWriteBytes", baselineWriteBytes);
        command.Parameters.AddWithValue("$earliestRecordedAt", DateTime.Now.ToString("O"));
        command.Parameters.AddWithValue("$lastTotalReadBytes", currentReadTotal);
        command.Parameters.AddWithValue("$lastTotalWriteBytes", currentWriteTotal);
        command.ExecuteNonQuery();
    }

    private SqliteConnection OpenConnection()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true
        };

        var connection = new SqliteConnection(builder.ConnectionString);
        connection.Open();
        return connection;
    }

    private static long ReadCounterValue(PerformanceCounter? counter)
    {
        if (counter is null)
        {
            return 0;
        }

        try
        {
            return Math.Max(0, (long)Math.Round(counter.NextValue(), MidpointRounding.AwayFromZero));
        }
        catch
        {
            return 0;
        }
    }

    private static bool TryReadCounterRawValue(PerformanceCounter? counter, out long value)
    {
        value = 0;
        if (counter is null)
        {
            return false;
        }

        try
        {
            value = Math.Max(0, counter.RawValue);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static IReadOnlyDictionary<string, NetworkInterfaceUsage> ReadNetworkInterfaceTotals()
    {
        var totals = new Dictionary<string, NetworkInterfaceUsage>(StringComparer.OrdinalIgnoreCase);

        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                networkInterface.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }

            try
            {
                var statistics = networkInterface.GetIPStatistics();
                totals[networkInterface.Id] = new NetworkInterfaceUsage(
                    Math.Max(0, statistics.BytesReceived),
                    Math.Max(0, statistics.BytesSent));
            }
            catch
            {
            }
        }

        return totals;
    }
}

internal readonly record struct NetworkInterfaceUsage(long DownloadBytes, long UploadBytes);
internal readonly record struct NetworkInterfaceUsageState(
    long DownloadBaseline,
    long UploadBaseline,
    long LastDownloadBytes,
    long LastUploadBytes);

public sealed class SystemOverviewSnapshot
{
    public long RealtimeDownloadBytesPerSecond { get; init; }
    public long RealtimeUploadBytesPerSecond { get; init; }
    public long TodayDownloadBytes { get; init; }
    public long TodayUploadBytes { get; init; }
    public long RealtimeIoReadBytesPerSecond { get; init; }
    public long RealtimeIoWriteBytesPerSecond { get; init; }
    public long TodayIoReadBytes { get; init; }
    public long TodayIoWriteBytes { get; init; }
    public string NetworkDebugSummary { get; init; } = string.Empty;
    public string IoDebugSummary { get; init; } = string.Empty;
}
