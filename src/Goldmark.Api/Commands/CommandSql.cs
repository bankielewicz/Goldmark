using Microsoft.Data.SqlClient;

namespace Goldmark.Api.Commands;

/// <summary>SQL plumbing shared by the one-shot database commands.</summary>
internal static class CommandSql
{
    /// <summary>
    /// Retries until SQL Server accepts the connection. The whole wait, including each
    /// connection attempt, is bounded by <paramref name="wait"/> plus one connect timeout.
    /// </summary>
    public static async Task<SqlConnection> OpenWithRetryAsync(string connectionString, TimeSpan wait, TextWriter output, string command)
    {
        var connectTimeout = TimeSpan.FromSeconds(new SqlConnectionStringBuilder(connectionString).ConnectTimeout);
        using var overall = new CancellationTokenSource(wait + connectTimeout);
        var deadline = DateTime.UtcNow + wait;
        while (true)
        {
            var connection = new SqlConnection(connectionString);
            try
            {
                await connection.OpenAsync(overall.Token);
                return connection;
            }
            catch (SqlException ex) when (DateTime.UtcNow < deadline)
            {
                await connection.DisposeAsync();
                await output.WriteLineAsync($"{command}: SQL Server not ready yet (error {ex.Number}); retrying");
                await Task.Delay(TimeSpan.FromSeconds(3), overall.Token);
            }
            catch
            {
                await connection.DisposeAsync();
                throw;
            }
        }
    }

    /// <summary>Runs a query whose rows are (kind, name) findings and returns them as "kind name".</summary>
    public static async Task<List<string>> FindingsAsync(SqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 60;
        await using var reader = await command.ExecuteReaderAsync();
        var findings = new List<string>();
        while (await reader.ReadAsync())
        {
            findings.Add($"{reader.GetString(0)} {reader.GetString(1)}");
        }

        return findings;
    }
}
