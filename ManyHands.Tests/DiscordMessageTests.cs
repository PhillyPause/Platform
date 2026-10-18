using ManyHands.Core;
using ManyHands.Data.Discord;

namespace ManyHands.Tests;

/// <summary>What the bot posts: the 2000-character ceiling, and what survives it.</summary>
public sealed class DiscordMessageTests
{
    private const string BaseUrl = "https://phillypause.org";

    private static ActivityListItem Activity(string title = "Saturday flyering", string? description = null) => new(
        Guid.NewGuid(), title, description, ActivityKind.OneTime, "Clark Park",
        DateTimeOffset.UtcNow.AddDays(3), DateTimeOffset.UtcNow.AddDays(3).AddHours(2), null,
        SignupCount: 2, Capacity: 10, Tags: ["Flyering"]);

    [Fact]
    public void A_long_description_is_cut_and_the_sign_up_link_survives()
    {
        var activity = Activity(description: string.Join(' ', Enumerable.Repeat("word", 1000)));

        var post = DiscordMessages.Announcement(activity, BaseUrl);

        Assert.True(post.Length <= DiscordMessages.MaxMessageLength);
        Assert.EndsWith($"Sign up: {BaseUrl}{ManyHandsPaths.Activity(activity.Id)}", post);
    }

    [Fact]
    public void A_thread_name_fits_Discord_and_is_never_empty()
    {
        Assert.True(DiscordMessages.ThreadName(Activity(new string('x', 300))).Length <= DiscordMessages.MaxThreadNameLength);
        Assert.Equal("Activity discussion", DiscordMessages.ThreadName(Activity("   ")));
    }

    [Fact]
    public void An_interest_ping_says_why_and_how_to_stop()
    {
        var ping = DiscordMessages.InterestPing(Activity(), BaseUrl);

        Assert.StartsWith("New, and it matches what you said you like doing", ping);
        Assert.Contains($"{BaseUrl}{ManyHandsPaths.Me}", ping);
    }

    [Fact]
    public void Fit_never_splits_a_surrogate_pair()
    {
        var message = new string('a', 1990) + string.Concat(Enumerable.Repeat("😀", 20));

        var fitted = DiscordMessages.Fit(message);

        Assert.True(fitted.Length <= DiscordMessages.MaxMessageLength);
        Assert.False(char.IsHighSurrogate(fitted[^2]));
    }

    [Fact]
    public void A_list_says_how_many_it_left_out()
    {
        var items = Enumerable.Range(0, 100).Select(i => $"• item {i} " + new string('x', 80)).ToList();

        var list = DiscordMessages.List("**Header**", items, "nothing");

        Assert.True(list.Length <= DiscordMessages.MaxMessageLength);
        Assert.Matches(@"and \d+ more\.$", list);
    }

    [Fact]
    public void Times_are_Discord_timestamps_so_each_reader_sees_their_own_zone()
    {
        var at = new DateTimeOffset(2026, 11, 7, 15, 0, 0, TimeSpan.Zero);

        Assert.Equal($"<t:{at.ToUnixTimeSeconds()}:F>", DiscordMessages.When(at));
    }
}
