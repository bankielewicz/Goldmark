using Goldmark.Api.Data;

namespace Goldmark.SqlGate.Fixture;

/// <summary>Calls the gate must accept: constant SQL, values as parameters.</summary>
internal static class Constants
{
    private const string ByName = "SELECT Id FROM cmdb.UserAccount WHERE NormalizedUserName = @name;";
    private const string Table = "cmdb.UserAccount";

    public static Task<Guid> Literal(DbSession db, string name) =>
        db.ExecuteScalarAsync<Guid>("SELECT Id FROM cmdb.UserAccount WHERE NormalizedUserName = @name;", new { name });

    public static Task<Guid> Constant(DbSession db, string name) => db.ExecuteScalarAsync<Guid>(ByName, new { name });

    // Interpolating and concatenating constants yields a constant.
    public static Task<int> ConstantParts(DbSession db) =>
        db.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {Table};");

    public static Task<int> ConstantConcatenation(DbSession db) => db.ExecuteAsync("DELETE FROM " + Table + " WHERE 1 = 0;");
}
