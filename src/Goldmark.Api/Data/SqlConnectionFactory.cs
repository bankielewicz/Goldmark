using Goldmark.Api.Configuration;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Goldmark.Api.Data;

/// <summary>
/// Builds SQL Server connection strings from <see cref="DatabaseOptions"/>. Encryption is always
/// on and the server certificate is always validated (by chain or by pinned certificate);
/// <c>TrustServerCertificate</c> is never enabled. With Windows authentication (DEC-043) the
/// connection uses this process's Windows account and carries no name or password.
/// </summary>
internal sealed class SqlConnectionFactory(IOptions<DatabaseOptions> options)
{
    public SqlConnectionStringBuilder CreateBuilder() =>
        options.Value.UsesWindowsAuthentication ? BuildIntegrated(options.Value) : Build(options.Value);

    /// <summary>A connection as this process's Windows account (the service's, or an operator's for the commands).</summary>
    public static SqlConnectionStringBuilder BuildIntegrated(DatabaseOptions db, string? database = null)
    {
        var builder = Common(db, database);
        builder.IntegratedSecurity = true;
        return builder;
    }

    public static SqlConnectionStringBuilder Build(
        DatabaseOptions db,
        string? userId = null,
        string? password = null,
        string? database = null)
    {
        var builder = Common(db, database);
        builder.UserID = userId ?? db.User;
        builder.Password = password ?? db.Password;
        return builder;
    }

    private static SqlConnectionStringBuilder Common(DatabaseOptions db, string? database)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = $"tcp:{db.Host},{db.Port}",
            InitialCatalog = database ?? db.Name,
            Encrypt = db.Encrypt == "Mandatory" ? SqlConnectionEncryptOption.Mandatory : SqlConnectionEncryptOption.Strict,
            TrustServerCertificate = false,
            PersistSecurityInfo = false,
            ConnectTimeout = db.ConnectTimeoutSeconds,
            ApplicationName = "Goldmark",
        };
        if (!string.IsNullOrWhiteSpace(db.ServerCertificatePath))
        {
            builder.ServerCertificate = db.ServerCertificatePath;
        }

        if (!string.IsNullOrWhiteSpace(db.HostNameInCertificate))
        {
            builder.HostNameInCertificate = db.HostNameInCertificate;
        }

        return builder;
    }
}
