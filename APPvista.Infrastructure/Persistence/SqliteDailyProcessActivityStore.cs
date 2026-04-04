using System.Globalization;
using APPvista.Application.Abstractions;
using APPvista.Domain.Entities;

namespace APPvista.Infrastructure.Persistence;

public sealed class SqliteDailyProcessActivityStore : IDailyProcessActivityStore
{
    private readonly string _databasePath;
    private readonly object _sync = new();

    public SqliteDailyProcessActivityStore(string databasePath)
    {
        _databasePath = databasePath;

        lock (_sync)
        {
            SqliteMonitoringDatabase.EnsureCreated(_databasePath);
        }
    }

    public IReadOnlyList<DailyProcessActivitySummary> Load(DateOnly day)
    {
        lock (_sync)
        {
            using var connection = SqliteMonitoringDatabase.OpenConnection(_databasePath);
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT day,
       process_name,
       executable_path,
       foreground_milliseconds,
       background_milliseconds,
       download_bytes,
       upload_bytes,
       peak_download_bytes_per_second,
       peak_upload_bytes_per_second,
       foreground_cpu_total,
       foreground_working_set_total,
       foreground_samples,
       background_cpu_total,
       background_working_set_total,
       background_samples,
       peak_working_set_bytes,
       thread_count_total,
       thread_samples,
       peak_thread_count,
       io_read_bytes,
       io_write_bytes,
       foreground_io_operations,
       background_io_operations,
       io_read_operations,
       io_write_operations,
       peak_io_read_bytes_per_second,
       peak_io_write_bytes_per_second,
       peak_io_bytes_per_second,
       has_main_window
FROM daily_process_activity
WHERE day = $day
ORDER BY foreground_milliseconds DESC, background_milliseconds DESC, process_name COLLATE NOCASE;";
            command.Parameters.AddWithValue("$day", day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

            using var reader = command.ExecuteReader();
            var summaries = new List<DailyProcessActivitySummary>();
            while (reader.Read())
            {
                summaries.Add(new DailyProcessActivitySummary
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
                    HasMainWindow = !reader.IsDBNull(28) && reader.GetInt64(28) != 0
                });
            }

            return summaries;
        }
    }

