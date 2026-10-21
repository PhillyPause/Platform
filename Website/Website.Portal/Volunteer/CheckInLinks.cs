using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using QRCoder;

namespace Website.Portal.Volunteer;

/// <summary>
/// The code an organizer shows at the door. It carries the activity and an
/// expiry, sealed by Data Protection, so a volunteer can check themselves in
/// without an account while a guessed or stale link cannot. The keys are the
/// ones in Postgres, so a code survives a restart and needs no secret of its own.
/// </summary>
public sealed class CheckInLinks(IDataProtectionProvider provider)
{
    /// <summary>Long enough for an event that starts late; short enough that a photographed code is not a back door.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(12);

    private readonly ITimeLimitedDataProtector _protector =
        provider.CreateProtector("ManyHands.CheckIn").ToTimeLimitedDataProtector();

    public string Issue(Guid activityId) => _protector.Protect(activityId.ToString("N"), Lifetime);

    /// <summary>The activity the code was made for, or null if it is forged, mangled or expired.</summary>
    public Guid? Read(string token)
    {
        try
        {
            return Guid.TryParseExact(_protector.Unprotect(token), "N", out var id) ? id : null;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    /// <summary>A QR code for a link, as SVG markup drawn on the server.</summary>
    public static string QrSvg(string url)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
        return new SvgQRCode(data).GetGraphic(8);
    }
}
