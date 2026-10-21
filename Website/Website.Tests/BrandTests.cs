using FluentAssertions;

namespace Website.Tests;

/// <summary>
/// Brand/Logo/PhillyPause.svg is the one master logo. The site serves a copy
/// of it, and the copy may not drift.
/// </summary>
public class BrandTests
{
    [Fact]
    public void The_served_logo_is_the_master()
    {
        var master = File.ReadAllBytes(Path.Combine(TestPaths.RepoRoot, "Brand", "Logo", "PhillyPause.svg"));
        var served = File.ReadAllBytes(Path.Combine(TestPaths.RepoRoot, "Website", "Website.Portal", "wwwroot", "phillypause-logo.svg"));

        served.Should().Equal(master, "copy Brand/Logo/PhillyPause.svg over the site's copy after changing it");
    }
}
