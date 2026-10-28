using FluentAssertions;

namespace Website.Tests;

/// <summary>
/// Brand/Logo/PhillyPause.svg is the one master logo. The site and the
/// explainer video each use a copy of it, and no copy may drift.
/// </summary>
public class BrandTests
{
    public static TheoryData<string> Copies() => new()
    {
        "Website/Website.Portal/wwwroot/phillypause-logo.svg",
        "Video/Explainer/production/assets/brand/logo.svg",
    };

    [Theory]
    [MemberData(nameof(Copies))]
    public void Every_copy_of_the_logo_is_the_master(string copy)
    {
        var master = File.ReadAllBytes(Path.Combine(TestPaths.RepoRoot, "Brand", "Logo", "PhillyPause.svg"));
        var served = File.ReadAllBytes(Path.Combine(TestPaths.RepoRoot, copy));

        served.Should().Equal(master, $"copy Brand/Logo/PhillyPause.svg over {copy} after changing it");
    }

    /// <summary>
    /// The joining emails link to a PNG of the logo, since mail clients do not
    /// draw SVG. It is a rendering of the master, not a copy, so only its
    /// presence can be checked; BRAND.md says how to make it again.
    /// </summary>
    [Fact]
    public void The_email_logo_is_served_where_the_emails_point()
    {
        var path = Path.Combine(TestPaths.RepoRoot, "Website", "Website.Portal", "wwwroot",
            ManyHands.Data.Email.EmailTemplates.LogoPath.TrimStart('/'));

        File.Exists(path).Should().BeTrue($"the joining emails show {ManyHands.Data.Email.EmailTemplates.LogoPath}");
    }
}
