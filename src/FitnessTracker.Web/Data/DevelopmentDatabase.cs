using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FitnessTracker.Web.Data;

/// <summary>Development only: keeps the local database, which holds the owner's real data, up to date on start.</summary>
public static class DevelopmentDatabase
{
    /// <summary>
    /// Applies any pending migrations. When the database already holds data from an earlier migration, a copy is
    /// saved next to it first (<c>fitness-dev.before-&lt;migration&gt;.db</c>) and its path returned; otherwise null.
    /// </summary>
    public static string? MigrateWithBackup(AppDbContext db)
    {
        var pending = db.Database.GetPendingMigrations().ToList();
        string? backupPath = null;

        if (pending.Count > 0 && db.Database.GetAppliedMigrations().Any())
        {
            var connection = (SqliteConnection)db.Database.GetDbConnection();
            var databasePath = new SqliteConnectionStringBuilder(connection.ConnectionString).DataSource;
            backupPath = Path.Combine(
                Path.GetDirectoryName(databasePath) ?? "",
                $"{Path.GetFileNameWithoutExtension(databasePath)}.before-{pending[0]}.db");

            // SQLite's backup API rather than a file copy: recent changes may still be in the -wal file.
            using var destination = new SqliteConnection($"Data Source={backupPath}");
            db.Database.OpenConnection();
            try
            {
                connection.BackupDatabase(destination);
            }
            finally
            {
                db.Database.CloseConnection();
            }
        }

        db.Database.Migrate();
        return backupPath;
    }
}
