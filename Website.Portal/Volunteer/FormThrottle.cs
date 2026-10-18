using System.Net;
using System.Threading.RateLimiting;

namespace Website.Portal.Volunteer;

/// <summary>
/// Per-address limits on the anonymous forms that send mail. Counted on the
/// submit, not the page view, so reading a page never uses up anyone's turns.
/// The flood cap in EmailDispatcher stands behind this: however many
/// addresses a script rotates through, no inbox gets more than ten an hour.
/// </summary>
public sealed class FormThrottle : IDisposable
{
    // Sign-in links are the tighter one: asking for five in fifteen minutes is
    // already somebody who is not getting the email.
    private readonly PartitionedRateLimiter<string> _signIn = Limiter(permits: 5, TimeSpan.FromMinutes(15));

    // Signing up and joining. Looser, because a table of volunteers at one
    // venue shares an address, but bounded.
    private readonly PartitionedRateLimiter<string> _publicWrite = Limiter(permits: 20, TimeSpan.FromMinutes(5));

    public bool AllowSignIn(HttpContext http) => Take(_signIn, http);

    public bool AllowPublicWrite(HttpContext http) => Take(_publicWrite, http);

    private static bool Take(PartitionedRateLimiter<string> limiter, HttpContext http)
    {
        using var lease = limiter.AttemptAcquire(ClientAddress(http));
        return lease.IsAcquired;
    }

    /// <summary>
    /// Cloudflare fronts the site and says who it is talking to in
    /// CF-Connecting-IP; without it every reader would share Cloudflare's
    /// address and one bucket. A request that reaches App Platform directly can
    /// forge the header, which only lets it pick its own bucket — the per-inbox
    /// flood cap still holds.
    /// </summary>
    internal static string ClientAddress(HttpContext http) =>
        http.Request.Headers["CF-Connecting-IP"].FirstOrDefault() is { } header
        && IPAddress.TryParse(header, out var forwarded)
            ? forwarded.ToString()
            : http.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static PartitionedRateLimiter<string> Limiter(int permits, TimeSpan window) =>
        PartitionedRateLimiter.Create<string, string>(address =>
            RateLimitPartition.GetFixedWindowLimiter(address, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permits,
                Window = window,
                QueueLimit = 0
            }));

    public void Dispose()
    {
        _signIn.Dispose();
        _publicWrite.Dispose();
    }
}
