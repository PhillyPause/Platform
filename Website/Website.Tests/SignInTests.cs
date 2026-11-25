using FluentAssertions;
using Website.Portal.Components.Volunteer;
using Website.Portal.Volunteer;

namespace Website.Tests;

/// <summary>
/// The sign-in dialog's two guards that need no database: five wrong codes
/// per person, and a ReturnUrl that only ever leads back into this site.
/// </summary>
public class SignInTests
{
    [Fact]
    public void The_fifth_wrong_code_is_the_last()
    {
        var attempts = new SignInCodeAttempts();
        var person = Guid.NewGuid();

        Enumerable.Range(1, SignInCodeAttempts.Most - 1).Select(_ => attempts.Wrong(person)).ToList()
            .Should().AllBeEquivalentTo(false);
        attempts.Wrong(person).Should().BeTrue();

        // The code is thrown away then, and a fresh one starts the count again.
        attempts.Wrong(person).Should().BeFalse();
    }

    [Fact]
    public void Wrong_codes_are_counted_per_person_and_a_right_one_clears_them()
    {
        var attempts = new SignInCodeAttempts();
        var person = Guid.NewGuid();
        var other = Guid.NewGuid();

        for (var i = 1; i < SignInCodeAttempts.Most; i++) attempts.Wrong(person);
        attempts.Wrong(other).Should().BeFalse();

        attempts.Clear(person);
        attempts.Wrong(person).Should().BeFalse();
    }

    [Theory]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("/", "/")]
    [InlineData("/team/partners", "/team/partners")]
    [InlineData("/volunteer/me?tab=1", "/volunteer/me?tab=1")]
    [InlineData("//evil.example/", "/")]
    [InlineData("/\\evil.example/", "/")]
    [InlineData("https://evil.example/", "/")]
    [InlineData("team", "/")]
    public void A_return_address_stays_on_this_site(string? returnUrl, string expected) =>
        SignInDialog.SafeReturn(returnUrl).Should().Be(expected);
}
