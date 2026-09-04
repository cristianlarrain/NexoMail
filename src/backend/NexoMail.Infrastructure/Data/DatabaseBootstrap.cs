using System.Data;
using Microsoft.EntityFrameworkCore;

namespace NexoMail.Infrastructure.Data;

public static class DatabaseBootstrap
{
    /// <summary>
    /// Keeps the existing development SQLite database usable while NexoMail moves from the
    /// original local-user prototype to authenticated users. Fresh databases are created with
    /// the complete model by EnsureCreated; old databases only need the PasswordHash column.
    /// </summary>
    public static async Task EnsureAuthenticationSchemaAsync(NexoMailDbContext database, CancellationToken cancellationToken = default)
    {
        var connection = database.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose) await connection.OpenAsync(cancellationToken);
        try
        {
            await using var inspect = connection.CreateCommand();
            inspect.CommandText = "PRAGMA table_info('Users');";
            var hasPasswordHash = false;
            await using (var reader = await inspect.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    if (string.Equals(reader["name"]?.ToString(), "PasswordHash", StringComparison.OrdinalIgnoreCase))
                    {
                        hasPasswordHash = true;
                        break;
                    }
                }
            }

            if (!hasPasswordHash)
            {
                await using var alter = connection.CreateCommand();
                alter.CommandText = "ALTER TABLE Users ADD COLUMN PasswordHash TEXT NULL;";
                await alter.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        finally
        {
            if (shouldClose) await connection.CloseAsync();
        }
    }
}
