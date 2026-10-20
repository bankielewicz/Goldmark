using System.Buffers.Text;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Goldmark.Api.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Goldmark.Api.Auth;

/// <summary>
/// Server-side sessions behind the sign-in cookie, through the cookie handler's supported session
/// store (owner, DEC-040): the cookie carries only a random 256-bit key and the ticket lives in
/// <c>cmdb.UserSession</c>, keyed by the key's SHA-256 hash and encrypted with Data Protection. So
/// signing out deletes the session on the server (a copied cookie stops working), and an
/// administrator's lock, disable or two-factor reset ends every session of that person at once.
/// A session ends after <see cref="AuthOptions.SessionIdle"/> without a request, or
/// <see cref="AuthOptions.SessionLifetime"/> after sign-in (DEC-038 item 6).
/// The cookie handler always calls the overloads that pass the request; they use the request's
/// <see cref="DbSession"/>, so a session row is written or deleted in the endpoint's transaction,
/// together with its audit row and whatever else the sign-in or sign-out changes (Codex, PR #24).
/// The overloads without a request, which the interface requires, use a connection of their own.
/// </summary>
internal sealed class SessionTicketStore(SqlConnectionFactory connections, IDataProtectionProvider protection,
    IOptions<AuthOptions> options, TimeProvider time) : ITicketStore
{
    public const string TicketPurpose = "Goldmark.SessionTicket.v1";

    /// <summary>A session's last-seen time is written at most this often, not on every request.</summary>
    private static readonly TimeSpan TouchInterval = TimeSpan.FromMinutes(1);

    private readonly IDataProtector _tickets = protection.CreateProtector(TicketPurpose);

    public Task<string> StoreAsync(AuthenticationTicket ticket) => StoreAsync(ticket, CancellationToken.None);

    public async Task<string> StoreAsync(AuthenticationTicket ticket, CancellationToken cancellationToken)
    {
        await using var db = new DbSession(connections);
        return await StoreAsync(db, ticket, cancellationToken);
    }

    public Task<string> StoreAsync(AuthenticationTicket ticket, HttpContext httpContext, CancellationToken cancellationToken) =>
        StoreAsync(RequestDb(httpContext), ticket, cancellationToken);

    private async Task<string> StoreAsync(DbSession db, AuthenticationTicket ticket, CancellationToken cancellationToken)
    {
        var userId = UserOf(ticket);
        var key = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        Mark(ticket, key);
        var now = time.GetUtcNow().UtcDateTime;
        var absolute = now + options.Value.SessionLifetime;
        var expires = Min(now + options.Value.SessionIdle, absolute);
        ticket.Properties.ExpiresUtc = new DateTimeOffset(expires, TimeSpan.Zero);

        await db.ExecuteAsync("""
            DELETE TOP (100) FROM cmdb.UserSession WHERE ExpiresAt < @now OR AbsoluteExpiresAt < @now;
            INSERT INTO cmdb.UserSession (Id, UserId, Ticket, CreatedAt, LastSeenAt, ExpiresAt, AbsoluteExpiresAt)
            VALUES (@id, @userId, @ticket, @now, @now, @expires, @absolute);
            """,
            new { id = Hash(key), userId, ticket = Protect(ticket), now, expires, absolute }, cancellationToken);
        return key;
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket) => RenewAsync(key, ticket, CancellationToken.None);

    /// <summary>
    /// Replaces the ticket of a live session of the same person (the cookie handler calls this when a
    /// request that already has a session signs in again: only a new authenticator does, see
    /// <see cref="StartsNewSession"/>). Never moves a session to another person.
    /// </summary>
    public async Task RenewAsync(string key, AuthenticationTicket ticket, CancellationToken cancellationToken)
    {
        await using var db = new DbSession(connections);
        await RenewAsync(db, key, ticket, cancellationToken);
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket, HttpContext httpContext, CancellationToken cancellationToken) =>
        RenewAsync(RequestDb(httpContext), key, ticket, cancellationToken);

    private async Task RenewAsync(DbSession db, string key, AuthenticationTicket ticket, CancellationToken cancellationToken)
    {
        Mark(ticket, key);
        await db.ExecuteAsync("UPDATE cmdb.UserSession SET Ticket = @ticket WHERE Id = @id AND UserId = @userId;",
            new { id = Hash(key), userId = UserOf(ticket), ticket = Protect(ticket) }, cancellationToken);
    }

    public Task<AuthenticationTicket?> RetrieveAsync(string key) => RetrieveAsync(key, CancellationToken.None);

    /// <summary>
    /// On an endpoint that signs someone in (<see cref="StartsNewSession"/>), the session the browser
    /// presents is ended and not continued, so the sign-in that follows stores a new key: a new
    /// session token on every authentication, and the old one terminated (ASVS 5.0 V7.2.4). This also
    /// defeats session fixation, where a planted session key would otherwise carry the new sign-in.
    /// </summary>
    public async Task<AuthenticationTicket?> RetrieveAsync(string key, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var db = RequestDb(httpContext);
        if (httpContext.GetEndpoint()?.Metadata.GetMetadata<StartsNewSession>() is not null)
        {
            await RemoveAsync(db, key, cancellationToken);
            return null;
        }

        return await RetrieveAsync(db, key, cancellationToken);
    }

    /// <summary>
    /// The session's ticket if it is still live, with its idle expiry moved forward; null (and the row
    /// deleted) once it has expired.
    /// </summary>
    public async Task<AuthenticationTicket?> RetrieveAsync(string key, CancellationToken cancellationToken)
    {
        await using var db = new DbSession(connections);
        return await RetrieveAsync(db, key, cancellationToken);
    }

    private async Task<AuthenticationTicket?> RetrieveAsync(DbSession db, string key, CancellationToken cancellationToken)
    {
        var id = Hash(key);
        var now = time.GetUtcNow().UtcDateTime;
        var row = await db.QuerySingleOrDefaultAsync<SessionRow>(
            "SELECT Ticket, LastSeenAt, ExpiresAt, AbsoluteExpiresAt FROM cmdb.UserSession WHERE Id = @id;", new { id }, cancellationToken);
        if (row is null)
        {
            return null;
        }

        if (row.ExpiresAt <= now || row.AbsoluteExpiresAt <= now)
        {
            await db.ExecuteAsync("DELETE FROM cmdb.UserSession WHERE Id = @id;", new { id }, cancellationToken);
            return null;
        }

        var expires = row.ExpiresAt;
        if (now - row.LastSeenAt >= TouchInterval)
        {
            expires = Min(now + options.Value.SessionIdle, row.AbsoluteExpiresAt);
            await db.ExecuteAsync("UPDATE cmdb.UserSession SET LastSeenAt = @now, ExpiresAt = @expires WHERE Id = @id;",
                new { id, now, expires }, cancellationToken);
        }

        var ticket = Unprotect(row.Ticket);
        if (ticket is not null)
        {
            ticket.Properties.ExpiresUtc = new DateTimeOffset(expires, TimeSpan.Zero);
        }

        return ticket;
    }

    public Task RemoveAsync(string key) => RemoveAsync(key, CancellationToken.None);

    public async Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        await using var db = new DbSession(connections);
        await RemoveAsync(db, key, cancellationToken);
    }

    public Task RemoveAsync(string key, HttpContext httpContext, CancellationToken cancellationToken) =>
        RemoveAsync(RequestDb(httpContext), key, cancellationToken);

    private static async Task RemoveAsync(DbSession db, string key, CancellationToken cancellationToken) =>
        await db.ExecuteAsync("DELETE FROM cmdb.UserSession WHERE Id = @id;", new { id = Hash(key) }, cancellationToken);

    /// <summary>Ends every session of a person, inside the caller's transaction (with its audit row).</summary>
    public static Task<int> EndAllAsync(DbSession db, Guid userId, CancellationToken cancellationToken = default) =>
        db.ExecuteAsync("DELETE FROM cmdb.UserSession WHERE UserId = @userId;", new { userId }, cancellationToken);

    /// <summary>The session's row id (hex), as the <see cref="GoldmarkClaims.Session"/> claim of the signed-in principal.</summary>
    public static string SessionId(string key) => Convert.ToHexString(Hash(key));

    /// <summary>Ends every other session of a person, keeping <paramref name="sessionId"/> (a new authenticator).</summary>
    public static Task<int> EndOthersAsync(DbSession db, Guid userId, string sessionId, CancellationToken cancellationToken = default) =>
        db.ExecuteAsync("DELETE FROM cmdb.UserSession WHERE UserId = @userId AND Id <> @current;",
            new { userId, current = Convert.FromHexString(sessionId) }, cancellationToken);

    /// <summary>The request's own database session (owned by the request scope: never disposed here).</summary>
    private static DbSession RequestDb(HttpContext httpContext) => httpContext.RequestServices.GetRequiredService<DbSession>();

    private static Guid UserOf(AuthenticationTicket ticket) =>
        Guid.Parse(ticket.Principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("A session needs a user."));

    /// <summary>Records which session the ticket belongs to, so the signed-in principal can name it.</summary>
    private static void Mark(AuthenticationTicket ticket, string key)
    {
        var identity = (ClaimsIdentity)ticket.Principal.Identity!;
        foreach (var old in identity.FindAll(GoldmarkClaims.Session).ToList())
        {
            identity.RemoveClaim(old);
        }

        identity.AddClaim(new Claim(GoldmarkClaims.Session, SessionId(key)));
    }

    private byte[] Protect(AuthenticationTicket ticket) => _tickets.Protect(TicketSerializer.Default.Serialize(ticket));

    private AuthenticationTicket? Unprotect(byte[] data)
    {
        try
        {
            return TicketSerializer.Default.Deserialize(_tickets.Unprotect(data));
        }
        catch (CryptographicException)
        {
            // The key ring was lost or replaced: the session is unreadable, so it has ended.
            return null;
        }
    }

    private static byte[] Hash(string key) => SHA256.HashData(Encoding.UTF8.GetBytes(key));

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;

    private sealed record SessionRow(byte[] Ticket, DateTime LastSeenAt, DateTime ExpiresAt, DateTime AbsoluteExpiresAt);
}

/// <summary>Marks an endpoint that signs someone in: it never continues the session the request presents.</summary>
internal sealed class StartsNewSession;

/// <summary>The cookie handler's session store needs a singleton; options resolve it from DI.</summary>
internal sealed class SessionStoreSetup(SessionTicketStore store) : IPostConfigureOptions<CookieAuthenticationOptions>
{
    public void PostConfigure(string? name, CookieAuthenticationOptions options)
    {
        if (name == AuthSchemes.Session)
        {
            options.SessionStore = store;
        }
    }
}
