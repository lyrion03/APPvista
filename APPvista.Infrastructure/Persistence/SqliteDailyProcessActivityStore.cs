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

    public void Delete(DateOnly day, IEnumerable<string> processNames)
    {
        lock (_sync)
        {
            var normalizedDay = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var items = processNames
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Select(static item => item.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (items.Count == 0)
            {
                return;
            }

            using var connection = SqliteMonitoringDatabase.OpenConnection(_databasePath);
            using var transaction = connection.BeginTransaction();
            using var deleteCommand = connection.CreateCommand();
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = @"
DELETE FROM daily_process_activity
WHERE day = $day
  AND process_name = $processName;";
            deleteCommand.Parameters.AddWithValue("$day", normalizedDay);
            var processNameParameter = deleteCommand.Parameters.Add("$processName", Microsoft.Data.Sqlite.SqliteType.Text);
            deleteCommand.Prepare();

            foreach (var processName in items)
            {
                processNameParameter.Value = processName;
                deleteCommand.ExecuteNonQuery();
            }

            transaction.Commit();
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
            var processNameParameter = insertCommand.Parameters.Add("$processName", Microsoft.Data.Sqlite.SqliteType.Text);
            var executablePathParameter = insertCommand.Parameters.Add("$executablePath", Microsoft.Data.Sqlite.SqliteType.Text);
            var foregroundMillisecondsParameter = insertCommand.Parameters.Add("$foregroundMilliseconds", Microsoft.Data.Sqlite.SqliteType.Integer);
            var backgroundMillisecondsParameter = insertCommand.Parameters.Add("$backgroundMilliseconds", Microsoft.Data.Sqlite.SqliteType.Integer);
            var downloadBytesParameter = insertCommand.Parameters.Add("$downloadBytes", Microsoft.Data.Sqlite.SqliteType.Integer);
            var uploadBytesParameter = insertCommand.Parameters.Add("$uploadBytes", Microsoft.Data.Sqlite.SqliteType.Integer);
            var peakDownloadBytesPerSecondParameter = insertCommand.Parameters.Add("$peakDownloadBytesPerSecond", Microsoft.Data.Sqlite.SqliteType.Integer);
            var peakUploadBytesPerSecondParameter = insertCommand.Parameters.Add("$peakUploadBytesPerSecond", Microsoft.Data.Sqlite.SqliteType.Integer);
            var foregroundCpuTotalParameter = insertCommand.Parameters.Add("$foregroundCpuTotal", Microsoft.Data.Sqlite.SqliteType.Real);
            var foregroundWorkingSetTotalParameter = insertCommand.Parameters.Add("$foregroundWorkingSetTotal", Microsoft.Data.Sqlite.SqliteType.Real);
            var foregroundSamplesParameter = insertCommand.Parameters.Add("$foregroundSamples", Microsoft.Data.Sqlite.SqliteType.Integer);
            var backgroundCpuTotalParameter = insertCommand.Parameters.Add("$backgroundCpuTotal", Microsoft.Data.Sqlite.SqliteType.Real);
            var backgroundWorkingSetTotalParameter = insertCommand.Parameters.Add("$backgroundWorkingSetTotal", Microsoft.Data.Sqlite.SqliteType.Real);
            var backgroundSamplesParameter = insertCommand.Parameters.Add("$backgroundSamples", Microsoft.Data.Sqlite.SqliteType.Integer);
            var peakWorkingSetBytesParameter = insertCommand.Parameters.Add("$peakWorkingSetBytes", Microsoft.Data.Sqlite.SqliteType.Integer);
            var threadCountTotalParameter = insertCommand.Parameters.Add("$threadCountTotal", Microsoft.Data.Sqlite.SqliteType.Real);
            var threadSamplesParameter = insertCommand.Parameters.Add("$threadSamples", Microsoft.Data.Sqlite.SqliteType.Integer);
            var peakThreadCountParameter = insertCommand.Parameters.Add("$peakThreadCount", Microsoft.Data.Sqlite.SqliteType.Integer);
            var ioReadBytesParameter = insertCommand.Parameters.Add("$ioReadBytes", Microsoft.Data.Sqlite.SqliteType.Integer);
            var ioWriteBytesParameter = insertCommand.Parameters.Add("$ioWriteBytes", Microsoft.Data.Sqlite.SqliteType.Integer);
            var foregroundIoOperationsParameter = insertCommand.Parameters.Add("$foregroundIoOperations", Microsoft.Data.Sqlite.SqliteType.Integer);
            var backgroundIoOperationsParameter = insertCommand.Parameters.Add("$backgroundIoOperations", Microsoft.Data.Sqlite.SqliteType.Integer);
            var ioReadOperationsParameter = insertCommand.Parameters.Add("$ioReadOperations", Microsoft.Data.Sqlite.SqliteType.Integer);
            var ioWriteOperationsParameter = insertCommand.Parameters.Add("$ioWriteOperations", Microsoft.Data.Sqlite.SqliteType.Integer);
            var peakIoReadBytesPerSecondParameter = insertCommand.Parameters.Add("$peakIoReadBytesPerSecond", Microsoft.Data.Sqlite.SqliteType.Integer);
            var peakIoWriteBytesPerSecondParameter = insertCommand.Parameters.Add("$peakIoWriteBytesPerSecond", Microsoft.Data.Sqlite.SqliteType.Integer);
            var peakIoBytesPerSecondParameter = insertCommand.Parameters.Add("$peakIoBytesPerSecond", Microsoft.Data.Sqlite.SqliteType.Integer);
            var hasMainWindowParameter = insertCommand.Parameters.Add("$hasMainWindow", Microsoft.Data.Sqlite.SqliteType.Integer);
            insertCommand.Prepare();

            foreach (var summary in items)
            {
                processNameParameter.Value = summary.ProcessName.Trim();
                executablePathParameter.Value = summary.ExecutablePath ?? string.Empty;
                foregroundMillisecondsParameter.Value = summary.ForegroundMilliseconds;
                backgroundMillisecondsParameter.Value = summary.BackgroundMilliseconds;
                downloadBytesParameter.Value = summary.DownloadBytes;
                uploadBytesParameter.Value = summary.UploadBytes;
                peakDownloadBytesPerSecondParameter.Value = summary.PeakDownloadBytesPerSecond;
                peakUploadBytesPerSecondParameter.Value = summary.PeakUploadBytesPerSecond;
                foregroundCpuTotalParameter.Value = summary.ForegroundCpuTotal;
                foregroundWorkingSetTotalParameter.Value = summary.ForegroundWorkingSetTotal;
                foregroundSamplesParameter.Value = summary.ForegroundSamples;
                backgroundCpuTotalParameter.Value = summary.BackgroundCpuTotal;
                backgroundWorkingSetTotalParameter.Value = summary.BackgroundWorkingSetTotal;
                backgroundSamplesParameter.Value = summary.BackgroundSamples;
                peakWorkingSetBytesParameter.Value = summary.PeakWorkingSetBytes;
                threadCountTotalParameter.Value = summary.ThreadCountTotal;
                threadSamplesParameter.Value = summary.ThreadSamples;
                peakThreadCountParameter.Value = summary.PeakThreadCount;
                ioReadBytesParameter.Value = summary.IoReadBytes;
                ioWriteBytesParameter.Value = summary.IoWriteBytes;
                foregroundIoOperationsParameter.Value = summary.ForegroundIoOperations;
                backgroundIoOperationsParameter.Value = summary.BackgroundIoOperations;
                ioReadOperationsParameter.Value = summary.IoReadOperations;
                ioWriteOperationsParameter.Value = summary.IoWriteOperations;
                peakIoReadBytesPerSecondParameter.Value = summary.PeakIoReadBytesPerSecond;
                peakIoWriteBytesPerSecondParameter.Value = summary.PeakIoWriteBytesPerSecond;
                peakIoBytesPerSecondParameter.Value = summary.PeakIoBytesPerSecond;
                hasMainWindowParameter.Value = summary.HasMainWindow ? 1 : 0;
                insertCommand.ExecuteNonQuery();
            }

            transaction.Commit();
        }
    }
}
