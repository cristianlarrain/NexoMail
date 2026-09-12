using System.Data;
using Microsoft.EntityFrameworkCore;
using NexoMail.Infrastructure.Data;

namespace NexoMail.Infrastructure.Imap;

public static class ImapSchemaBootstrap
{
    public static async Task EnsureAsync(NexoMailDbContext database, CancellationToken ct = default)
    {
        var connection = database.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose) await connection.OpenAsync(ct);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = @"
                CREATE TABLE IF NOT EXISTS ImapCredentials (
                    Id TEXT NOT NULL CONSTRAINT PK_ImapCredentials PRIMARY KEY,
                    MailAccountId TEXT NOT NULL,
                    Username TEXT NOT NULL,
                    EncryptedPassword TEXT NOT NULL,
                    ImapHost TEXT NOT NULL,
                    ImapPort INTEGER NOT NULL,
                    ImapSecurity TEXT NOT NULL,
                    SmtpHost TEXT NOT NULL,
                    SmtpPort INTEGER NOT NULL,
                    SmtpSecurity TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL,
                    CONSTRAINT FK_ImapCredentials_MailAccounts_MailAccountId FOREIGN KEY (MailAccountId) REFERENCES MailAccounts (Id) ON DELETE CASCADE
                );
                CREATE UNIQUE INDEX IF NOT EXISTS IX_ImapCredentials_MailAccountId ON ImapCredentials (MailAccountId);";
            await command.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            if (shouldClose) await connection.CloseAsync();
        }
    }
}
