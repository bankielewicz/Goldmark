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
/// <para>
/// The connection also carries who is acting (FR-CM-5, DEC-047): before a statement runs for a signed-in
/// person, their account ID goes into the connection's <c>SESSION_CONTEXT</c> under
/// <see cref="ChangedByKey"/>, read-only until the connection is reset. <c>cmdb.Ci</c> and
/// <c>cmdb.Relationship</c> take <c>ChangedBy</c> from it by default (migration 0009), and statements on
/// the definition tables read it with <c>SESSION_CONTEXT</c> themselves. A session with no principal sets nothing, and a
/// pooled connection starts empty (SqlClient resets it before reuse), so a write that needs a
/// <c>ChangedBy</c> fails there instead of recording nobody, or the last person.
/// </para>
/// </summary>
internal sealed class DbSession(SqlConnectionFactory factory, ActingPrincipal? actor = null) : IAsyncDisposable
{
    /// <summary>The <c>SESSION_CONTEXT</c> key that holds the acting principal (the same text as migration 0009's defaults).</summary>
    public const string ChangedByKey = "Goldmark.ChangedBy";

    private SqlConnection? _connection;
    private SqlTransaction? _transaction;
    private bool _actorSet;

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

    /// <summary>
    /// Refuses to go on when nobody is acting: call before a statement that records <c>ChangedBy</c> in a
    /// column that allows null (the definition tables), where the database itself wouldn't refuse.
    /// </summary>
    public void RequireActor()
    {
        if (actor?.ChangedBy is null)
        {
            throw new InvalidOperationException("This change needs a signed-in person: nobody is acting on this connection.");
        }
    }

    private async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        if (_connection is null)
        {
            _connection = new SqlConnection(factory.CreateBuilder().ConnectionString);
            await _connection.OpenAsync(cancellationToken);
        }

        // Once per connection, as soon as someone is acting (a sign-in request opens it before anyone is).
        // Read-only: nothing later on this connection can name someone else.
        if (!_actorSet && actor?.ChangedBy is { } changedBy)
        {
            await _connection.ExecuteAsync(Command(
                "EXEC sys.sp_set_session_context @key = N'" + ChangedByKey + "', @value = @changedBy, @read_only = 1;",
                new { changedBy }, cancellationToken));
            _actorSet = true; // only once it is set: a failure here fails the statement that was about to run
        }

        return _connection;
    }
}
