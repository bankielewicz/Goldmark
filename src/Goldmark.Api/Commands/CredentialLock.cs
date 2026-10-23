using System.Security.Cryptography;

namespace Goldmark.Api.Commands;

/// <summary>
/// One writer of Goldmark's credential files and SQL logins at a time (Codex on #39): <c>bootstrap-db</c> and
/// <c>rotate-db-credentials</c> both write a file and change its login, and two runs interleaved could leave them
/// with different passwords. Each takes this lock on its administrator connection before it changes anything and
/// doesn't wait for it. Both read the files before they can connect, so each also checks that the files are still
/// those it read, now that it holds the lock.
/// </summary>
internal static class CredentialLock
{
    // Session-owned, in master: released when the connection closes.
    public const string Sql = """
        DECLARE @result int;
        EXEC @result = sys.sp_getapplock @Resource = N'goldmark:credentials', @LockMode = N'Exclusive',
            @LockOwner = N'Session', @LockTimeout = 0;
        SELECT @result;
        """;

    public const string Held = "another bootstrap-db or rotate-db-credentials run is changing Goldmark's logins; run this again when it has finished. Nothing was changed.";

    public const string Changed = "a credential file changed while this run was starting (another bootstrap-db or rotate-db-credentials run?); run this again. Nothing was changed.";

    /// <summary>The files' contents, so a change between reading them and taking the lock is noticed. Nothing is kept but a hash.</summary>
    public static string Fingerprint(params string?[] files)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in files)
        {
            foreach (var path in new[] { file, file is null ? null : file + ".new" })
            {
                hash.AppendData(System.Text.Encoding.UTF8.GetBytes((path ?? "") + "\n"));
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    try
                    {
                        hash.AppendData(File.ReadAllBytes(path));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        hash.AppendData("unreadable"u8);
                    }
                }
            }
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
