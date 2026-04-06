using Microsoft.Data.Sqlite;

namespace APPvista.Infrastructure.Persistence;

internal static class SqliteMonitoringDatabase
{
    public static void EnsureCreated(string databasePath)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var connection = OpenConnection(databasePath);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = @"
CREATE TABLE IF NOT EXISTS blacklist_entries (
    process_name TEXT NOT NULL COLLATE NOCASE PRIMARY KEY,
    mode INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS daily_process_activity (
    day TEXT NOT NULL,
    process_name TEXT NOT NULL COLLATE NOCASE,
    executable_path TEXT NOT NULL DEFAULT '',
    foreground_milliseconds INTEGER NOT NULL DEFAULT 0,
    background_milliseconds INTEGER NOT NULL DEFAULT 0,
    download_bytes INTEGER NOT NULL DEFAULT 0,
    upload_bytes INTEGER NOT NULL DEFAULT 0,
    peak_download_bytes_per_second INTEGER NOT NULL DEFAULT 0,
    peak_upload_bytes_per_second INTEGER NOT NULL DEFAULT 0,
    foreground_cpu_total REAL NOT NULL DEFAULT 0,
    foreground_working_set_total REAL NOT NULL DEFAULT 0,
    foreground_samples INTEGER NOT NULL DEFAULT 0,
    background_cpu_total REAL NOT NULL DEFAULT 0,
    background_working_set_total REAL NOT NULL DEFAULT 0,
    background_samples INTEGER NOT NULL DEFAULT 0,
    peak_working_set_bytes INTEGER NOT NULL DEFAULT 0,
    thread_count_total REAL NOT NULL DEFAULT 0,
    thread_samples INTEGER NOT NULL DEFAULT 0,
    peak_thread_count INTEGER NOT NULL DEFAULT 0,
    io_read_bytes INTEGER NOT NULL DEFAULT 0,
    io_write_bytes INTEGER NOT NULL DEFAULT 0,
    foreground_io_operations INTEGER NOT NULL DEFAULT 0,
    background_io_operations INTEGER NOT NULL DEFAULT 0,
    io_read_operations INTEGER NOT NULL DEFAULT 0,
    io_write_operations INTEGER NOT NULL DEFAULT 0,
    peak_io_read_bytes_per_second INTEGER NOT NULL DEFAULT 0,
    peak_io_write_bytes_per_second INTEGER NOT NULL DEFAULT 0,
    peak_io_bytes_per_second INTEGER NOT NULL DEFAULT 0,
    has_main_window INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (day, process_name)
);
";
            command.ExecuteNonQuery();
        }

        EnsureColumn(connection, "daily_process_activity", "executable_path", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(connection, "daily_process_activity", "thread_count_total", "REAL NOT NULL DEFAULT 0");
        EnsureColumn(connection, "daily_process_activity", "foreground_cpu_total", "REAL NOT NULL DEFAULT 0");
        EnsureColumn(connection, "daily_process_activity", "foreground_working_set_total", "REAL NOT NULL DEFAULT 0");
        EnsureColumn(connection, "daily_process_activity", "foreground_samples", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "daily_process_activity", "background_cpu_total", "REAL NOT NULL DEFAULT 0");
        EnsureColumn(connection, "daily_process_activity", "background_working_set_total", "REAL NOT NULL DEFAULT 0");
        EnsureColumn(connection, "daily_process_activity", "background_samples", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "daily_process_activity", "peak_working_set_bytes", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "daily_process_activity", "thread_samples", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "daily_process_activity", "peak_thread_count", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "daily_process_activity", "foreground_io_operations", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "daily_process_activity", "background_io_operations", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "daily_process_activity", "io_read_operations", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "daily_process_activity", "io_write_operations", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "daily_process_activity", "peak_io_read_bytes_per_second", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "daily_process_activity", "peak_io_write_bytes_per_second", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "daily_process_activity", "peak_io_bytes_per_second", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "daily_process_activity", "has_main_window", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "blacklist_entries", "mode", "INTEGER NOT NULL DEFAULT 1");
    }

    public static SqliteConnection OpenConnection(string databasePath)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true
        };

        var connection = new SqliteConnection(builder.ConnectionString);
        connection.Open();
        return connection;
    }

    private static void EnsureColumn(SqliteConnection connection, string tableName, string columnName, string columnDefinition)
    {
        using var checkCommand = connection.CreateCommand();
        checkCommand.CommandText = $"PRAGMA table_info({tableName});";

        using var reader = checkCommand.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        using var alterCommand = connection.CreateCommand();
        alterCommand.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};";
        alterCommand.ExecuteNonQuery();
    }
}
