using WinFormsApp1.Application.Abstractions;

namespace WinFormsApp1.Infrastructure.Persistence;

public sealed class SqliteWhitelistStore : IWhitelistStore
{
    private readonly string _databasePath;
    private readonly string? _legacyFilePath;
    private readonly object _sync = new();

    public SqliteWhitelistStore(string databasePath, string? legacyFilePath = null)
    {
        _databasePath = databasePath;
        _legacyFilePath = legacyFilePath;

        lock (_sync)
        {
            SqliteMonitoringDatabase.EnsureCreated(_databasePath);
            ImportLegacyDataIfNeeded();
        }
    }

    public IReadOnlySet<string> Load()
    {
        lock (_sync)
        {
            using var connection = SqliteMonitoringDatabase.OpenConnection(_databasePath);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT process_name FROM whitelist_entries ORDER BY process_name COLLATE NOCASE;";

            using var reader = command.ExecuteReader();
            var items = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (reader.Read())
            {
                var processName = reader.GetString(0);
                if (!string.IsNullOrWhiteSpace(processName))
                {
                    items.Add(processName);
                }
            }

            return items;
        }
    }

    public void Save(IEnumerable<string> processNames)
    {
        lock (_sync)
        {
            var items = processNames
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                .ToList();

            using var connection = SqliteMonitoringDatabase.OpenConnection(_databasePath);
            using var transaction = connection.BeginTransaction();

            using (var deleteCommand = connection.CreateCommand())
            {
                deleteCommand.Transaction = transaction;
                deleteCommand.CommandText = "DELETE FROM whitelist_entries;";
                deleteCommand.ExecuteNonQuery();
            }

            foreach (var item in items)
            {
                using var insertCommand = connection.CreateCommand();
                insertCommand.Transaction = transaction;
                insertCommand.CommandText = "INSERT INTO whitelist_entries (process_name) VALUES ($processName);";
                insertCommand.Parameters.AddWithValue("$processName", item);
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
        countCommand.CommandText = "SELECT COUNT(*) FROM whitelist_entries;";
        var existingCount = (long)(countCommand.ExecuteScalar() ?? 0L);
        if (existingCount > 0)
        {
            return;
        }

        var legacyStore = new FileWhitelistStore(_legacyFilePath);
        var legacyItems = legacyStore.Load();
        if (legacyItems.Count == 0)
        {
            return;
        }

        Save(legacyItems);
    }
}
