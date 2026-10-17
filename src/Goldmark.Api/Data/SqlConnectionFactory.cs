using Goldmark.Api.Configuration;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Goldmark.Api.Data;

/// <summary>
/// Builds SQL Server connection strings from <see cref="DatabaseOptions"/>. Encryption is always
/// on and the server certificate is always validated (by chain or by pinned certificate);
/// <c>TrustServerCertificate</c> is never enabled.
/// </summary>
internal sealed class SqlConnectionFactory(IOptions<DatabaseOptions> options)
{
    public SqlConnectionStringBuilder CreateBuilder() => Build(options.Value);

    public static SqlConnectionStringBuilder Build(
        DatabaseOptions db,
        string? userId = null,
        string? password = null,
        string? database = null)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = $"tcp:{db.Host},{db.Port}",
            InitialCatalog = database ?? db.Name,
            UserID = userId ?? db.User,
            Password = password ?? db.Password,
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
