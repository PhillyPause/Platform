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
}
