using Microsoft.Data.Sqlite;

namespace LlmGateway.Desktop.Services;

public sealed class CodexStateService(AppPaths paths)
{
    public async Task SynchronizeModelProviderAsync(string provider, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(paths.CodexStateDatabasePath))
        {
            return;
        }

        await using var connection = new SqliteConnection($"Data Source={paths.CodexStateDatabasePath};Mode=ReadWrite");
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE threads SET model_provider = $provider";
        command.Parameters.AddWithValue("$provider", provider);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
