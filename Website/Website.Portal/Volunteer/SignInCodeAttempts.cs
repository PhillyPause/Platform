using System.Collections.Concurrent;

namespace Website.Portal.Volunteer;

/// <summary>
/// Wrong sign-in codes, counted per person rather than per address, because
/// guessing from many addresses at one inbox is what the per-address
/// FormThrottle cannot see. Five wrong and that person's code is thrown away,
/// so a code is one in a million to guess and gets five tries. Kept in memory:
/// the site runs as one instance, and a restart only hands a guesser five more
/// tries at a code that dies within twenty minutes anyway.
/// </summary>
public sealed class SignInCodeAttempts
{
    public const int Most = 5;

    private readonly ConcurrentDictionary<Guid, int> _wrong = new();

    /// <summary>Counts a wrong code; true when that was the last one allowed.</summary>
    public bool Wrong(Guid volunteerId)
    {
        if (_wrong.AddOrUpdate(volunteerId, 1, (_, count) => count + 1) < Most)
            return false;

        _wrong.TryRemove(volunteerId, out _);
        return true;
    }

    /// <summary>A right code, or a fresh one sent: the count starts again.</summary>
    public void Clear(Guid volunteerId) => _wrong.TryRemove(volunteerId, out _);
}
