using Goldmark.Api.Data;

namespace Goldmark.SqlGate.Fixture;

/// <summary>
/// Calls the gate must refuse. check-sql-gate.sh expects CA1857 on every line that ends with a
/// "// violation" marker, and no other error.
/// </summary>
internal static class Violations
{
    private static readonly string NotConstant = "SELECT 1;";

    public static Task<int> Interpolation(DbSession db, string name) =>
        db.ExecuteAsync($"DELETE FROM cmdb.UserAccount WHERE UserName = '{name}';"); // violation

    public static Task<int> Concatenation(DbSession db, string name) =>
        db.ExecuteAsync("DELETE FROM cmdb.UserAccount WHERE UserName = '" + name + "';"); // violation

    public static Task<int> Variable(DbSession db, string sql) => db.ExecuteAsync(sql); // violation

    public static Task<int> ReadonlyField(DbSession db) => db.ExecuteScalarAsync<int>(NotConstant); // violation

    public static Task<IReadOnlyList<string>> Format(DbSession db, string column) =>
        db.QueryAsync<string>(string.Format(System.Globalization.CultureInfo.InvariantCulture, "SELECT {0} FROM cmdb.Role;", column)); // violation
}
