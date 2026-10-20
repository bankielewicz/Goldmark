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
/// stored or logged. Failures are kept until the name locks, as an account's count is (it has no idle
/// expiry either), and the lockout ends after the same time. Bounded: when it is full, a new name's
/// failure can go uncounted until the cache compacts (expired entries, then the least recently used
/// names, go first), and a restart forgets every name. Both are accepted: an account's count survives them.
/// </summary>
internal sealed class UnknownUserThrottle(IOptions<AuthOptions> options, TimeProvider time) : IDisposable
{
    private const int Capacity = 10_000;

    // The cache keeps the app's time, so what it expires agrees with the comparisons below (and tests can move it).
    private readonly MemoryCache _entries = new(new MemoryCacheOptions { SizeLimit = Capacity, Clock = new Clock(time) });

    public bool IsLockedOut(string normalizedUserName)
    {
        lock (_entries)
        {
            return _entries.TryGetValue(Key(normalizedUserName), out Entry? entry) && entry!.LockedUntil > time.GetUtcNow();
        }
    }

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
            var expiry = new MemoryCacheEntryOptions { Size = 1 };
            if (locked)
            {
                entry.Failures = 0;
                entry.LockedUntil = now + options.Value.Lockout;
                // Once the lockout ends nothing is left to remember: the count starts again from zero.
                expiry.AbsoluteExpiration = entry.LockedUntil;
            }

            _entries.Set(key, entry, expiry);
            return locked;
        }
    }

    public void Dispose() => _entries.Dispose();

    private static string Key(string normalizedUserName) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedUserName)));

    private sealed class Clock(TimeProvider time) : Microsoft.Extensions.Internal.ISystemClock
    {
        public DateTimeOffset UtcNow => time.GetUtcNow();
    }

    private sealed class Entry
    {
        public int Failures { get; set; }

        public DateTimeOffset LockedUntil { get; set; } = DateTimeOffset.MinValue;
    }
}
