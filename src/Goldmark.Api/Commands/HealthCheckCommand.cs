using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Goldmark.Api.Configuration;
using Goldmark.Api.Security;

namespace Goldmark.Api.Commands;

/// <summary>
/// <c>healthcheck</c>: container HEALTHCHECK for the shell-less runtime image. Calls
/// <c>/health/live</c> over loopback HTTPS and pins the server certificate to the one on disk,
/// so no certificate validation is ever disabled.
/// </summary>
internal static class HealthCheckCommand
{
    public const string Name = "healthcheck";

    public static async Task<int> RunAsync()
    {
        var https = CommandConfiguration.Build().GetSection(HttpsOptions.SectionName).Get<HttpsOptions>() ?? new HttpsOptions();
        byte[] expected;
        try
        {
            using var certificate = X509CertificateLoader.LoadCertificateFromFile(TlsCertificateProvider.PublicCertificatePath(https));
            expected = certificate.GetCertHash(HashAlgorithmName.SHA256);
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
        {
            await Console.Error.WriteLineAsync($"healthcheck: cannot read the server certificate ({ex.GetType().Name}).");
            return 1;
        }

        using var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(3),
            SslOptions =
            {
                RemoteCertificateValidationCallback = (_, presented, _, _) =>
                    presented is not null
                    && CryptographicOperations.FixedTimeEquals(presented.GetCertHash(HashAlgorithmName.SHA256), expected),
            },
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        try
        {
            using var response = await client.GetAsync(new Uri($"https://127.0.0.1:{https.Port}/health/live"));
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            await Console.Error.WriteLineAsync($"healthcheck: liveness request failed ({ex.GetType().Name}).");
            return 1;
        }
    }
}