    public void Save(DateOnly day, IEnumerable<DailyProcessActivitySummary> summaries)
    {
        lock (_sync)
        {
            var normalizedDay = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var items = summaries
                .Where(summary => !string.IsNullOrWhiteSpace(summary.ProcessName))
                .GroupBy(summary => summary.ProcessName.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .ToList();

            using var connection = SqliteMonitoringDatabase.OpenConnection(_databasePath);
            using var transaction = connection.BeginTransaction();

            foreach (var summary in items)
            {
                using var insertCommand = connection.CreateCommand();
                insertCommand.Transaction = transaction;
                insertCommand.CommandText = @"
INSERT INTO daily_process_activity (
    day,
    process_name,
    executable_path,
    foreground_milliseconds,
    background_milliseconds,
    download_bytes,
    upload_bytes,
    peak_download_bytes_per_second,
    peak_upload_bytes_per_second,
    foreground_cpu_total,
    foreground_working_set_total,
    foreground_samples,
    background_cpu_total,
    background_working_set_total,
    background_samples,
    peak_working_set_bytes,
    thread_count_total,
    thread_samples,
    peak_thread_count,
    io_read_bytes,
    io_write_bytes,
    foreground_io_operations,
    background_io_operations,
    io_read_operations,
    io_write_operations,
    peak_io_read_bytes_per_second,
    peak_io_write_bytes_per_second,
    peak_io_bytes_per_second,
    has_main_window
) VALUES (
    $day,
    $processName,
    $executablePath,
    $foregroundMilliseconds,
    $backgroundMilliseconds,
    $downloadBytes,
    $uploadBytes,
    $peakDownloadBytesPerSecond,
    $peakUploadBytesPerSecond,
    $foregroundCpuTotal,
    $foregroundWorkingSetTotal,
    $foregroundSamples,
    $backgroundCpuTotal,
    $backgroundWorkingSetTotal,
    $backgroundSamples,
    $peakWorkingSetBytes,
    $threadCountTotal,
    $threadSamples,
    $peakThreadCount,
    $ioReadBytes,
    $ioWriteBytes,
    $foregroundIoOperations,
    $backgroundIoOperations,
    $ioReadOperations,
    $ioWriteOperations,
    $peakIoReadBytesPerSecond,
    $peakIoWriteBytesPerSecond,
    $peakIoBytesPerSecond,
    $hasMainWindow
)
ON CONFLICT(day, process_name) DO UPDATE SET
    executable_path = excluded.executable_path,
    foreground_milliseconds = excluded.foreground_milliseconds,
    background_milliseconds = excluded.background_milliseconds,
    download_bytes = excluded.download_bytes,
    upload_bytes = excluded.upload_bytes,
    peak_download_bytes_per_second = excluded.peak_download_bytes_per_second,
    peak_upload_bytes_per_second = excluded.peak_upload_bytes_per_second,
    foreground_cpu_total = excluded.foreground_cpu_total,
    foreground_working_set_total = excluded.foreground_working_set_total,
    foreground_samples = excluded.foreground_samples,
    background_cpu_total = excluded.background_cpu_total,
    background_working_set_total = excluded.background_working_set_total,
    background_samples = excluded.background_samples,
    peak_working_set_bytes = excluded.peak_working_set_bytes,
    thread_count_total = excluded.thread_count_total,
    thread_samples = excluded.thread_samples,
    peak_thread_count = excluded.peak_thread_count,
    io_read_bytes = excluded.io_read_bytes,
    io_write_bytes = excluded.io_write_bytes,
    foreground_io_operations = excluded.foreground_io_operations,
    background_io_operations = excluded.background_io_operations,
    io_read_operations = excluded.io_read_operations,
    io_write_operations = excluded.io_write_operations,
    peak_io_read_bytes_per_second = excluded.peak_io_read_bytes_per_second,
    peak_io_write_bytes_per_second = excluded.peak_io_write_bytes_per_second,
    peak_io_bytes_per_second = excluded.peak_io_bytes_per_second,
    has_main_window = excluded.has_main_window;";
                insertCommand.Parameters.AddWithValue("$day", normalizedDay);
                insertCommand.Parameters.AddWithValue("$processName", summary.ProcessName.Trim());
                insertCommand.Parameters.AddWithValue("$executablePath", summary.ExecutablePath ?? string.Empty);
                insertCommand.Parameters.AddWithValue("$foregroundMilliseconds", summary.ForegroundMilliseconds);
                insertCommand.Parameters.AddWithValue("$backgroundMilliseconds", summary.BackgroundMilliseconds);
                insertCommand.Parameters.AddWithValue("$downloadBytes", summary.DownloadBytes);
                insertCommand.Parameters.AddWithValue("$uploadBytes", summary.UploadBytes);
                insertCommand.Parameters.AddWithValue("$peakDownloadBytesPerSecond", summary.PeakDownloadBytesPerSecond);
                insertCommand.Parameters.AddWithValue("$peakUploadBytesPerSecond", summary.PeakUploadBytesPerSecond);
                insertCommand.Parameters.AddWithValue("$foregroundCpuTotal", summary.ForegroundCpuTotal);
                insertCommand.Parameters.AddWithValue("$foregroundWorkingSetTotal", summary.ForegroundWorkingSetTotal);
                insertCommand.Parameters.AddWithValue("$foregroundSamples", summary.ForegroundSamples);
                insertCommand.Parameters.AddWithValue("$backgroundCpuTotal", summary.BackgroundCpuTotal);
                insertCommand.Parameters.AddWithValue("$backgroundWorkingSetTotal", summary.BackgroundWorkingSetTotal);
                insertCommand.Parameters.AddWithValue("$backgroundSamples", summary.BackgroundSamples);
                insertCommand.Parameters.AddWithValue("$peakWorkingSetBytes", summary.PeakWorkingSetBytes);
                insertCommand.Parameters.AddWithValue("$threadCountTotal", summary.ThreadCountTotal);
                insertCommand.Parameters.AddWithValue("$threadSamples", summary.ThreadSamples);
                insertCommand.Parameters.AddWithValue("$peakThreadCount", summary.PeakThreadCount);
                insertCommand.Parameters.AddWithValue("$ioReadBytes", summary.IoReadBytes);
                insertCommand.Parameters.AddWithValue("$ioWriteBytes", summary.IoWriteBytes);
                insertCommand.Parameters.AddWithValue("$foregroundIoOperations", summary.ForegroundIoOperations);
                insertCommand.Parameters.AddWithValue("$backgroundIoOperations", summary.BackgroundIoOperations);
                insertCommand.Parameters.AddWithValue("$ioReadOperations", summary.IoReadOperations);
                insertCommand.Parameters.AddWithValue("$ioWriteOperations", summary.IoWriteOperations);
                insertCommand.Parameters.AddWithValue("$peakIoReadBytesPerSecond", summary.PeakIoReadBytesPerSecond);
                insertCommand.Parameters.AddWithValue("$peakIoWriteBytesPerSecond", summary.PeakIoWriteBytesPerSecond);
                insertCommand.Parameters.AddWithValue("$peakIoBytesPerSecond", summary.PeakIoBytesPerSecond);
                insertCommand.Parameters.AddWithValue("$hasMainWindow", summary.HasMainWindow ? 1 : 0);
                insertCommand.ExecuteNonQuery();
            }

            transaction.Commit();
        }
    }
}
