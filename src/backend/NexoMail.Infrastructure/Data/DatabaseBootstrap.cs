using System.Data;
using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NexoMail.Domain;

namespace NexoMail.Infrastructure.Data;

public static class DatabaseBootstrap
{
    /// <summary>
    /// Keeps existing development SQLite databases usable while NexoMail evolves its
    /// authentication, commercial and operational metadata models.
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

            if (!columns.Contains("PasswordHash")) await AddColumnAsync("ALTER TABLE Users ADD COLUMN PasswordHash TEXT NULL;", connection, cancellationToken);
            if (!columns.Contains("PasswordResetTokenHash")) await AddColumnAsync("ALTER TABLE Users ADD COLUMN PasswordResetTokenHash TEXT NULL;", connection, cancellationToken);
            if (!columns.Contains("PasswordResetTokenExpiresAt")) await AddColumnAsync("ALTER TABLE Users ADD COLUMN PasswordResetTokenExpiresAt TEXT NULL;", connection, cancellationToken);
            if (!columns.Contains("PasswordResetAttempts")) await AddColumnAsync("ALTER TABLE Users ADD COLUMN PasswordResetAttempts INTEGER NOT NULL DEFAULT 0;", connection, cancellationToken);
            if (!columns.Contains("IsEmailVerified")) await AddColumnAsync("ALTER TABLE Users ADD COLUMN IsEmailVerified INTEGER NOT NULL DEFAULT 1;", connection, cancellationToken);
            if (!columns.Contains("EmailVerificationTokenHash")) await AddColumnAsync("ALTER TABLE Users ADD COLUMN EmailVerificationTokenHash TEXT NULL;", connection, cancellationToken);
            if (!columns.Contains("EmailVerificationTokenExpiresAt")) await AddColumnAsync("ALTER TABLE Users ADD COLUMN EmailVerificationTokenExpiresAt TEXT NULL;", connection, cancellationToken);
            if (!columns.Contains("EmailVerificationAttempts")) await AddColumnAsync("ALTER TABLE Users ADD COLUMN EmailVerificationAttempts INTEGER NOT NULL DEFAULT 0;", connection, cancellationToken);
            if (!columns.Contains("AvatarDataUrl")) await AddColumnAsync("ALTER TABLE Users ADD COLUMN AvatarDataUrl TEXT NULL;", connection, cancellationToken);
            // Existing beta users are grandfathered into Premium. New registrations use the Freemium entity default.
            if (!columns.Contains("PlanCode")) await AddColumnAsync("ALTER TABLE Users ADD COLUMN PlanCode TEXT NOT NULL DEFAULT 'premium';", connection, cancellationToken);
            if (!columns.Contains("IsAdministrator")) await AddColumnAsync("ALTER TABLE Users ADD COLUMN IsAdministrator INTEGER NOT NULL DEFAULT 0;", connection, cancellationToken);

