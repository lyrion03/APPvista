using APPvista.Application.Abstractions;

namespace APPvista.Infrastructure.Persistence;

public sealed class SqliteBlacklistStore : IBlacklistStore
{
    private readonly string _databasePath;
    private readonly string? _legacyFilePath;
    private readonly object _sync = new();

    public SqliteBlacklistStore(string databasePath, string? legacyFilePath = null)
    {
        _databasePath = databasePath;
        _legacyFilePath = legacyFilePath;

        lock (_sync)
        {
            SqliteMonitoringDatabase.EnsureCreated(_databasePath);
            ImportLegacyDataIfNeeded();
        }
    }

    public IReadOnlyDictionary<string, BlacklistEntryMode> Load()
    {
        lock (_sync)
        {
            using var connection = SqliteMonitoringDatabase.OpenConnection(_databasePath);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT process_name, mode FROM blacklist_entries ORDER BY process_name COLLATE NOCASE;";

            using var reader = command.ExecuteReader();
            var items = new Dictionary<string, BlacklistEntryMode>(StringComparer.OrdinalIgnoreCase);
            while (reader.Read())
            {
                var processName = reader.GetString(0);
                if (string.IsNullOrWhiteSpace(processName))
                {
                    continue;
                }

                var modeValue = reader.IsDBNull(1) ? (int)BlacklistEntryMode.Hidden : reader.GetInt32(1);
                var mode = Enum.IsDefined(typeof(BlacklistEntryMode), modeValue)
                    ? (BlacklistEntryMode)modeValue
                    : BlacklistEntryMode.Hidden;
                items[processName] = mode;
            }

            return items;
        }
    }

    public void Save(IEnumerable<BlacklistEntry> entries)
    {
        lock (_sync)
        {
            var items = entries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.ProcessName))
                .GroupBy(entry => entry.ProcessName.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    var entry = group.Last();
                    return new BlacklistEntry(group.Key, entry.Mode);
                })
                .OrderBy(static entry => entry.ProcessName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            using var connection = SqliteMonitoringDatabase.OpenConnection(_databasePath);
            using var transaction = connection.BeginTransaction();

            using (var deleteCommand = connection.CreateCommand())
            {
                deleteCommand.Transaction = transaction;
                deleteCommand.CommandText = "DELETE FROM blacklist_entries;";
                deleteCommand.ExecuteNonQuery();
            }

            foreach (var item in items)
            {
                using var insertCommand = connection.CreateCommand();
                insertCommand.Transaction = transaction;
                insertCommand.CommandText = "INSERT INTO blacklist_entries (process_name, mode) VALUES ($processName, $mode);";
                insertCommand.Parameters.AddWithValue("$processName", item.ProcessName);
                insertCommand.Parameters.AddWithValue("$mode", (int)item.Mode);
                insertCommand.ExecuteNonQuery();
            }

            transaction.Commit();
        }
    }

    private void ImportLegacyDataIfNeeded()
    {
        if (string.IsNullOrWhiteSpace(_legacyFilePath) || !File.Exists(_legacyFilePath))
        {
            return;
        }

        using var connection = SqliteMonitoringDatabase.OpenConnection(_databasePath);
        using var countCommand = connection.CreateCommand();
        countCommand.CommandText = "SELECT COUNT(*) FROM blacklist_entries;";
        var existingCount = (long)(countCommand.ExecuteScalar() ?? 0L);
        if (existingCount > 0)
        {
            return;
        }

        var legacyStore = new FileBlacklistStore(_legacyFilePath);
        var legacyItems = legacyStore.Load();
        if (legacyItems.Count == 0)
        {
            return;
        }

        Save(legacyItems.Select(static item => new BlacklistEntry(item.Key, item.Value)));
    }
}
