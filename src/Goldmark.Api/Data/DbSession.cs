using System.Data;
using System.Diagnostics.CodeAnalysis;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Goldmark.Api.Data;

/// <summary>
/// The application's only route to Dapper (NFR-SEC-3, DEC-040): one connection per request scope,
/// opened on first use, with at most one transaction at a time. Every SQL argument is
/// <see cref="ConstantExpectedAttribute"/>, so the build refuses SQL built from runtime values
/// (CA1857 is an error under TreatWarningsAsErrors): interpolation, concatenation and variables fail
/// to compile, and values travel as Dapper parameters. <c>SqlGateTests</c> check that no other file
/// uses Dapper and that CA1857 is never suppressed; <c>tests/sql-gate/</c> proves a violating call
/// fails the build.
/// </summary>
internal sealed class DbSession(SqlConnectionFactory factory) : IAsyncDisposable
{
    private SqlConnection? _connection;
    private SqlTransaction? _transaction;

    public bool InTransaction => _transaction is not null;

    public async Task<IReadOnlyList<T>> QueryAsync<T>([ConstantExpected] string sql, object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var connection = await OpenAsync(cancellationToken);
        return [.. await connection.QueryAsync<T>(Command(sql, parameters, cancellationToken))];
    }

    public async Task<T?> QuerySingleOrDefaultAsync<T>([ConstantExpected] string sql, object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var connection = await OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<T>(Command(sql, parameters, cancellationToken));
    }

    /// <summary>The single-row query with a longer timeout than the 30-second default, for maintenance such as the audit purge.</summary>
    public async Task<T?> QuerySingleOrDefaultAsync<T>([ConstantExpected] string sql, int commandTimeoutSeconds, CancellationToken cancellationToken)
    {
        var connection = await OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<T>(Command(sql, null, cancellationToken, commandTimeoutSeconds));
    }

    public async Task<T?> ExecuteScalarAsync<T>([ConstantExpected] string sql, object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var connection = await OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<T>(Command(sql, parameters, cancellationToken));
    }

    /// <summary>Runs a statement and returns the number of rows it affected.</summary>
    public async Task<int> ExecuteAsync([ConstantExpected] string sql, object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var connection = await OpenAsync(cancellationToken);
        return await connection.ExecuteAsync(Command(sql, parameters, cancellationToken));
    }

    /// <summary>
    /// Runs <paramref name="work"/> in a transaction: committed when it returns, rolled back when it
    /// throws. Inside an open transaction it simply joins it, so a store call and its audit row
    /// commit together.
    /// </summary>
    public async Task<T> InTransactionAsync<T>(Func<Task<T>> work, IsolationLevel isolation = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default)
    {
        if (_transaction is not null)
        {
            return await work();
        }

        var connection = await OpenAsync(cancellationToken);
        _transaction = (SqlTransaction)await connection.BeginTransactionAsync(isolation, cancellationToken);
        try
        {
            var result = await work();
            await _transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await _transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_transaction is not null)
        {
            await _transaction.DisposeAsync();
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }
    }

    private CommandDefinition Command(string sql, object? parameters, CancellationToken cancellationToken, int? commandTimeoutSeconds = null) =>
        new(sql, parameters, _transaction, commandTimeoutSeconds, cancellationToken: cancellationToken);

    private async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        if (_connection is null)
        {
            _connection = new SqlConnection(factory.CreateBuilder().ConnectionString);
            await _connection.OpenAsync(cancellationToken);
        }

        return _connection;
    }
}
