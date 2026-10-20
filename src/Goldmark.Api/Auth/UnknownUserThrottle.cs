using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Goldmark.Api.Auth;

/// <summary>
/// Counts failed sign-ins for user names that have no usable account (unknown, disabled, or invited
/// but not yet set up), with the same threshold and lockout as real accounts, so a "locked" answer
/// never confirms that an account exists (slice-3 screens canvas note). Kept in memory, keyed by a
/// hash of the normalized name: what someone typed as a user name may be a password, so it is never
/// stored or logged. Bounded; a restart forgets it, as it would forget nothing of value.
/// </summary>
internal sealed class UnknownUserThrottle(IOptions<AuthOptions> options, TimeProvider time) : IDisposable
{
    private const int Capacity = 10_000;

    private readonly MemoryCache _entries = new(new MemoryCacheOptions { SizeLimit = Capacity });

    public bool IsLockedOut(string normalizedUserName) =>
        _entries.TryGetValue(Key(normalizedUserName), out Entry? entry) && entry!.LockedUntil > time.GetUtcNow();

    /// <summary>Records a failure; returns true if this failure locked the name.</summary>
    public bool RecordFailure(string normalizedUserName)
    {
        var key = Key(normalizedUserName);
        var now = time.GetUtcNow();
        lock (_entries)
        {
            var entry = _entries.TryGetValue(key, out Entry? existing) ? existing! : new Entry();
            if (entry.LockedUntil > now)
            {
                return false;
            }

            entry.Failures++;
            var locked = entry.Failures >= options.Value.LockoutThreshold;
            if (locked)
            {
                entry.Failures = 0;
                entry.LockedUntil = now + options.Value.Lockout;
            }

            _entries.Set(key, entry, new MemoryCacheEntryOptions
            {
                Size = 1,
                SlidingExpiration = options.Value.Lockout,
            });
            return locked;
        }
    }

    public void Dispose() => _entries.Dispose();

    private static string Key(string normalizedUserName) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedUserName)));

    private sealed class Entry
    {
        public int Failures { get; set; }

        public DateTimeOffset LockedUntil { get; set; } = DateTimeOffset.MinValue;
    }
}
