using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace ManyHands.Data.Discord;

/// <summary>
/// The link <c>/link</c> hands a Discord user: their Discord id and an expiry,
/// sealed with Data Protection. Discord shows it to that user alone, and the
/// page it opens needs them signed in on the site, so the link proves the
/// Discord side and the sign-in proves the email side.
/// </summary>
public sealed class DiscordLinks(IDataProtectionProvider provider)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    private readonly ITimeLimitedDataProtector _protector =
        provider.CreateProtector("ManyHands.DiscordLink").ToTimeLimitedDataProtector();

    public string Issue(ulong discordUserId, string discordName) =>
        _protector.Protect($"{discordUserId}|{discordName}", Lifetime);

    /// <summary>The Discord account the link was made for, or null if it is forged or expired.</summary>
    public (ulong DiscordUserId, string DiscordName)? Read(string token)
    {
        try
        {
            var parts = _protector.Unprotect(token).Split('|', 2);
            return parts.Length == 2 && ulong.TryParse(parts[0], out var id) ? (id, parts[1]) : null;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}
