using Goldmark.Api.Data;
using Microsoft.Data.SqlClient;

namespace Goldmark.Api.Auth;

/// <summary>
/// First-run setup's state in this process (US-02; DEC-038 item 7). While no administrator exists,
/// each start generates a new random setup token, logs it once and keeps only its hash, so the token
/// works only in this process and only until setup completes; completing setup also writes
/// <c>cmdb.Installation</c>, which disables setup permanently.
/// </summary>
internal sealed class SetupState
{
    private readonly Lock _gate = new();
    private byte[]? _tokenHash;

    public SetupStatus Status { get; private set; } = SetupStatus.Unknown;

    public bool Verify(string? token)
    {
        lock (_gate)
        {
            return Status == SetupStatus.Required && _tokenHash is not null && token is { Length: > 0 and <= 128 }
                && LinkSecrets.Matches(_tokenHash, LinkSecrets.HashSetupToken(token));
        }
    }

    /// <summary>Whether <paramref name="tokenHash"/> is still this process's token (an enrolment carries it).</summary>
    public bool IsCurrent(byte[] tokenHash)
    {
        lock (_gate)
        {
            return Status == SetupStatus.Required && _tokenHash is not null && LinkSecrets.Matches(_tokenHash, tokenHash);
        }
    }

    public byte[]? CurrentTokenHash
    {
        get
        {
            lock (_gate)
            {
                return _tokenHash;
            }
        }
    }

    public void Require(byte[] tokenHash)
    {
        lock (_gate)
        {
            _tokenHash = tokenHash;
            Status = SetupStatus.Required;
        }
    }

    public void Complete()
    {
        lock (_gate)
        {
            _tokenHash = null;
            Status = SetupStatus.Complete;
        }
    }
}

internal enum SetupStatus
{
    /// <summary>Not known yet: the database has not answered since start.</summary>
    Unknown,
    Required,
    Complete,
}

/// <summary>
/// At start, asks the database whether setup is done, retrying quietly until it answers (the app
/// starts before SQL Server is ready, and readiness reports the wait); then, only if setup is not
/// done, generates the setup token and logs it once at Warning under its own event ID. It is the one
/// secret Goldmark ever logs.
/// </summary>
internal sealed partial class SetupTokenService(IServiceScopeFactory scopes, SetupState state, ILogger<SetupTokenService> logger)
    : BackgroundService
{
    public static readonly EventId SetupTokenEvent = new(3001, "SetupToken");

    private static readonly TimeSpan MaximumDelay = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = TimeSpan.FromSeconds(1);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<DbSession>();
                if (await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM cmdb.Installation;", cancellationToken: stoppingToken) > 0)
                {
                    state.Complete();
                }
                else
                {
                    var token = LinkSecrets.NewSetupToken();
                    state.Require(LinkSecrets.HashSetupToken(token));
                    LogSetupToken(logger, token);
                }

                return;
            }
            catch (SqlException ex)
            {
                // Not reachable yet, or not migrated yet (no cmdb.Installation): try again later.
                LogWaiting(logger, ex.Number);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Anything else (a pool timeout, a socket error) must not stop the host either.
                LogWaiting(logger, ex.HResult);
            }

            await Task.Delay(delay, stoppingToken);
            delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaximumDelay.Ticks));
        }
    }

    [LoggerMessage(EventId = 3001, EventName = "SetupToken", Level = LogLevel.Warning,
        Message = "Setup token: {SetupToken}. Goldmark is not set up yet: open Goldmark in a browser and enter this token to " +
            "create the first administrator. A new token is printed each time Goldmark starts, until setup is complete.")]
    private static partial void LogSetupToken(ILogger logger, string setupToken);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Debug, Message = "Setup status: database not available yet (error {Error}); retrying.")]
    private static partial void LogWaiting(ILogger logger, int error);
}
