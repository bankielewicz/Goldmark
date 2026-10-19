using Goldmark.Api.Security;
using Goldmark.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Goldmark.Api.Tests;

/// <summary>
/// Seed of the M6 authorization enumeration test. Every endpoint must declare its access
/// policy. In slice 1 all endpoints are anonymous by design; a new endpoint fails this test
/// until it is classified here and in docs/security/endpoint-access.md. Slice 3 extends this to
/// require an authorization policy on everything not listed as anonymous.
/// </summary>
public sealed class EndpointInventoryTests(GoldmarkFactory factory) : IClassFixture<GoldmarkFactory>
{
    private static readonly Dictionary<string, string> AnonymousByDesign = new()
    {
        ["Health: live"] = "/health/live",
        ["Health: ready"] = "/health/ready",
        ["Health: not found"] = "/health/{**path}",
        ["API: not found"] = "/api/{**path}",
        ["SPA: shell"] = "{**path:nonfile}",
    };

    [Fact]
    public void Every_endpoint_declares_its_access_policy()
    {
        using var client = factory.CreateClient();
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();

        var inventory = endpoints.ToDictionary(e => e.DisplayName ?? "(unnamed)", e => e.RoutePattern.RawText ?? "");
        Assert.Equal(AnonymousByDesign.OrderBy(p => p.Key), inventory.OrderBy(p => p.Key));

        foreach (var endpoint in endpoints)
        {
            var access = endpoint.Metadata.GetMetadata<EndpointAccess>();
            Assert.True(access is not null, $"{endpoint.DisplayName} has no EndpointAccess metadata");
            Assert.Equal(AccessPolicy.Anonymous, access.Policy);
            Assert.False(string.IsNullOrWhiteSpace(access.Rationale));
            Assert.NotNull(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
        }
    }
}
