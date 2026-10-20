using System.Net;
using System.Text.Json;
using System.Xml.Linq;
using Goldmark.Api.Configuration;
using Goldmark.Api.Tests.Infrastructure;

namespace Goldmark.Api.Tests;

/// <summary>The API's anonymous starting points and the key ring, without a database.</summary>
public sealed class AuthApiTests(GoldmarkFactory factory) : IClassFixture<GoldmarkFactory>
{
    [Fact]
    public async Task The_antiforgery_endpoint_returns_a_token_and_sets_its_host_only_cookie()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/v1/auth/antiforgery", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.False(string.IsNullOrEmpty(json.RootElement.GetProperty("token").GetString()));
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith("__Host-goldmark-af=", cookie, StringComparison.Ordinal);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Session_state_is_anonymous_and_setup_unknown_until_the_database_answers()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/v1/auth/session", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal("""{"setup":"unknown","user":null}""", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void The_key_ring_directory_is_0700_and_key_files_0600()
    {
        var directory = Path.Combine(factory.CreateFile("unused"), "..", $"ring-{Guid.NewGuid():N}");
        var repository = new PrivateKeyRingRepository(directory);

        repository.StoreElement(new XElement("key", new XAttribute("id", "1")), "key-00000000-0000-0000-0000-000000000001");
        repository.StoreElement(new XElement("key", new XAttribute("id", "2")), "../escape");

        var files = Directory.GetFiles(directory);
        Assert.Equal(2, files.Length);
        Assert.Contains(files, f => Path.GetFileName(f) == "key-00000000-0000-0000-0000-000000000001.xml");
        Assert.All(files, f => Assert.Equal(Path.GetFullPath(directory), Path.GetDirectoryName(Path.GetFullPath(f))));
        Assert.Equal(2, repository.GetAllElements().Count);
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(directory));
            foreach (var file in files)
            {
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
            }
        }
    }
}
