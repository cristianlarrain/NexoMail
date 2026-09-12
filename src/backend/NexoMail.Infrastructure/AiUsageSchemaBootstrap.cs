using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using NexoMail.Infrastructure.Data;

namespace NexoMail.Infrastructure;

public static class AiUsageSchemaBootstrap
{
    public static async Task EnsureAsync(NexoMailDbContext database, CancellationToken ct = default)
    {
        if (!database.Database.IsSqlite()) return;

        var connection = database.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose) await connection.OpenAsync(ct);
        try
        {
            await ExecuteAsync(@"
                CREATE TABLE IF NOT EXISTS AiUsageEvents (
                    Id TEXT NOT NULL CONSTRAINT PK_AiUsageEvents PRIMARY KEY,
                    UserId TEXT NOT NULL,
                    OccurredAt TEXT NOT NULL,
                    OperationType TEXT NOT NULL,
                    Model TEXT NOT NULL,
                    InputTokens INTEGER NOT NULL,
                    OutputTokens INTEGER NOT NULL,
                    ReasoningTokens INTEGER NULL,
                    DurationMs INTEGER NOT NULL,
                    Succeeded INTEGER NOT NULL,
                    ErrorCategory TEXT NULL,
                    InputUsdPerMillion TEXT NULL,
                    OutputUsdPerMillion TEXT NULL,
                    EstimatedCostUsd TEXT NULL,
                    ClpPerUsd TEXT NULL,
                    EstimatedCostClp TEXT NULL,
                    CONSTRAINT FK_AiUsageEvents_Users_UserId FOREIGN KEY (UserId) REFERENCES Users (Id) ON DELETE CASCADE
                );
                CREATE INDEX IF NOT EXISTS IX_AiUsageEvents_UserId_OccurredAt ON AiUsageEvents (UserId, OccurredAt);
                CREATE INDEX IF NOT EXISTS IX_AiUsageEvents_OccurredAt ON AiUsageEvents (OccurredAt);
                CREATE INDEX IF NOT EXISTS IX_AiUsageEvents_OperationType_OccurredAt ON AiUsageEvents (OperationType, OccurredAt);

                CREATE TABLE IF NOT EXISTS AiUsageMonthlySummaries (
                    UserId TEXT NOT NULL,
                    Year INTEGER NOT NULL,
                    Month INTEGER NOT NULL,
                    OperationCount INTEGER NOT NULL,
                    SuccessfulOperations INTEGER NOT NULL,
                    FailedOperations INTEGER NOT NULL,
                    InputTokens INTEGER NOT NULL,
                    OutputTokens INTEGER NOT NULL,
                    EstimatedCostUsd TEXT NOT NULL,
                    EstimatedCostClp TEXT NOT NULL,
                    ActiveDays INTEGER NOT NULL,
                    UpdatedAt TEXT NOT NULL,
                    CONSTRAINT PK_AiUsageMonthlySummaries PRIMARY KEY (UserId, Year, Month),
                    CONSTRAINT FK_AiUsageMonthlySummaries_Users_UserId FOREIGN KEY (UserId) REFERENCES Users (Id) ON DELETE CASCADE
                );
                CREATE INDEX IF NOT EXISTS IX_AiUsageMonthlySummaries_Year_Month ON AiUsageMonthlySummaries (Year, Month);

                CREATE TABLE IF NOT EXISTS AiUsageSettings (
                    Id INTEGER NOT NULL CONSTRAINT PK_AiUsageSettings PRIMARY KEY,
                    GreenMaxClp TEXT NOT NULL,
                    YellowMaxClp TEXT NOT NULL,
                    ReferenceClpPerUsd TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL
                );", connection, ct);

            await using var settings = connection.CreateCommand();
            settings.CommandText = @"
                INSERT OR IGNORE INTO AiUsageSettings
                (Id, GreenMaxClp, YellowMaxClp, ReferenceClpPerUsd, UpdatedAt)
                VALUES (1, $green, $yellow, $rate, $updatedAt);";
            Add(settings, "$green", 1500m);
            Add(settings, "$yellow", 3000m);
            Add(settings, "$rate", 941.1m);
            Add(settings, "$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
            await settings.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            if (shouldClose) await connection.CloseAsync();
        }
    }

    private static async Task ExecuteAsync(string sql, DbConnection connection, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(ct);
    }

    private static void Add(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
