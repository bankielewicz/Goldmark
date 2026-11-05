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
            await StoppingAsync(lifetime);
            Assert.False(rights.Checked);
        }
        finally
        {
            release.SetResult();
        }
    }

    [Fact]
    public async Task A_refusal_while_the_host_is_still_starting_stops_it_once_it_has_started()
    {
        var (installation, _) = await sql.ProvisionAsync();
        await sql.AsAdminAsync("master", $"GRANT VIEW SERVER STATE TO [{installation.RuntimeUser}];");
        await using var app = new GoldmarkApp(sql, installation, Path.Combine(sql.NewDirectory(), "keys"), Check);
        // A start step after the check that lasts until the refusal and honours the start's token, as Kestrel's
        // binding can on a slow runner: a stop asked for during the start cancelled it, the start threw out of
        // RunAsync, and the Windows service ended as a crash (exit code 1067) instead of the refusal (CI, PR #59).
        var release = new TaskCompletionSource();
        await using var started = app.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddHostedService(provider => new SlowStart(provider.GetRequiredService<RuntimeRights>(), release.Task))));
        try
        {
            var lifetime = started.Services.GetRequiredService<IHostApplicationLifetime>();

            Assert.True(lifetime.ApplicationStarted.IsCancellationRequested);
            await StoppingAsync(lifetime);
            Assert.Equal(3, started.Services.GetRequiredService<RuntimeRights>().ExitCode);
        }
        finally
        {
            release.SetResult();
        }
    }

    private static async Task StoppingAsync(IHostApplicationLifetime lifetime)
    {
        var stopping = new TaskCompletionSource();
        using (lifetime.ApplicationStopping.Register(() => stopping.TrySetResult()))
        {
            await stopping.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        }
    }

    /// <summary>
    /// Registered after the check, so started after it: its start lasts until the check has refused (and a moment
    /// more), then honours the start's token. Its stop is held like <see cref="HeldStop"/>'s.
    /// </summary>
    private sealed class SlowStart(RuntimeRights rights, Task release) : IHostedService
    {
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            for (var tries = 0; rights.ExitCode != RuntimeRightsCheck.Refused && tries < 600; tries++)
            {
                await Task.Delay(50, CancellationToken.None);
            }

            await Task.Delay(200, CancellationToken.None);
            cancellationToken.ThrowIfCancellationRequested();
        }

        public Task StopAsync(CancellationToken cancellationToken) => release.WaitAsync(cancellationToken);
    }

    /// <summary>Registered last, so stopped first: the host stops, and is disposed, only once the test releases it.</summary>
    private sealed class HeldStop(Task release) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => release.WaitAsync(cancellationToken);
    }
}
