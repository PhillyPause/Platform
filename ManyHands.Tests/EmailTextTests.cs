using FluentAssertions;
using ManyHands.Data;
using ManyHands.Data.Email;

namespace ManyHands.Tests;

/// <summary>
/// The text volunteers actually read. These pin the load-bearing parts: the
/// time is Philadelphia's and says so, a waitlist is never described as a
/// confirmation, and mail that is not a sign-in link always carries a way out.
/// </summary>
public sealed class EmailTextTests
{
    private const string ManageUrl = "https://example.test/volunteer/me";
    private const string UnsubscribeUrl = "https://example.test/volunteer/unsubscribe/abc";
    private const string Title = "Saturday flyering";
    private const string Where = "Clark Park";

    // 14:00 UTC on an August Saturday is 10:00 AM in Philadelphia.
    private static readonly DateTimeOffset AugustSaturday = new(2026, 8, 22, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public void An_august_instant_reads_as_eastern_daylight_time() =>
        LocalTime.Format(AugustSaturday).Should().Be("Saturday, August 22 at 10:00 AM (Eastern time)");

    [Fact]
    public void A_january_instant_crosses_back_to_standard_time() =>
        LocalTime.Format(new DateTimeOffset(2026, 1, 24, 14, 0, 0, TimeSpan.Zero))
            .Should().Contain("9:00 AM (Eastern time)");

    [Fact]
    public void Only_the_instant_matters_not_the_offset_it_was_written_with() =>
        LocalTime.Format(new DateTimeOffset(2026, 8, 22, 10, 0, 0, TimeSpan.FromHours(-4)))
            .Should().Be(LocalTime.Format(AugustSaturday));

    [Fact]
    public void The_sign_in_email_carries_the_link_and_its_lifetime_and_no_unsubscribe()
    {
        const string link = "https://example.test/volunteer/sign-in/xyz";

        var (subject, body) = EmailTemplates.MagicLink("Maya", link, TimeSpan.FromMinutes(20));

        subject.Should().Contain("sign-in").And.Contain("PhillyPause");
        body.Should().Contain(link).And.Contain("20 minutes").And.Contain("Maya");
        // Requested by the recipient: an unsubscribe here would let someone
        // opt out of their own sign-in.
        body.Should().NotContainEquivalentOf("unsubscribe");
    }

    [Fact]
    public void A_confirmation_states_the_local_time_and_the_place()
    {
        var (subject, body) = EmailTemplates.SignupConfirmation(
            "Maya", Title, AugustSaturday, Where, waitlisted: false, ManageUrl, UnsubscribeUrl);

        subject.Should().Be($"Signed up: {Title}");
        body.Should().Contain($"You are signed up for {Title}.")
            .And.Contain("10:00 AM (Eastern time)")
            .And.Contain(Where)
            .And.NotContain("2:00 PM")
            .And.NotContain("waitlist");
    }

    [Fact]
    public void A_waitlist_notice_says_waitlist_in_the_subject()
    {
        var (subject, body) = EmailTemplates.SignupConfirmation(
            "Maya", Title, AugustSaturday, Where, waitlisted: true, ManageUrl, UnsubscribeUrl);

        subject.Should().Be($"On the waitlist: {Title}");
        body.Should().Contain($"You are on the waitlist for {Title}.");
    }

    [Fact]
    public void A_waitlisted_volunteer_is_told_the_truth()
    {
        var (_, body) = EmailTemplates.SignupConfirmation(
            "Maya", Title, AugustSaturday, Where, waitlisted: true, ManageUrl, UnsubscribeUrl);

        body.Should().Contain("on the waitlist").And.NotContain("You are signed up for");
    }

    [Fact]
    public void Ongoing_work_does_not_claim_a_time_or_a_place()
    {
        var (_, body) = EmailTemplates.SignupConfirmation(
            "Maya", "Call your representative", startsAt: null, location: null,
            waitlisted: false, ManageUrl, UnsubscribeUrl);

        body.Should().Contain("Ongoing").And.Contain("To be confirmed");
    }

    [Fact]
    public void A_reminder_includes_the_meeting_link_only_when_there_is_one()
    {
        const string meeting = "https://meet.example.test/abc";

        var (_, withLink) = EmailTemplates.ActivityReminder(
            "Maya", Title, AugustSaturday, "Online", meeting, ManageUrl, UnsubscribeUrl);
        var (_, withoutLink) = EmailTemplates.ActivityReminder(
            "Maya", Title, AugustSaturday, "Online", meetingUrl: null, ManageUrl, UnsubscribeUrl);

        withLink.Should().Contain(meeting);
        withoutLink.Should().NotContain("Join online");
    }

    [Fact]
    public void Every_message_but_the_sign_in_link_offers_a_way_out()
    {
        var bodies = new[]
        {
            EmailTemplates.SignupConfirmation("M", Title, null, null, false, ManageUrl, UnsubscribeUrl).Body,
            EmailTemplates.WaitlistPromoted("M", Title, AugustSaturday, Where, ManageUrl, UnsubscribeUrl).Body,
            EmailTemplates.ActivityReminder("M", Title, AugustSaturday, Where, null, ManageUrl, UnsubscribeUrl).Body,
            EmailTemplates.Welcome("M", "https://example.test/volunteer", ManageUrl, UnsubscribeUrl).Body,
        };

        bodies.Should().AllSatisfy(body => body.Should().Contain(UnsubscribeUrl));
    }
}
