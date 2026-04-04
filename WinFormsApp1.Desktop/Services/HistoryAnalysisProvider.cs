using Microsoft.Data.Sqlite;

namespace WinFormsApp1.Desktop.Services;

public sealed class HistoryAnalysisProvider
{
    private readonly string _databasePath;

    public HistoryAnalysisProvider(string databasePath)
    {
        _databasePath = databasePath;
    }

    public IReadOnlyList<HistoryDailyRecord> LoadDailyRecords(int maxDays)
    {
        var applicationRecords = LoadApplicationDailyRecords(maxDays);
        if (applicationRecords.Count == 0)
        {
            return [];
        }

        var networkTotals = LoadSystemNetworkTotals();
        var ioTotals = LoadSystemIoTotals();
        var result = new List<HistoryDailyRecord>(applicationRecords.Count);

        foreach (var record in applicationRecords)
        {
            networkTotals.TryGetValue(record.Day, out var network);
            ioTotals.TryGetValue(record.Day, out var io);

            result.Add(record with
            {
                SystemDownloadBytes = network.DownloadBytes,
                SystemUploadBytes = network.UploadBytes,
                SystemIoReadBytes = io.ReadBytes,
                SystemIoWriteBytes = io.WriteBytes
            });
        }

        return result;
    }

    public IReadOnlyList<HistoryApplicationAggregate> LoadApplicationAggregates(DateOnly startDay, DateOnly endDay)
    {
        if (endDay < startDay)
        {
            return [];
        }

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT
    process_name,
    MAX(executable_path) AS executable_path,
    COALESCE(SUM(foreground_milliseconds), 0) AS foreground_milliseconds,
    COALESCE(SUM(background_milliseconds), 0) AS background_milliseconds,
    COALESCE(SUM(download_bytes), 0) AS download_bytes,
    COALESCE(SUM(upload_bytes), 0) AS upload_bytes,
    COALESCE(SUM(io_read_bytes), 0) AS io_read_bytes,
    COALESCE(SUM(io_write_bytes), 0) AS io_write_bytes
FROM daily_process_activity
WHERE day >= $startDay
  AND day <= $endDay
GROUP BY process_name
ORDER BY process_name COLLATE NOCASE;";
        command.Parameters.AddWithValue("$startDay", startDay.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$endDay", endDay.ToString("yyyy-MM-dd"));

        using var reader = command.ExecuteReader();
        var result = new List<HistoryApplicationAggregate>();
        while (reader.Read())
        {
            result.Add(new HistoryApplicationAggregate
            {
                ProcessName = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                ExecutablePath = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                ForegroundMilliseconds = reader.GetInt64(2),
                BackgroundMilliseconds = reader.GetInt64(3),
                DownloadBytes = reader.GetInt64(4),
                UploadBytes = reader.GetInt64(5),
                IoReadBytes = reader.GetInt64(6),
                IoWriteBytes = reader.GetInt64(7)
            });
        }

        return result;
    }

    private List<HistoryDailyRecord> LoadApplicationDailyRecords(int maxDays)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
WITH day_ranked AS (
    SELECT
        day,
        process_name,
        foreground_milliseconds,
        (foreground_milliseconds + background_milliseconds) AS total_usage_milliseconds,
        ROW_NUMBER() OVER (
            PARTITION BY day
            ORDER BY foreground_milliseconds DESC, (foreground_milliseconds + background_milliseconds) DESC, process_name COLLATE NOCASE
        ) AS usage_rank
    FROM daily_process_activity
)
SELECT
    d.day,
    COUNT(*) AS application_count,
    COALESCE(SUM(d.foreground_milliseconds), 0) AS foreground_milliseconds,
    COALESCE(SUM(d.background_milliseconds), 0) AS background_milliseconds,
    COALESCE(SUM(d.download_bytes), 0) AS app_download_bytes,
    COALESCE(SUM(d.upload_bytes), 0) AS app_upload_bytes,
    COALESCE(SUM(d.io_read_bytes), 0) AS app_io_read_bytes,
    COALESCE(SUM(d.io_write_bytes), 0) AS app_io_write_bytes,
    COALESCE(SUM(d.foreground_cpu_total + d.background_cpu_total), 0) AS cpu_total,
    COALESCE(SUM(d.foreground_samples + d.background_samples), 0) AS cpu_samples,
    COALESCE(MAX(d.peak_working_set_bytes), 0) AS peak_working_set_bytes,
    COALESCE(MAX(CASE WHEN r.usage_rank = 1 THEN r.process_name END), '') AS top_application_name,
    COALESCE(MAX(CASE WHEN r.usage_rank = 1 THEN r.foreground_milliseconds END), 0) AS top_application_foreground_milliseconds,
    COALESCE(MAX(CASE WHEN r.usage_rank = 1 THEN r.total_usage_milliseconds END), 0) AS top_application_usage_milliseconds
FROM daily_process_activity d
LEFT JOIN day_ranked r
    ON r.day = d.day
   AND r.process_name = d.process_name
GROUP BY d.day
ORDER BY d.day DESC
LIMIT $limit;";
        command.Parameters.AddWithValue("$limit", maxDays);

        using var reader = command.ExecuteReader();
        var records = new List<HistoryDailyRecord>();
        while (reader.Read())
        {
            records.Add(new HistoryDailyRecord
            {
                Day = DateOnly.Parse(reader.GetString(0)),
                ApplicationCount = reader.GetInt32(1),
                ForegroundMilliseconds = reader.GetInt64(2),
                BackgroundMilliseconds = reader.GetInt64(3),
                AppDownloadBytes = reader.GetInt64(4),
                AppUploadBytes = reader.GetInt64(5),
                AppIoReadBytes = reader.GetInt64(6),
                AppIoWriteBytes = reader.GetInt64(7),
                CpuTotal = reader.GetDouble(8),
                CpuSamples = reader.GetInt32(9),
                PeakWorkingSetBytes = reader.GetInt64(10),
                TopApplicationName = reader.IsDBNull(11) ? string.Empty : reader.GetString(11),
                TopApplicationForegroundMilliseconds = reader.GetInt64(12),
                TopApplicationUsageMilliseconds = reader.GetInt64(13)
            });
        }

        return records;
    }

