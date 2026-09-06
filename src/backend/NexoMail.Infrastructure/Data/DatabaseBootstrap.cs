using System.Data;
using Microsoft.EntityFrameworkCore;

namespace NexoMail.Infrastructure.Data;

public static class DatabaseBootstrap
{
    /// <summary>
    /// Keeps existing development SQLite databases usable while NexoMail evolves its
    /// authentication and operational metadata models. Fresh databases are created with the complete model by EnsureCreated.
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

            await ExecuteAsync(@"
                CREATE TABLE IF NOT EXISTS UserSessions (
                    Id TEXT NOT NULL CONSTRAINT PK_UserSessions PRIMARY KEY,
                    UserId TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    LastSeenAt TEXT NOT NULL,
                    ExpiresAt TEXT NOT NULL,
                    RevokedAt TEXT NULL,
                    IpAddress TEXT NULL,
                    UserAgent TEXT NULL,
                    SecurityStamp TEXT NOT NULL,
                    CONSTRAINT FK_UserSessions_Users_UserId FOREIGN KEY (UserId) REFERENCES Users (Id) ON DELETE CASCADE
                );", connection, cancellationToken);
            await ExecuteAsync("CREATE INDEX IF NOT EXISTS IX_UserSessions_UserId_RevokedAt ON UserSessions (UserId, RevokedAt);", connection, cancellationToken);

            await ExecuteAsync(@"
                CREATE TABLE IF NOT EXISTS ControlCenterStates (
                    Id TEXT NOT NULL CONSTRAINT PK_ControlCenterStates PRIMARY KEY,
                    UserId TEXT NOT NULL,
                    AccountId TEXT NOT NULL,
                    ConversationId TEXT NOT NULL,
                    LastMessageId TEXT NOT NULL,
                    Status TEXT NOT NULL,
                    SnoozedUntil TEXT NULL,
                    UpdatedAt TEXT NOT NULL,
                    CONSTRAINT FK_ControlCenterStates_Users_UserId FOREIGN KEY (UserId) REFERENCES Users (Id) ON DELETE CASCADE,
                    CONSTRAINT FK_ControlCenterStates_MailAccounts_AccountId FOREIGN KEY (AccountId) REFERENCES MailAccounts (Id) ON DELETE CASCADE
                );", connection, cancellationToken);
            await ExecuteAsync("CREATE UNIQUE INDEX IF NOT EXISTS IX_ControlCenterStates_UserId_AccountId_ConversationId ON ControlCenterStates (UserId, AccountId, ConversationId);", connection, cancellationToken);

            await ExecuteAsync(@"
                CREATE TABLE IF NOT EXISTS IgnoredSenders (
                    Id TEXT NOT NULL CONSTRAINT PK_IgnoredSenders PRIMARY KEY,
                    UserId TEXT NOT NULL,
                    AccountId TEXT NOT NULL,
                    SenderAddress TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    CONSTRAINT FK_IgnoredSenders_Users_UserId FOREIGN KEY (UserId) REFERENCES Users (Id) ON DELETE CASCADE,
                    CONSTRAINT FK_IgnoredSenders_MailAccounts_AccountId FOREIGN KEY (AccountId) REFERENCES MailAccounts (Id) ON DELETE CASCADE
                );", connection, cancellationToken);
            await ExecuteAsync("CREATE UNIQUE INDEX IF NOT EXISTS IX_IgnoredSenders_UserId_AccountId_SenderAddress ON IgnoredSenders (UserId, AccountId, SenderAddress);", connection, cancellationToken);
        }
        finally
        {
            if (shouldClose) await connection.CloseAsync();
        }
    }

    private static Task AddColumnAsync(string sql, System.Data.Common.DbConnection connection, CancellationToken cancellationToken) =>
        ExecuteAsync(sql, connection, cancellationToken);

    private static async Task ExecuteAsync(string sql, System.Data.Common.DbConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
