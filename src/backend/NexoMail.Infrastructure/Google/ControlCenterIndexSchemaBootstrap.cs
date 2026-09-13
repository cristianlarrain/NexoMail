using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using NexoMail.Infrastructure.Data;

namespace NexoMail.Infrastructure.Google;

public static class ControlCenterIndexSchemaBootstrap
{
    public static async Task EnsureAsync(NexoMailDbContext database, CancellationToken ct = default)
    {
        var provider = database.Database.ProviderName ?? string.Empty;
        var connection = database.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose) await connection.OpenAsync(ct);
        try
        {
            if (provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                await EnsureSqliteColumnAsync(connection, "MailMessageIndex", "GmailLabels", "TEXT NOT NULL DEFAULT ''", ct);
                await EnsureSqliteColumnAsync(connection, "MailMessageIndex", "IsInbox", "INTEGER NOT NULL DEFAULT 0", ct);
                await EnsureSqliteColumnAsync(connection, "MailMessageIndex", "IsUnread", "INTEGER NOT NULL DEFAULT 0", ct);
                await EnsureSqliteColumnAsync(connection, "MailMessageIndex", "AutoSubmitted", "TEXT NOT NULL DEFAULT ''", ct);
                await EnsureSqliteColumnAsync(connection, "MailMessageIndex", "Precedence", "TEXT NOT NULL DEFAULT ''", ct);
                await EnsureSqliteColumnAsync(connection, "MailMessageIndex", "HasListUnsubscribe", "INTEGER NOT NULL DEFAULT 0", ct);
                await EnsureSqliteColumnAsync(connection, "MailIndexStates", "SyncLeaseOwner", "TEXT NULL", ct);
                await EnsureSqliteColumnAsync(connection, "MailIndexStates", "SyncLeaseUntil", "TEXT NULL", ct);
                return;
            }

            if (provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                await ExecuteAsync(connection, "IF COL_LENGTH('MailMessageIndex','GmailLabels') IS NULL ALTER TABLE MailMessageIndex ADD GmailLabels nvarchar(2000) NOT NULL CONSTRAINT DF_MailMessageIndex_GmailLabels DEFAULT '';", ct);
                await ExecuteAsync(connection, "IF COL_LENGTH('MailMessageIndex','IsInbox') IS NULL ALTER TABLE MailMessageIndex ADD IsInbox bit NOT NULL CONSTRAINT DF_MailMessageIndex_IsInbox DEFAULT 0;", ct);
                await ExecuteAsync(connection, "IF COL_LENGTH('MailMessageIndex','IsUnread') IS NULL ALTER TABLE MailMessageIndex ADD IsUnread bit NOT NULL CONSTRAINT DF_MailMessageIndex_IsUnread DEFAULT 0;", ct);
                await ExecuteAsync(connection, "IF COL_LENGTH('MailMessageIndex','AutoSubmitted') IS NULL ALTER TABLE MailMessageIndex ADD AutoSubmitted nvarchar(256) NOT NULL CONSTRAINT DF_MailMessageIndex_AutoSubmitted DEFAULT '';", ct);
                await ExecuteAsync(connection, "IF COL_LENGTH('MailMessageIndex','Precedence') IS NULL ALTER TABLE MailMessageIndex ADD Precedence nvarchar(64) NOT NULL CONSTRAINT DF_MailMessageIndex_Precedence DEFAULT '';", ct);
                await ExecuteAsync(connection, "IF COL_LENGTH('MailMessageIndex','HasListUnsubscribe') IS NULL ALTER TABLE MailMessageIndex ADD HasListUnsubscribe bit NOT NULL CONSTRAINT DF_MailMessageIndex_HasListUnsubscribe DEFAULT 0;", ct);
                await ExecuteAsync(connection, "IF COL_LENGTH('MailIndexStates','SyncLeaseOwner') IS NULL ALTER TABLE MailIndexStates ADD SyncLeaseOwner nvarchar(128) NULL;", ct);
                await ExecuteAsync(connection, "IF COL_LENGTH('MailIndexStates','SyncLeaseUntil') IS NULL ALTER TABLE MailIndexStates ADD SyncLeaseUntil datetimeoffset NULL;", ct);
            }
        }
        finally
        {
            if (shouldClose) await connection.CloseAsync();
        }
    }

    private static async Task EnsureSqliteColumnAsync(DbConnection connection, string table, string column, string definition, CancellationToken ct)
    {
        await using var check = connection.CreateCommand();
        check.CommandText = $"PRAGMA table_info({table});";
        await using var reader = await check.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase)) return;
        }
        await reader.DisposeAsync();
        await ExecuteAsync(connection, $"ALTER TABLE {table} ADD COLUMN {column} {definition};", ct);
    }

    private static async Task ExecuteAsync(DbConnection connection, string sql, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(ct);
    }
}
