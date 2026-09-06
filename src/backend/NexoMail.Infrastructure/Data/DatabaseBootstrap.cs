using System.Data;
using Microsoft.EntityFrameworkCore;

namespace NexoMail.Infrastructure.Data;

public static class DatabaseBootstrap
{
    /// <summary>
    /// Keeps existing development SQLite databases usable while NexoMail evolves its
    /// authentication model. Fresh databases are created with the complete model by EnsureCreated.
    /// </summary>
    public static async Task EnsureAuthenticationSchemaAsync(NexoMailDbContext database, CancellationToken cancellationToken = default)
    {
        var connection = database.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose) await connection.OpenAsync(cancellationToken);
        try
        {
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var inspect = connection.CreateCommand())
            {
                inspect.CommandText = "PRAGMA table_info('Users');";
                await using var reader = await inspect.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                    if (reader["name"]?.ToString() is { Length: > 0 } name) columns.Add(name);
            }

            if (!columns.Contains("PasswordHash"))
                await AddColumnAsync("ALTER TABLE Users ADD COLUMN PasswordHash TEXT NULL;", connection, cancellationToken);
            if (!columns.Contains("PasswordResetTokenHash"))
                await AddColumnAsync("ALTER TABLE Users ADD COLUMN PasswordResetTokenHash TEXT NULL;", connection, cancellationToken);
            if (!columns.Contains("PasswordResetTokenExpiresAt"))
                await AddColumnAsync("ALTER TABLE Users ADD COLUMN PasswordResetTokenExpiresAt TEXT NULL;", connection, cancellationToken);
            if (!columns.Contains("PasswordResetAttempts"))
                await AddColumnAsync("ALTER TABLE Users ADD COLUMN PasswordResetAttempts INTEGER NOT NULL DEFAULT 0;", connection, cancellationToken);
            if (!columns.Contains("IsEmailVerified"))
                await AddColumnAsync("ALTER TABLE Users ADD COLUMN IsEmailVerified INTEGER NOT NULL DEFAULT 1;", connection, cancellationToken);
            if (!columns.Contains("EmailVerificationTokenHash"))
                await AddColumnAsync("ALTER TABLE Users ADD COLUMN EmailVerificationTokenHash TEXT NULL;", connection, cancellationToken);
            if (!columns.Contains("EmailVerificationTokenExpiresAt"))
                await AddColumnAsync("ALTER TABLE Users ADD COLUMN EmailVerificationTokenExpiresAt TEXT NULL;", connection, cancellationToken);
            if (!columns.Contains("EmailVerificationAttempts"))
                await AddColumnAsync("ALTER TABLE Users ADD COLUMN EmailVerificationAttempts INTEGER NOT NULL DEFAULT 0;", connection, cancellationToken);
            if (!columns.Contains("AvatarDataUrl"))
                await AddColumnAsync("ALTER TABLE Users ADD COLUMN AvatarDataUrl TEXT NULL;", connection, cancellationToken);
        }
        finally
        {
            if (shouldClose) await connection.CloseAsync();
        }
    }

    private static async Task AddColumnAsync(string sql, System.Data.Common.DbConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
