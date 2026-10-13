using FitnessTracker.Web.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FitnessTracker.Web.Tests.Data;

// The development database holds the owner's real week, and is migrated automatically on start.
// These tests use real database files (in a temporary folder) because the behaviour is about files.
public sealed class DevelopmentDatabaseTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"ft-devdb-{Guid.NewGuid():N}");
    private readonly string _databasePath;

    public DevelopmentDatabaseTests()
    {
        Directory.CreateDirectory(_folder);
        _databasePath = Path.Combine(_folder, "fitness-dev.db");
    }

    [Fact]
    public void MigrateWithBackup_SavesACopyBeforeMigrating_WhenExistingDataHasMigrationsToApply()
    {
        // A database from an earlier version of the app: one older migration applied, and some data.
        Execute("""
            CREATE TABLE "__EFMigrationsHistory" ("MigrationId" TEXT NOT NULL PRIMARY KEY, "ProductVersion" TEXT NOT NULL);
            INSERT INTO "__EFMigrationsHistory" VALUES ('00000000000000_Earlier', '10.0.12');
            CREATE TABLE "Marker" ("Value" TEXT);
            INSERT INTO "Marker" VALUES ('owner data');
            """);

        using (var db = CreateContext())
        {
            var backupPath = DevelopmentDatabase.MigrateWithBackup(db);

            Assert.NotNull(backupPath);
            Assert.Equal(_folder, Path.GetDirectoryName(backupPath));
            Assert.Equal("owner data", QueryScalar(backupPath, """SELECT "Value" FROM "Marker" """));
            Assert.Equal(0L, QueryScalar(backupPath, "SELECT COUNT(*) FROM sqlite_master WHERE name = 'AspNetUsers'"));
        }

        Assert.Equal(1L, QueryScalar(_databasePath, "SELECT COUNT(*) FROM sqlite_master WHERE name = 'AspNetUsers'"));
    }

    [Fact]
    public void MigrateWithBackup_MakesNoCopy_ForABrandNewDatabase()
    {
        using var db = CreateContext();

        var backupPath = DevelopmentDatabase.MigrateWithBackup(db);

        Assert.Null(backupPath);
        Assert.Equal(1L, QueryScalar(_databasePath, "SELECT COUNT(*) FROM sqlite_master WHERE name = 'AspNetUsers'"));
    }

    [Fact]
    public void MigrateWithBackup_MakesNoCopy_WhenTheDatabaseIsUpToDate()
    {
        using (var first = CreateContext())
        {
            DevelopmentDatabase.MigrateWithBackup(first);
        }

        using var db = CreateContext();
        var backupPath = DevelopmentDatabase.MigrateWithBackup(db);

        Assert.Null(backupPath);
        Assert.DoesNotContain(Directory.GetFiles(_folder), file => file.Contains(".before-", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        // Pooled connections keep file handles open; release them so the folder can be deleted.
        SqliteConnection.ClearAllPools();
        Directory.Delete(_folder, recursive: true);
    }

    private AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={_databasePath}").Options);

    private void Execute(string sql)
    {
        using var connection = new SqliteConnection($"Data Source={_databasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static object? QueryScalar(string databasePath, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }
}
