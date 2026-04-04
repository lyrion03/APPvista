using Microsoft.Data.Sqlite;
using WinFormsApp1.Domain.Entities;

namespace WinFormsApp1.Desktop.Services;

public sealed class ApplicationHistoryAnalysisProvider
{
    private readonly string _databasePath;

    public ApplicationHistoryAnalysisProvider(string databasePath)
    {
        _databasePath = databasePath;
    }

    public IReadOnlyList<DailyProcessActivitySummary> LoadDailyRecords(string processName, int maxDays)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return [];
        }

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT
    day,
    process_name,
    MAX(executable_path) AS executable_path,
    COALESCE(SUM(foreground_milliseconds), 0) AS foreground_milliseconds,
    COALESCE(SUM(background_milliseconds), 0) AS background_milliseconds,
    COALESCE(SUM(download_bytes), 0) AS download_bytes,
    COALESCE(SUM(upload_bytes), 0) AS upload_bytes,
    COALESCE(MAX(peak_download_bytes_per_second), 0) AS peak_download_bytes_per_second,
    COALESCE(MAX(peak_upload_bytes_per_second), 0) AS peak_upload_bytes_per_second,
    COALESCE(SUM(foreground_cpu_total), 0) AS foreground_cpu_total,
    COALESCE(SUM(foreground_working_set_total), 0) AS foreground_working_set_total,
    COALESCE(SUM(foreground_samples), 0) AS foreground_samples,
    COALESCE(SUM(background_cpu_total), 0) AS background_cpu_total,
    COALESCE(SUM(background_working_set_total), 0) AS background_working_set_total,
    COALESCE(SUM(background_samples), 0) AS background_samples,
    COALESCE(MAX(peak_working_set_bytes), 0) AS peak_working_set_bytes,
    COALESCE(SUM(thread_count_total), 0) AS thread_count_total,
    COALESCE(SUM(thread_samples), 0) AS thread_samples,
    COALESCE(MAX(peak_thread_count), 0) AS peak_thread_count,
    COALESCE(SUM(io_read_bytes), 0) AS io_read_bytes,
    COALESCE(SUM(io_write_bytes), 0) AS io_write_bytes,
    COALESCE(SUM(foreground_io_operations), 0) AS foreground_io_operations,
    COALESCE(SUM(background_io_operations), 0) AS background_io_operations,
    COALESCE(SUM(io_read_operations), 0) AS io_read_operations,
    COALESCE(SUM(io_write_operations), 0) AS io_write_operations,
    COALESCE(MAX(peak_io_read_bytes_per_second), 0) AS peak_io_read_bytes_per_second,
    COALESCE(MAX(peak_io_write_bytes_per_second), 0) AS peak_io_write_bytes_per_second,
    COALESCE(MAX(peak_io_bytes_per_second), 0) AS peak_io_bytes_per_second,
    COALESCE(MAX(has_main_window), 0) AS has_main_window
FROM daily_process_activity
WHERE process_name = $processName
GROUP BY day, process_name
ORDER BY day DESC
LIMIT $limit;";
        command.Parameters.AddWithValue("$processName", processName);
        command.Parameters.AddWithValue("$limit", maxDays);

        using var reader = command.ExecuteReader();
        var records = new List<DailyProcessActivitySummary>();
        while (reader.Read())
        {
            records.Add(new DailyProcessActivitySummary
            {
                Day = reader.GetString(0),
                ProcessName = reader.GetString(1),
                ExecutablePath = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                ForegroundMilliseconds = reader.GetInt64(3),
                BackgroundMilliseconds = reader.GetInt64(4),
                DownloadBytes = reader.GetInt64(5),
                UploadBytes = reader.GetInt64(6),
                PeakDownloadBytesPerSecond = reader.GetInt64(7),
                PeakUploadBytesPerSecond = reader.GetInt64(8),
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
                IoReadBytes = reader.GetInt64(19),
                IoWriteBytes = reader.GetInt64(20),
                ForegroundIoOperations = reader.GetInt64(21),
                BackgroundIoOperations = reader.GetInt64(22),
                IoReadOperations = reader.GetInt64(23),
                IoWriteOperations = reader.GetInt64(24),
                PeakIoReadBytesPerSecond = reader.GetInt64(25),
                PeakIoWriteBytesPerSecond = reader.GetInt64(26),
                PeakIoBytesPerSecond = reader.GetInt64(27),
                HasMainWindow = reader.GetInt64(28) > 0
            });
        }

        return records;
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
