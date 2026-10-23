using System.Globalization;
using Goldmark.Api.Health;
using Goldmark.Api.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Goldmark.Api.IntegrationTests;

/// <summary>
/// <c>Goldmark:Database:CheckRightsAtStart</c> (the Windows service, DEC-043 items 2 and 11): the application
/// checks its own login's rights at start, as migrate does for the runtime login on Docker; readiness waits for
/// the check, and a login that holds more than goldmark_runtime stops Goldmark with exit code 3.
/// </summary>
[Collection(SqlServerTests.Name)]
public sealed class RightsAtStartTests(SqlServerFixture sql)
{
    private static readonly Dictionary<string, string?> Check = new() { ["Goldmark:Database:CheckRightsAtStart"] = "true" };

    [Fact]
    public async Task A_login_with_exactly_its_role_is_ready_once_checked()
    {
        var (installation, _) = await sql.ProvisionAsync();
        await using var app = new GoldmarkApp(sql, installation, Path.Combine(sql.NewDirectory(), "keys"), Check);

        await app.EventAsync(1004);
        using var ready = await app.CreateClient().GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal(0, app.Services.GetRequiredService<RuntimeRights>().ExitCode);
    }

    [Fact]
    public async Task A_login_that_holds_more_stops_Goldmark_and_is_never_ready()
    {
        var (installation, _) = await sql.ProvisionAsync();
        await sql.AsAdminAsync("master", $"GRANT VIEW SERVER STATE TO [{installation.RuntimeUser}];");
        await using var app = new GoldmarkApp(sql, installation, Path.Combine(sql.NewDirectory(), "keys"), Check);
        // Taken before the refusal: once Goldmark stops itself, the host disposes its services.
        var rights = app.Services.GetRequiredService<RuntimeRights>();
        var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();

        var refused = await app.EventAsync(1005);

        Assert.Contains("holds more than goldmark_runtime", refused.RenderMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.Contains("VIEW SERVER STATE", refused.RenderMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.Equal(3, rights.ExitCode);
        Assert.True(lifetime.ApplicationStopping.IsCancellationRequested);
        Assert.False(rights.Checked);
    }
}