    private Dictionary<DateOnly, (long DownloadBytes, long UploadBytes)> LoadSystemNetworkTotals()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT
    day,
    MAX(0, last_total_download_bytes - baseline_download_bytes) AS download_bytes,
    MAX(0, last_total_upload_bytes - baseline_upload_bytes) AS upload_bytes
FROM system_daily_network_totals;";

        using var reader = command.ExecuteReader();
        var result = new Dictionary<DateOnly, (long DownloadBytes, long UploadBytes)>();
        while (reader.Read())
        {
            result[DateOnly.Parse(reader.GetString(0))] = (reader.GetInt64(1), reader.GetInt64(2));
        }

        return result;
    }

    private Dictionary<DateOnly, (long ReadBytes, long WriteBytes)> LoadSystemIoTotals()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT
    day,
    MAX(0, last_total_read_bytes - baseline_read_bytes) AS read_bytes,
    MAX(0, last_total_write_bytes - baseline_write_bytes) AS write_bytes
FROM system_daily_io_totals;";

        using var reader = command.ExecuteReader();
        var result = new Dictionary<DateOnly, (long ReadBytes, long WriteBytes)>();
        while (reader.Read())
        {
            result[DateOnly.Parse(reader.GetString(0))] = (reader.GetInt64(1), reader.GetInt64(2));
        }

        return result;
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
}

public readonly record struct HistoryDailyRecord
{
    public DateOnly Day { get; init; }
    public int ApplicationCount { get; init; }
    public long ForegroundMilliseconds { get; init; }
    public long BackgroundMilliseconds { get; init; }
    public long AppDownloadBytes { get; init; }
    public long AppUploadBytes { get; init; }
    public long AppIoReadBytes { get; init; }
    public long AppIoWriteBytes { get; init; }
    public double CpuTotal { get; init; }
    public int CpuSamples { get; init; }
    public long PeakWorkingSetBytes { get; init; }
    public string TopApplicationName { get; init; }
    public long TopApplicationUsageMilliseconds { get; init; }
    public long TopApplicationForegroundMilliseconds { get; init; }
    public long SystemDownloadBytes { get; init; }
    public long SystemUploadBytes { get; init; }
    public long SystemIoReadBytes { get; init; }
    public long SystemIoWriteBytes { get; init; }

    public long TotalUsageMilliseconds => ForegroundMilliseconds + BackgroundMilliseconds;
    public long AppTotalNetworkBytes => AppDownloadBytes + AppUploadBytes;
    public long AppTotalIoBytes => AppIoReadBytes + AppIoWriteBytes;
    public long TotalNetworkBytes => SystemDownloadBytes + SystemUploadBytes;
    public long TotalIoBytes => SystemIoReadBytes + SystemIoWriteBytes;
}

public readonly record struct HistoryApplicationAggregate
{
    public string ProcessName { get; init; }
    public string ExecutablePath { get; init; }
    public long ForegroundMilliseconds { get; init; }
    public long BackgroundMilliseconds { get; init; }
    public long DownloadBytes { get; init; }
    public long UploadBytes { get; init; }
    public long IoReadBytes { get; init; }
    public long IoWriteBytes { get; init; }

    public long TotalUsageMilliseconds => ForegroundMilliseconds + BackgroundMilliseconds;
    public long TotalTrafficBytes => DownloadBytes + UploadBytes;
    public long TotalIoBytes => IoReadBytes + IoWriteBytes;
}
