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
        // Goldmark stops itself, and its host then disposes its services, possibly before the factory has
        // finished starting it (it did on GitHub's runner): the stop is held until the test has looked.
        var release = new TaskCompletionSource();
        await using var started = app.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddHostedService(_ => new HeldStop(release.Task))));
        try
        {
            var rights = started.Services.GetRequiredService<RuntimeRights>();
            var lifetime = started.Services.GetRequiredService<IHostApplicationLifetime>();

            await app.Logs.WaitForAsync(e => e.Any(x => GoldmarkApp.IsEvent(x, 1005)), TimeSpan.FromSeconds(30));
            var refused = Assert.Single(app.Logs.Events, e => GoldmarkApp.IsEvent(e, 1005));

            Assert.Contains("holds more than goldmark_runtime", refused.RenderMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
            Assert.Contains("VIEW SERVER STATE", refused.RenderMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
            Assert.Equal(3, rights.ExitCode);
            Assert.True(lifetime.ApplicationStopping.IsCancellationRequested);
            Assert.False(rights.Checked);
        }
        finally
        {
            release.SetResult();
        }
    }

    /// <summary>Registered last, so stopped first: the host stops, and is disposed, only once the test releases it.</summary>
    private sealed class HeldStop(Task release) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => release.WaitAsync(cancellationToken);
    }
}