            await ExecuteAsync(@"
                CREATE TABLE IF NOT EXISTS UserSessions (
                    Id TEXT NOT NULL CONSTRAINT PK_UserSessions PRIMARY KEY, UserId TEXT NOT NULL, CreatedAt TEXT NOT NULL, LastSeenAt TEXT NOT NULL,
                    ExpiresAt TEXT NOT NULL, RevokedAt TEXT NULL, IpAddress TEXT NULL, UserAgent TEXT NULL, SecurityStamp TEXT NOT NULL,
                    CONSTRAINT FK_UserSessions_Users_UserId FOREIGN KEY (UserId) REFERENCES Users (Id) ON DELETE CASCADE
                );", connection, cancellationToken);
            await ExecuteAsync("CREATE INDEX IF NOT EXISTS IX_UserSessions_UserId_RevokedAt ON UserSessions (UserId, RevokedAt);", connection, cancellationToken);

            await ExecuteAsync(@"
                CREATE TABLE IF NOT EXISTS ControlCenterStates (
                    Id TEXT NOT NULL CONSTRAINT PK_ControlCenterStates PRIMARY KEY, UserId TEXT NOT NULL, AccountId TEXT NOT NULL, ConversationId TEXT NOT NULL,
                    LastMessageId TEXT NOT NULL, Status TEXT NOT NULL, SnoozedUntil TEXT NULL, UpdatedAt TEXT NOT NULL,
                    CONSTRAINT FK_ControlCenterStates_Users_UserId FOREIGN KEY (UserId) REFERENCES Users (Id) ON DELETE CASCADE,
                    CONSTRAINT FK_ControlCenterStates_MailAccounts_AccountId FOREIGN KEY (AccountId) REFERENCES MailAccounts (Id) ON DELETE CASCADE
                );", connection, cancellationToken);
            await ExecuteAsync("CREATE UNIQUE INDEX IF NOT EXISTS IX_ControlCenterStates_UserId_AccountId_ConversationId ON ControlCenterStates (UserId, AccountId, ConversationId);", connection, cancellationToken);

            await ExecuteAsync(@"
                CREATE TABLE IF NOT EXISTS IgnoredSenders (
                    Id TEXT NOT NULL CONSTRAINT PK_IgnoredSenders PRIMARY KEY, UserId TEXT NOT NULL, AccountId TEXT NOT NULL, SenderAddress TEXT NOT NULL, CreatedAt TEXT NOT NULL,
                    CONSTRAINT FK_IgnoredSenders_Users_UserId FOREIGN KEY (UserId) REFERENCES Users (Id) ON DELETE CASCADE,
                    CONSTRAINT FK_IgnoredSenders_MailAccounts_AccountId FOREIGN KEY (AccountId) REFERENCES MailAccounts (Id) ON DELETE CASCADE
                );", connection, cancellationToken);
            await ExecuteAsync("CREATE UNIQUE INDEX IF NOT EXISTS IX_IgnoredSenders_UserId_AccountId_SenderAddress ON IgnoredSenders (UserId, AccountId, SenderAddress);", connection, cancellationToken);

            await ExecuteAsync(@"
                CREATE TABLE IF NOT EXISTS MailMessageIndex (
                    Id TEXT NOT NULL CONSTRAINT PK_MailMessageIndex PRIMARY KEY, UserId TEXT NOT NULL, AccountId TEXT NOT NULL,
                    ProviderMessageId TEXT NOT NULL, ThreadId TEXT NOT NULL, Direction TEXT NOT NULL, FromName TEXT NOT NULL, FromAddress TEXT NOT NULL,
                    ToAddresses TEXT NOT NULL, Subject TEXT NOT NULL, Snippet TEXT NOT NULL, OccurredAt TEXT NOT NULL, HasAttachments INTEGER NOT NULL, IndexedAt TEXT NOT NULL,
                    CONSTRAINT FK_MailMessageIndex_Users_UserId FOREIGN KEY (UserId) REFERENCES Users (Id) ON DELETE CASCADE,
                    CONSTRAINT FK_MailMessageIndex_MailAccounts_AccountId FOREIGN KEY (AccountId) REFERENCES MailAccounts (Id) ON DELETE CASCADE
                );", connection, cancellationToken);
            await ExecuteAsync("CREATE UNIQUE INDEX IF NOT EXISTS IX_MailMessageIndex_UserId_AccountId_ProviderMessageId ON MailMessageIndex (UserId, AccountId, ProviderMessageId);", connection, cancellationToken);
            await ExecuteAsync("CREATE INDEX IF NOT EXISTS IX_MailMessageIndex_UserId_OccurredAt ON MailMessageIndex (UserId, OccurredAt);", connection, cancellationToken);
            await ExecuteAsync("CREATE INDEX IF NOT EXISTS IX_MailMessageIndex_UserId_ThreadId_OccurredAt ON MailMessageIndex (UserId, ThreadId, OccurredAt);", connection, cancellationToken);

            await ExecuteAsync(@"
                CREATE TABLE IF NOT EXISTS MailAttachmentIndex (
                    Id TEXT NOT NULL CONSTRAINT PK_MailAttachmentIndex PRIMARY KEY, UserId TEXT NOT NULL, AccountId TEXT NOT NULL,
                    ProviderMessageId TEXT NOT NULL, AttachmentId TEXT NOT NULL, FileName TEXT NOT NULL, ContentType TEXT NOT NULL, Size INTEGER NOT NULL, IndexedAt TEXT NOT NULL,
                    CONSTRAINT FK_MailAttachmentIndex_Users_UserId FOREIGN KEY (UserId) REFERENCES Users (Id) ON DELETE CASCADE,
                    CONSTRAINT FK_MailAttachmentIndex_MailAccounts_AccountId FOREIGN KEY (AccountId) REFERENCES MailAccounts (Id) ON DELETE CASCADE
                );", connection, cancellationToken);
            await ExecuteAsync("CREATE UNIQUE INDEX IF NOT EXISTS IX_MailAttachmentIndex_UserId_AccountId_ProviderMessageId_AttachmentId ON MailAttachmentIndex (UserId, AccountId, ProviderMessageId, AttachmentId);", connection, cancellationToken);
            await ExecuteAsync("CREATE INDEX IF NOT EXISTS IX_MailAttachmentIndex_UserId_IndexedAt ON MailAttachmentIndex (UserId, IndexedAt);", connection, cancellationToken);

            await ExecuteAsync(@"
                CREATE TABLE IF NOT EXISTS MailIndexStates (
                    AccountId TEXT NOT NULL CONSTRAINT PK_MailIndexStates PRIMARY KEY, UserId TEXT NOT NULL, LastIndexedAt TEXT NOT NULL,
                    WindowDays INTEGER NOT NULL, IndexedMessageCount INTEGER NOT NULL,
                    CONSTRAINT FK_MailIndexStates_Users_UserId FOREIGN KEY (UserId) REFERENCES Users (Id) ON DELETE CASCADE,
                    CONSTRAINT FK_MailIndexStates_MailAccounts_AccountId FOREIGN KEY (AccountId) REFERENCES MailAccounts (Id) ON DELETE CASCADE
                );", connection, cancellationToken);
            await ExecuteAsync("CREATE INDEX IF NOT EXISTS IX_MailIndexStates_UserId_LastIndexedAt ON MailIndexStates (UserId, LastIndexedAt);", connection, cancellationToken);

            await ExecuteAsync(@"
                CREATE TABLE IF NOT EXISTS CommercialPlans (
                    Code TEXT NOT NULL CONSTRAINT PK_CommercialPlans PRIMARY KEY,
                    Name TEXT NOT NULL, Price TEXT NOT NULL, Cadence TEXT NOT NULL, MaxAccounts INTEGER NULL,
                    Description TEXT NOT NULL, FeaturesJson TEXT NOT NULL, IsFeatured INTEGER NOT NULL,
                    IsCorporate INTEGER NOT NULL, IsWhiteLabel INTEGER NOT NULL, IsActive INTEGER NOT NULL,
                    SortOrder INTEGER NOT NULL, UpdatedAt TEXT NOT NULL
                );", connection, cancellationToken);
            await ExecuteAsync("CREATE INDEX IF NOT EXISTS IX_CommercialPlans_IsActive_SortOrder ON CommercialPlans (IsActive, SortOrder);", connection, cancellationToken);

            var sortOrder = 10;
            foreach (var plan in CommercialPlanCatalog.All)
            {
                await SeedCommercialPlanAsync(connection, plan, sortOrder, cancellationToken);
                sortOrder += 10;
            }

            var isDevelopment = string.Equals(
                Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
                "Development",
                StringComparison.OrdinalIgnoreCase);
            if (isDevelopment)
            {
                // Local development only: keep the oldest active local owner as the administrator.
                await ExecuteAsync(@"
                    UPDATE Users
                    SET IsAdministrator = 1
                    WHERE Id = (SELECT Id FROM Users WHERE IsActive = 1 ORDER BY CreatedAt LIMIT 1)
                      AND NOT EXISTS (SELECT 1 FROM Users WHERE IsAdministrator = 1);", connection, cancellationToken);
            }
        }
        finally
        {
            if (shouldClose) await connection.CloseAsync();
        }
    }

    private static Task AddColumnAsync(string sql, DbConnection connection, CancellationToken cancellationToken) => ExecuteAsync(sql, connection, cancellationToken);

    private static async Task SeedCommercialPlanAsync(DbConnection connection, CommercialPlanDefinition plan, int sortOrder, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT OR IGNORE INTO CommercialPlans
            (Code, Name, Price, Cadence, MaxAccounts, Description, FeaturesJson, IsFeatured, IsCorporate, IsWhiteLabel, IsActive, SortOrder, UpdatedAt)
            VALUES
            ($code, $name, $price, $cadence, $maxAccounts, $description, $featuresJson, $isFeatured, $isCorporate, $isWhiteLabel, 1, $sortOrder, $updatedAt);";
        AddParameter(command, "$code", plan.Code);
        AddParameter(command, "$name", plan.Name);
        AddParameter(command, "$price", plan.Price);
        AddParameter(command, "$cadence", plan.Cadence);
        AddParameter(command, "$maxAccounts", plan.MaxAccounts);
        AddParameter(command, "$description", plan.Description);
        AddParameter(command, "$featuresJson", JsonSerializer.Serialize(plan.Features));
        AddParameter(command, "$isFeatured", plan.IsFeatured ? 1 : 0);
        AddParameter(command, "$isCorporate", plan.IsCorporate ? 1 : 0);
        AddParameter(command, "$isWhiteLabel", plan.IsWhiteLabel ? 1 : 0);
        AddParameter(command, "$sortOrder", sortOrder);
        AddParameter(command, "$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static async Task ExecuteAsync(string sql, DbConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
