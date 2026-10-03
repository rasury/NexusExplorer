using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NexusExplorer.Services;

namespace NexusExplorer.Data;

/// <summary>Versioned, transactional adoption of the original EnsureCreated database.</summary>
public static class DatabaseInitializer
{
    public const int SchemaVersion = 2;
    public static async Task InitializeAsync(AppDbContext db, string path)
    {
        var connection = (SqliteConnection)db.Database.GetDbConnection();
        await connection.OpenAsync();
        using var query = connection.CreateCommand();
        query.CommandText = "PRAGMA user_version";
        var version = Convert.ToInt64(await query.ExecuteScalarAsync());
        query.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'";
        var count = Convert.ToInt64(await query.ExecuteScalarAsync());
        if (version >= SchemaVersion || count == 0) { await InitializeCoreAsync(db, path); return; }

        var backupPath = path + ".before-v2-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N") + ".bak";
        var workingPath = path + ".upgrade-" + Guid.NewGuid().ToString("N");
        using (var backup = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = backupPath, Pooling = false }.ToString()))
        { backup.Open(); connection.BackupDatabase(backup); }
        try
        {
            using (var working = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = workingPath, Pooling = false }.ToString()))
            { working.Open(); connection.BackupDatabase(working); }
            await using var copy = new AppDbContext(workingPath, pooling: false);
            await InitializeCoreAsync(copy, workingPath);
            // Only the successfully upgraded copy is written back, via SQLite's atomic backup API.
            var upgraded = (SqliteConnection)copy.Database.GetDbConnection();
            upgraded.BackupDatabase(connection);
            await copy.Database.CloseConnectionAsync(); SqliteConnection.ClearPool(upgraded);
            Serilog.Log.Information("数据库已升级到 {Version};原库备份 {Backup}", SchemaVersion, backupPath);
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm" })
                if (File.Exists(workingPath + suffix)) File.Delete(workingPath + suffix);
        }
    }
    private static async Task InitializeCoreAsync(AppDbContext db, string path)
    {
        var connection = (SqliteConnection)db.Database.GetDbConnection();
        await connection.OpenAsync();
        async Task<long> Scalar(string sql)
        {
            using var command = connection.CreateCommand(); command.CommandText = sql;
            return Convert.ToInt64(await command.ExecuteScalarAsync());
        }
        var version = await Scalar("PRAGMA user_version");
        if (version > SchemaVersion) throw new InvalidDataException("数据库来自更新版本，不能降级打开。");
        if (version == SchemaVersion) return;
        var count = await Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'");
        if (count == 0)
        {
            await db.Database.EnsureCreatedAsync();
            await db.Database.ExecuteSqlRawAsync($"PRAGMA user_version = {SchemaVersion}");
            return;
        }
        async Task<HashSet<string>> Columns(string table)
        {
            using var command = connection.CreateCommand(); command.CommandText = $"PRAGMA table_info('{table}')";
            using var reader = await command.ExecuteReaderAsync(); var names = new HashSet<string>();
            while (await reader.ReadAsync()) names.Add(reader.GetString(1)); return names;
        }
        var categoryColumns = await Columns("Categories");
        var fileColumns = await Columns("Files");
        if (!new[] { "Id", "ParentId", "Name", "PhysicalPath", "SortOrder", "CreatedAt", "UpdatedAt" }.All(categoryColumns.Contains)
            || !new[] { "Id", "CategoryId", "FileName", "AbsolutePath", "CreatedAt", "UpdatedAt" }.All(fileColumns.Contains))
            throw new InvalidDataException("无法识别旧数据库结构。原数据库保留，升级已停止。");

        await using var transaction = await db.Database.BeginTransactionAsync();
        foreach (var (table, columns, name, type) in new[]
        {
            ("Categories", categoryColumns, "IsPinned", "INTEGER NOT NULL DEFAULT 0"),
            ("Categories", categoryColumns, "PinnedOrder", "INTEGER NOT NULL DEFAULT 0"),
            ("Categories", categoryColumns, "DirectoryLocationId", "INTEGER NULL"),
            ("Files", fileColumns, "DirectoryLocationId", "INTEGER NULL"),
            ("Files", fileColumns, "RelativePath", "TEXT NULL"),
            ("Files", fileColumns, "ExternalAbsolutePath", "TEXT NULL")
        })
            if (!columns.Contains(name))
            {
                // Identifiers and column types come exclusively from the constant whitelist above.
                var ddl = $"ALTER TABLE {table} ADD COLUMN {name} {type}";
                await db.Database.ExecuteSqlRawAsync(ddl);
            }
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS DirectoryLocations (Id INTEGER PRIMARY KEY AUTOINCREMENT, ParentId INTEGER NULL REFERENCES DirectoryLocations(Id), Segment TEXT NOT NULL, RootPath TEXT NULL)");
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS FileOperations (Id INTEGER PRIMARY KEY AUTOINCREMENT, Kind TEXT NOT NULL, FileId INTEGER NULL, Source TEXT NOT NULL, Target TEXT NOT NULL, Backup TEXT NULL, State TEXT NOT NULL, Error TEXT NULL, Payload TEXT NULL, Digest TEXT NULL, CreatedAt TEXT NOT NULL)");
        await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_Files_PathNoCase ON Files (AbsolutePath COLLATE NOCASE)");
        await LocationService.InitializeLegacyLocationsAsync(db);
        var pinned = await db.Categories.Where(c => c.IsPinned).OrderBy(c => c.SortOrder).ThenBy(c => c.Id).ToListAsync();
        for (var i = 0; i < pinned.Count; i++) pinned[i].PinnedOrder = i + 1;
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync($"PRAGMA user_version = {SchemaVersion}");
        await transaction.CommitAsync();
    }
}
