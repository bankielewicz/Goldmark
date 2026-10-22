using System.Net;
using System.Text.Json;

namespace Goldmark.Api.IntegrationTests.Infrastructure;

/// <summary>A person's credentials as a test knows them.</summary>
public sealed record Person(string UserName, string Password, Authenticator Authenticator, IReadOnlyList<string> RecoveryCodes, Guid Id);

/// <summary>
/// One isolated installation with Goldmark running against it, plus the steps most tests start
/// from: first-run setup, inviting a person, accepting the invitation, signing in.
/// </summary>
public sealed class AuthScenario : IAsyncDisposable
{
    public const string AdminPassword = "violet ostrich parade 4071";

    private AuthScenario(SqlServerFixture sql, Installation installation, string keys, IDictionary<string, string?>? overrides)
    {
        Sql = sql;
        Installation = installation;
        App = new GoldmarkApp(sql, installation, keys, overrides);
    }

    public SqlServerFixture Sql { get; }

    public Installation Installation { get; }

    public GoldmarkApp App { get; private set; }

    public static async Task<AuthScenario> StartAsync(SqlServerFixture sql, IDictionary<string, string?>? overrides = null)
    {
        var (installation, _) = await sql.ProvisionAsync();
        var keys = Path.Combine(sql.NewDirectory(), "keys");
        return new AuthScenario(sql, installation, keys, overrides);
    }

    /// <summary>
    /// Stops this instance and starts a new one on the same database and key ring, with
    /// <paramref name="overrides"/> as its only extra settings.
    /// </summary>
    public async Task RestartAsync(IDictionary<string, string?>? overrides = null)
    {
        var keys = App.KeyDirectory;
        await App.DisposeAsync();
        App = new GoldmarkApp(Sql, Installation, keys, overrides);
    }

    /// <summary>First-run setup through the API; returns the signed-in administrator's client.</summary>
    public async Task<(ApiClient Client, Person Admin)> SetUpAsync(string userName = "pat.admin")
    {
        var token = await App.SetupTokenAsync();
        var client = App.Client();
        Ok(await client.PostAsync("/api/v1/setup/token", new { token }));
        Ok(await client.PostAsync("/api/v1/enrolment/account", new { userName, displayName = "Pat Admin", password = AdminPassword }));
        var authenticator = new Authenticator((await client.PostAsync("/api/v1/enrolment/authenticator")).Value("key"));
        var done = Ok(await client.PostAsync("/api/v1/enrolment/complete", new { code = await authenticator.NextCodeAsync() }));
        var user = done.Body.GetProperty("user");
        return (client, new Person(userName, AdminPassword, authenticator, Strings(done.Body.GetProperty("recoveryCodes")), user.GetProperty("id").GetGuid()));
    }

    /// <summary>An administrator invites a person; returns the link's token and code and the new account's id.</summary>
    public static async Task<(string Token, string Code, Guid Id)> InviteAsync(ApiClient admin, string userName, params string[] roles)
    {
        var created = await admin.PostAsync("/api/v1/users", new { displayName = Title(userName), userName, roles });
        Assert.True(created.Status == HttpStatusCode.Created, created.ToString());
        var link = created.Body.GetProperty("link");
        return (link.GetProperty("token").GetString()!, link.GetProperty("code").GetString()!, created.Body.GetProperty("user").GetProperty("id").GetGuid());
    }

    /// <summary>The invited person accepts: code, password, authenticator, verification. Returns their signed-in client.</summary>
    public async Task<(ApiClient Client, Person Person)> AcceptAsync(string token, string code, string userName, string password)
    {
        var client = App.Client();
        var redeemed = Ok(await client.PostAsync("/api/v1/links/redeem", new { token, code }));
        Assert.Equal("invitation", redeemed.Value("purpose"));
        Ok(await client.PostAsync("/api/v1/enrolment/password", new { password }));
        var authenticator = new Authenticator((await client.PostAsync("/api/v1/enrolment/authenticator")).Value("key"));
        var done = Ok(await client.PostAsync("/api/v1/enrolment/complete", new { code = await authenticator.NextCodeAsync() }));
        var id = done.Body.GetProperty("user").GetProperty("id").GetGuid();
        return (client, new Person(userName, password, authenticator, Strings(done.Body.GetProperty("recoveryCodes")), id));
    }

    public async Task<(ApiClient Client, Person Person)> InviteAndAcceptAsync(ApiClient admin, string userName, params string[] roles)
    {
        var (token, code, _) = await InviteAsync(admin, userName, roles);
        return await AcceptAsync(token, code, userName, $"{userName} likes long walks 93");
    }

    /// <summary>Both sign-in steps; returns the signed-in client.</summary>
    public async Task<ApiClient> SignInAsync(Person person)
    {
        var client = App.Client();
        Assert.Equal("two-factor", Ok(await client.PostAsync("/api/v1/auth/sign-in", new { userName = person.UserName, password = person.Password })).Value("next"));
        Ok(await client.PostAsync("/api/v1/auth/sign-in/two-factor", new { code = await person.Authenticator.NextCodeAsync() }));
        return client;
    }

    /// <summary>Audit rows of an action, oldest first, as "TargetId|ActorType|ActorId|Details".</summary>
    public async Task<IReadOnlyList<string>> AuditAsync(string action)
    {
        var rows = await Sql.AdminScalarAsync<string?>(Installation.Database, $"""
            SELECT STRING_AGG(CONCAT(TargetId, '|', ActorType, '|', ActorId, '|', CAST(Details AS nvarchar(max))), CHAR(10)) WITHIN GROUP (ORDER BY Id)
            FROM cmdb.AuditLog WHERE Action = '{action}';
            """);
        // No rows: STRING_AGG gives NULL, which the scalar read returns as "" (not a row).
        return string.IsNullOrEmpty(rows) ? [] : rows.Split('\n');
    }

    public Task<string?> AuditTextAsync() =>
        Sql.AdminScalarAsync<string?>(Installation.Database,
            "SELECT STRING_AGG(CONCAT(Action, ' ', TargetId, ' ', CAST(Details AS nvarchar(max))), CHAR(10)) FROM cmdb.AuditLog;");

    public static Response Ok(Response response)
    {
        Assert.True(response.Status == HttpStatusCode.OK, response.ToString());
        return response;
    }

    public static IReadOnlyList<string> Strings(JsonElement array) => [.. array.EnumerateArray().Select(e => e.GetString()!)];

    private static string Title(string userName) =>
        string.Join(' ', userName.Split('.').Select(part => char.ToUpperInvariant(part[0]) + part[1..]));

    public async ValueTask DisposeAsync() => await App.DisposeAsync();
}
