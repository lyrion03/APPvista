using Microsoft.Data.Sqlite;
using APPvista.Application.Abstractions;

namespace APPvista.Desktop.Services;

public sealed class HistoryAnalysisProvider
{
    private readonly string _databasePath;
    private readonly IBlacklistStore _blacklistStore;

    public HistoryAnalysisProvider(string databasePath, IBlacklistStore blacklistStore)
    {
        _databasePath = databasePath;
        _blacklistStore = blacklistStore;
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

    public IReadOnlyList<HistoryOverviewApplicationAggregate> LoadOverviewApplicationAggregates(DateOnly startDay, DateOnly endDay)
    {
        if (endDay < startDay)
        {
            return [];
        }

        var ignoredProcesses = LoadIgnoredProcesses();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = BuildLoadOverviewApplicationAggregatesSql(ignoredProcesses, command);
        command.Parameters.AddWithValue("$startDay", startDay.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$endDay", endDay.ToString("yyyy-MM-dd"));

        using var reader = command.ExecuteReader();
        var result = new List<HistoryOverviewApplicationAggregate>();
        while (reader.Read())
        {
            result.Add(new HistoryOverviewApplicationAggregate
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

    public IReadOnlyList<HistoryOverviewApplicationAggregate> LoadOverviewApplicationAggregates(IReadOnlyCollection<DateOnly> selectedDays)
    {
        if (selectedDays.Count == 0)
        {
            return [];
        }

        var ignoredProcesses = LoadIgnoredProcesses();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = BuildLoadOverviewApplicationAggregatesByDaysSql(selectedDays, ignoredProcesses, command);

        using var reader = command.ExecuteReader();
        var result = new List<HistoryOverviewApplicationAggregate>();
        while (reader.Read())
        {
            result.Add(new HistoryOverviewApplicationAggregate
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

    public IReadOnlyList<HistoryApplicationAggregate> LoadApplicationAggregates(DateOnly startDay, DateOnly endDay)
    {
        if (endDay < startDay)
        {
            return [];
        }

        var ignoredProcesses = LoadIgnoredProcesses();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = BuildLoadApplicationAggregatesSql(ignoredProcesses, command);
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
                ActiveDays = reader.GetInt32(2),
                ForegroundMilliseconds = reader.GetInt64(3),
                BackgroundMilliseconds = reader.GetInt64(4),
                DownloadBytes = reader.GetInt64(5),
                UploadBytes = reader.GetInt64(6),
                IoReadBytes = reader.GetInt64(7),
                IoWriteBytes = reader.GetInt64(8),
                ForegroundCpuTotal = reader.GetDouble(9),
                ForegroundWorkingSetTotal = reader.GetDouble(10),
                ForegroundSamples = reader.GetInt32(11),
                BackgroundCpuTotal = reader.GetDouble(12),
                BackgroundWorkingSetTotal = reader.GetDouble(13),
                BackgroundSamples = reader.GetInt32(14),
                PeakWorkingSetBytes = reader.GetInt64(15),
                ThreadCountTotal = reader.GetDouble(16),
                ThreadSamples = reader.GetInt32(17),
                PeakThreadCount = reader.GetInt32(18),
                IoReadOperations = reader.GetInt64(19),
                IoWriteOperations = reader.GetInt64(20),
                PeakDownloadBytesPerSecond = reader.GetInt64(21),
                PeakUploadBytesPerSecond = reader.GetInt64(22),
                PeakIoBytesPerSecond = reader.GetInt64(23)
            });
        }

        return result;
    }

    public IReadOnlyList<HistoryApplicationAggregate> LoadApplicationAggregates(IReadOnlyCollection<DateOnly> selectedDays)
    {
        if (selectedDays.Count == 0)
        {
            return [];
        }

        var ignoredProcesses = LoadIgnoredProcesses();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = BuildLoadApplicationAggregatesByDaysSql(selectedDays, ignoredProcesses, command);

        using var reader = command.ExecuteReader();
        var result = new List<HistoryApplicationAggregate>();
        while (reader.Read())
        {
            result.Add(new HistoryApplicationAggregate
            {
                ProcessName = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                ExecutablePath = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                ActiveDays = reader.GetInt32(2),
                ForegroundMilliseconds = reader.GetInt64(3),
                BackgroundMilliseconds = reader.GetInt64(4),
                DownloadBytes = reader.GetInt64(5),
                UploadBytes = reader.GetInt64(6),
                IoReadBytes = reader.GetInt64(7),
                IoWriteBytes = reader.GetInt64(8),
                ForegroundCpuTotal = reader.GetDouble(9),
                ForegroundWorkingSetTotal = reader.GetDouble(10),
                ForegroundSamples = reader.GetInt32(11),
                BackgroundCpuTotal = reader.GetDouble(12),
                BackgroundWorkingSetTotal = reader.GetDouble(13),
                BackgroundSamples = reader.GetInt32(14),
                PeakWorkingSetBytes = reader.GetInt64(15),
                ThreadCountTotal = reader.GetDouble(16),
                ThreadSamples = reader.GetInt32(17),
                PeakThreadCount = reader.GetInt32(18),
                IoReadOperations = reader.GetInt64(19),
                IoWriteOperations = reader.GetInt64(20),
                PeakDownloadBytesPerSecond = reader.GetInt64(21),
                PeakUploadBytesPerSecond = reader.GetInt64(22),
                PeakIoBytesPerSecond = reader.GetInt64(23)
            });
        }

        return result;
    }

    private List<HistoryDailyRecord> LoadApplicationDailyRecords(int maxDays)
    {
        var ignoredProcesses = LoadIgnoredProcesses();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = BuildLoadApplicationDailyRecordsSql(ignoredProcesses, command);
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

    private List<string> LoadIgnoredProcesses()
    {
        return _blacklistStore.Load()
            .Where(static item => item.Value == BlacklistEntryMode.Ignored)
            .Select(static item => item.Key)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string BuildLoadApplicationAggregatesSql(IReadOnlyList<string> ignoredProcesses, SqliteCommand command)
    {
        return $@"
WITH latest_paths AS (
    SELECT process_name, executable_path
    FROM (
        SELECT
            process_name,
            executable_path,
            ROW_NUMBER() OVER (
                PARTITION BY process_name
                ORDER BY day DESC
            ) AS path_rank
        FROM daily_process_activity
        WHERE TRIM(COALESCE(executable_path, '')) <> ''
    )
    WHERE path_rank = 1
)
SELECT
    d.process_name,
    COALESCE(lp.executable_path, '') AS executable_path,
    COUNT(*) AS active_days,
    COALESCE(SUM(d.foreground_milliseconds), 0) AS foreground_milliseconds,
    COALESCE(SUM(d.background_milliseconds), 0) AS background_milliseconds,
    COALESCE(SUM(d.download_bytes), 0) AS download_bytes,
    COALESCE(SUM(d.upload_bytes), 0) AS upload_bytes,
    COALESCE(SUM(d.io_read_bytes), 0) AS io_read_bytes,
    COALESCE(SUM(d.io_write_bytes), 0) AS io_write_bytes,
    COALESCE(SUM(d.foreground_cpu_total), 0) AS foreground_cpu_total,
    COALESCE(SUM(d.foreground_working_set_total), 0) AS foreground_working_set_total,
    COALESCE(SUM(d.foreground_samples), 0) AS foreground_samples,
    COALESCE(SUM(d.background_cpu_total), 0) AS background_cpu_total,
    COALESCE(SUM(d.background_working_set_total), 0) AS background_working_set_total,
    COALESCE(SUM(d.background_samples), 0) AS background_samples,
    COALESCE(MAX(d.peak_working_set_bytes), 0) AS peak_working_set_bytes,
    COALESCE(SUM(d.thread_count_total), 0) AS thread_count_total,
    COALESCE(SUM(d.thread_samples), 0) AS thread_samples,
    COALESCE(MAX(d.peak_thread_count), 0) AS peak_thread_count,
    COALESCE(SUM(d.io_read_operations), 0) AS io_read_operations,
    COALESCE(SUM(d.io_write_operations), 0) AS io_write_operations,
    COALESCE(MAX(d.peak_download_bytes_per_second), 0) AS peak_download_bytes_per_second,
    COALESCE(MAX(d.peak_upload_bytes_per_second), 0) AS peak_upload_bytes_per_second,
    COALESCE(MAX(d.peak_io_bytes_per_second), 0) AS peak_io_bytes_per_second
FROM daily_process_activity d
LEFT JOIN latest_paths lp
    ON lp.process_name = d.process_name
WHERE d.day >= $startDay
  AND d.day <= $endDay{BuildIgnoredProcessClause(ignoredProcesses, command, "d.process_name")}
GROUP BY d.process_name, lp.executable_path
ORDER BY d.process_name COLLATE NOCASE;";
    }

    private static string BuildLoadOverviewApplicationAggregatesSql(IReadOnlyList<string> ignoredProcesses, SqliteCommand command)
    {
        return $@"
WITH latest_paths AS (
    SELECT process_name, executable_path
    FROM (
        SELECT
            process_name,
            executable_path,
            ROW_NUMBER() OVER (
                PARTITION BY process_name
                ORDER BY day DESC
            ) AS path_rank
        FROM daily_process_activity
        WHERE TRIM(COALESCE(executable_path, '')) <> ''
    )
    WHERE path_rank = 1
)
SELECT
    d.process_name,
    COALESCE(lp.executable_path, '') AS executable_path,
    COALESCE(SUM(d.foreground_milliseconds), 0) AS foreground_milliseconds,
    COALESCE(SUM(d.background_milliseconds), 0) AS background_milliseconds,
    COALESCE(SUM(d.download_bytes), 0) AS download_bytes,
    COALESCE(SUM(d.upload_bytes), 0) AS upload_bytes,
    COALESCE(SUM(d.io_read_bytes), 0) AS io_read_bytes,
    COALESCE(SUM(d.io_write_bytes), 0) AS io_write_bytes
FROM daily_process_activity d
LEFT JOIN latest_paths lp
    ON lp.process_name = d.process_name
WHERE d.day >= $startDay
  AND d.day <= $endDay{BuildIgnoredProcessClause(ignoredProcesses, command, "d.process_name")}
GROUP BY d.process_name, lp.executable_path
ORDER BY d.process_name COLLATE NOCASE;";
    }

    private static string BuildLoadApplicationAggregatesByDaysSql(
        IReadOnlyCollection<DateOnly> selectedDays,
        IReadOnlyList<string> ignoredProcesses,
        SqliteCommand command)
    {
        var selectedDaysClause = BuildSelectedDaysClause(selectedDays, command);
        return $@"
WITH latest_paths AS (
    SELECT process_name, executable_path
    FROM (
        SELECT
            process_name,
            executable_path,
            ROW_NUMBER() OVER (
                PARTITION BY process_name
                ORDER BY day DESC
            ) AS path_rank
        FROM daily_process_activity
        WHERE TRIM(COALESCE(executable_path, '')) <> ''
    )
    WHERE path_rank = 1
)
SELECT
    d.process_name,
    COALESCE(lp.executable_path, '') AS executable_path,
    COUNT(*) AS active_days,
    COALESCE(SUM(d.foreground_milliseconds), 0) AS foreground_milliseconds,
    COALESCE(SUM(d.background_milliseconds), 0) AS background_milliseconds,
    COALESCE(SUM(d.download_bytes), 0) AS download_bytes,
    COALESCE(SUM(d.upload_bytes), 0) AS upload_bytes,
    COALESCE(SUM(d.io_read_bytes), 0) AS io_read_bytes,
    COALESCE(SUM(d.io_write_bytes), 0) AS io_write_bytes,
    COALESCE(SUM(d.foreground_cpu_total), 0) AS foreground_cpu_total,
    COALESCE(SUM(d.foreground_working_set_total), 0) AS foreground_working_set_total,
    COALESCE(SUM(d.foreground_samples), 0) AS foreground_samples,
    COALESCE(SUM(d.background_cpu_total), 0) AS background_cpu_total,
    COALESCE(SUM(d.background_working_set_total), 0) AS background_working_set_total,
    COALESCE(SUM(d.background_samples), 0) AS background_samples,
    COALESCE(MAX(d.peak_working_set_bytes), 0) AS peak_working_set_bytes,
    COALESCE(SUM(d.thread_count_total), 0) AS thread_count_total,
    COALESCE(SUM(d.thread_samples), 0) AS thread_samples,
    COALESCE(MAX(d.peak_thread_count), 0) AS peak_thread_count,
    COALESCE(SUM(d.io_read_operations), 0) AS io_read_operations,
    COALESCE(SUM(d.io_write_operations), 0) AS io_write_operations,
    COALESCE(MAX(d.peak_download_bytes_per_second), 0) AS peak_download_bytes_per_second,
    COALESCE(MAX(d.peak_upload_bytes_per_second), 0) AS peak_upload_bytes_per_second,
    COALESCE(MAX(d.peak_io_bytes_per_second), 0) AS peak_io_bytes_per_second
FROM daily_process_activity d
LEFT JOIN latest_paths lp
    ON lp.process_name = d.process_name
WHERE d.day IN ({selectedDaysClause}){BuildIgnoredProcessClause(ignoredProcesses, command, "d.process_name")}
GROUP BY d.process_name, lp.executable_path
ORDER BY d.process_name COLLATE NOCASE;";
    }

    private static string BuildLoadOverviewApplicationAggregatesByDaysSql(
        IReadOnlyCollection<DateOnly> selectedDays,
        IReadOnlyList<string> ignoredProcesses,
        SqliteCommand command)
    {
        var selectedDaysClause = BuildSelectedDaysClause(selectedDays, command);
        return $@"
WITH latest_paths AS (
    SELECT process_name, executable_path
    FROM (
        SELECT
            process_name,
            executable_path,
            ROW_NUMBER() OVER (
                PARTITION BY process_name
                ORDER BY day DESC
            ) AS path_rank
        FROM daily_process_activity
        WHERE TRIM(COALESCE(executable_path, '')) <> ''
    )
    WHERE path_rank = 1
)
SELECT
    d.process_name,
    COALESCE(lp.executable_path, '') AS executable_path,
    COALESCE(SUM(d.foreground_milliseconds), 0) AS foreground_milliseconds,
    COALESCE(SUM(d.background_milliseconds), 0) AS background_milliseconds,
    COALESCE(SUM(d.download_bytes), 0) AS download_bytes,
    COALESCE(SUM(d.upload_bytes), 0) AS upload_bytes,
    COALESCE(SUM(d.io_read_bytes), 0) AS io_read_bytes,
    COALESCE(SUM(d.io_write_bytes), 0) AS io_write_bytes
FROM daily_process_activity d
LEFT JOIN latest_paths lp
    ON lp.process_name = d.process_name
WHERE d.day IN ({selectedDaysClause}){BuildIgnoredProcessClause(ignoredProcesses, command, "d.process_name")}
GROUP BY d.process_name, lp.executable_path
ORDER BY d.process_name COLLATE NOCASE;";
    }

    private static string BuildLoadApplicationDailyRecordsSql(IReadOnlyList<string> ignoredProcesses, SqliteCommand command)
    {
        var dayRankedClause = BuildIgnoredProcessClause(ignoredProcesses, command, "process_name", "ranked");
        var dailyClause = BuildIgnoredProcessClause(ignoredProcesses, command, "d.process_name", "daily");

        return $@"
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
    WHERE 1 = 1{dayRankedClause}
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
WHERE 1 = 1{dailyClause}
GROUP BY d.day
ORDER BY d.day DESC
LIMIT $limit;";
    }

    private static string BuildIgnoredProcessClause(
        IReadOnlyList<string> ignoredProcesses,
        SqliteCommand command,
        string columnExpression,
        string parameterPrefix = "ignored")
    {
        if (ignoredProcesses.Count == 0)
        {
            return string.Empty;
        }

        var parameterNames = new string[ignoredProcesses.Count];
        for (var i = 0; i < ignoredProcesses.Count; i++)
        {
            parameterNames[i] = $"${parameterPrefix}Process{i}";
            command.Parameters.AddWithValue(parameterNames[i], ignoredProcesses[i]);
        }

        return $" AND {columnExpression} NOT IN ({string.Join(", ", parameterNames)})";
    }

    private static string BuildSelectedDaysClause(IReadOnlyCollection<DateOnly> selectedDays, SqliteCommand command)
    {
        var orderedDays = selectedDays
            .Distinct()
            .OrderBy(static day => day)
            .ToArray();
        var parameterNames = new string[orderedDays.Length];

        for (var i = 0; i < orderedDays.Length; i++)
        {
            parameterNames[i] = $"$selectedDay{i}";
            command.Parameters.AddWithValue(parameterNames[i], orderedDays[i].ToString("yyyy-MM-dd"));
        }

        return string.Join(", ", parameterNames);
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

public readonly record struct HistoryOverviewApplicationAggregate
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

public readonly record struct HistoryApplicationAggregate
{
    public string ProcessName { get; init; }
    public string ExecutablePath { get; init; }
    public int ActiveDays { get; init; }
    public long ForegroundMilliseconds { get; init; }
    public long BackgroundMilliseconds { get; init; }
    public long DownloadBytes { get; init; }
    public long UploadBytes { get; init; }
    public long IoReadBytes { get; init; }
    public long IoWriteBytes { get; init; }
    public double ForegroundCpuTotal { get; init; }
    public double ForegroundWorkingSetTotal { get; init; }
    public int ForegroundSamples { get; init; }
    public double BackgroundCpuTotal { get; init; }
    public double BackgroundWorkingSetTotal { get; init; }
    public int BackgroundSamples { get; init; }
    public long PeakWorkingSetBytes { get; init; }
    public double ThreadCountTotal { get; init; }
    public int ThreadSamples { get; init; }
    public int PeakThreadCount { get; init; }
    public long IoReadOperations { get; init; }
    public long IoWriteOperations { get; init; }
    public long PeakDownloadBytesPerSecond { get; init; }
    public long PeakUploadBytesPerSecond { get; init; }
    public long PeakIoBytesPerSecond { get; init; }

    public long TotalUsageMilliseconds => ForegroundMilliseconds + BackgroundMilliseconds;
    public long TotalTrafficBytes => DownloadBytes + UploadBytes;
    public long TotalIoBytes => IoReadBytes + IoWriteBytes;
    public long TotalIoOperations => IoReadOperations + IoWriteOperations;
    public long PeakTrafficBytesPerSecond => PeakDownloadBytesPerSecond + PeakUploadBytesPerSecond;
    public double ForegroundRatio => TotalUsageMilliseconds > 0 ? ForegroundMilliseconds / (double)TotalUsageMilliseconds : 0d;
    public double AverageWorkingSetBytes
    {
        get
        {
            var totalSamples = ForegroundSamples + BackgroundSamples;
            return totalSamples > 0
                ? (ForegroundWorkingSetTotal + BackgroundWorkingSetTotal) / totalSamples
                : 0d;
        }
    }

    public double AverageCpu
    {
        get
        {
            var totalSamples = ForegroundSamples + BackgroundSamples;
            return totalSamples > 0
                ? (ForegroundCpuTotal + BackgroundCpuTotal) / totalSamples
                : 0d;
        }
    }

    public double AverageThreadCount => ThreadSamples > 0 ? ThreadCountTotal / ThreadSamples : 0d;
    public double ThreadPeakMeanRatio => AverageThreadCount > 0 ? PeakThreadCount / AverageThreadCount : 0d;
    public double AverageIops => TotalUsageMilliseconds > 0 ? TotalIoOperations / (TotalUsageMilliseconds / 1000d) : 0d;
}
