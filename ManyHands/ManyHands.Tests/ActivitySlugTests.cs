using System.Net;
using Dapper;
using ManyHands.Core;
using ManyHands.Data;

namespace ManyHands.Tests;

/// <summary>
/// Readable activity addresses: phillypause.org/events/philly-says-pause. The
/// database makes and keeps them; old links by id redirect for good.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ActivitySlugTests(DatabaseFixture db) : IAsyncLifetime
{
    private static readonly DateTimeOffset Saturday = new(2027, 1, 30, 17, 0, 0, TimeSpan.Zero);

    private PortalFactory _portal = null!;

    private ActivityAdminRepository Admin => new(db.DataSource);

    public Task InitializeAsync()
    {
        _portal = new PortalFactory(db.ConnectionString);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _portal.DisposeAsync();

    private static ActivityFields Fields(string title, DateTimeOffset? startsAt = null, string? slug = null) =>
        new(title, null, ActivityKind.OneTime, "City Hall", null,
            startsAt ?? Saturday, (startsAt ?? Saturday).AddHours(1.5), null, [], slug);

    private static string Unique(string title) => $"{title} {Guid.NewGuid():N}"[..(title.Length + 9)];

    private async Task<string> SlugOfAsync(Guid id) => (await Admin.GetAsync(id))!.Slug;

    [Theory]
    [InlineData("Philly Says Pause", "philly-says-pause")]
    [InlineData("  Stop the AI death race!  ", "stop-the-ai-death-race")]
    [InlineData("Café & Co. — 2027", "cafe-co-2027")]
    [InlineData("!!!", "event")]
    public void A_slug_is_letters_digits_and_single_dashes(string title, string expected)
    {
        Assert.Equal(expected, ActivitySlug.From(title));
    }

    [Fact]
    public void A_slug_is_at_most_sixty_characters_and_never_ends_in_a_dash()
    {
        var slug = ActivitySlug.From(string.Join(' ', Enumerable.Repeat("abcd", 30)));

        Assert.True(slug.Length <= ActivitySlug.MaxLength);
        Assert.False(slug.EndsWith('-'));
    }

    [Theory]
    [InlineData("Philly Says Pause")]
    [InlineData("Teach-in: AI & Jobs (Part 2)")]
    [InlineData("!!!")]
    public async Task The_database_and_the_editor_make_the_same_slug(string title)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();

        var fromDatabase = await conn.ExecuteScalarAsync<string>("SELECT activity_slug_base(@title)", new { title });

        Assert.Equal(ActivitySlug.From(title), fromDatabase);
    }

    [Fact]
    public async Task A_new_activity_takes_its_slug_from_its_title()
    {
        var title = Unique("Rally");

        var id = await Admin.CreateAsync(Fields(title));

        Assert.Equal(ActivitySlug.From(title), await SlugOfAsync(id));
    }

    [Fact]
    public async Task A_taken_slug_gets_the_date_and_then_the_id()
    {
        var title = Unique("Leafleting");
        var first = await Admin.CreateAsync(Fields(title));
        var second = await Admin.CreateAsync(Fields(title));
        var third = await Admin.CreateAsync(Fields(title));

        var baseSlug = ActivitySlug.From(title);
        Assert.Equal(baseSlug, await SlugOfAsync(first));
        Assert.Equal($"{baseSlug}-2027-01-30", await SlugOfAsync(second));
        Assert.Equal($"{baseSlug}-{third.ToString("N")[..8]}", await SlugOfAsync(third));
    }

    [Fact]
    public async Task A_drafts_slug_follows_its_title_or_takes_the_one_typed()
    {
        var id = await Admin.CreateAsync(Fields(Unique("Working title")));
        var renamed = Unique("Final name");

        await Admin.UpdateAsync(id, Fields(renamed, slug: await SlugOfAsync(id)));
        var followed = await SlugOfAsync(id);
        var typed = $"My Own {Guid.NewGuid():N}";
        await Admin.UpdateAsync(id, Fields(renamed, slug: typed));

        Assert.Equal(ActivitySlug.From(renamed), followed);
        Assert.Equal(ActivitySlug.From(typed), await SlugOfAsync(id));
    }

    [Fact]
    public async Task Publishing_freezes_the_slug()
    {
        var id = await Admin.CreateAsync(Fields(Unique("Before")));
        var published = await SlugOfAsync(id);
        await Admin.SetPublishedAsync(id, true);

        await Admin.UpdateAsync(id, Fields(Unique("After"), slug: "something-else"));
        await Admin.SetPublishedAsync(id, false);
        await Admin.UpdateAsync(id, Fields(Unique("Again")));

        Assert.Equal(published, await SlugOfAsync(id));
        Assert.True((await Admin.GetAsync(id))!.SlugFrozen);
    }

    [Fact]
    public async Task The_public_page_is_at_its_readable_address()
    {
        var title = Unique("Readable");
        var id = await TestData.ActivityAsync(db.DataSource, title);

        var page = await _portal.Browser().GetStringAsync(ManyHandsPaths.Activity(ActivitySlug.From(title)));

        Assert.Contains(title, page);
        Assert.Equal(ManyHandsPaths.Activity(ActivitySlug.From(title)), await TestData.PathAsync(db.DataSource, id));
    }

    [Fact]
    public async Task A_drafts_readable_address_is_a_404()
    {
        var title = Unique("Not yet");
        await TestData.ActivityAsync(db.DataSource, title, published: false);

        var response = await _portal.Browser().GetAsync(ManyHandsPaths.Activity(ActivitySlug.From(title)));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task An_old_link_by_id_redirects_for_good_and_keeps_its_tracking_code()
    {
        var id = await TestData.ActivityAsync(db.DataSource, Unique("Old link"));

        var response = await _portal.Browser().GetAsync($"/volunteer/activity/{id}?src=newsguild");

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal($"{await TestData.PathAsync(db.DataSource, id)}?src=newsguild", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task An_old_link_to_a_draft_or_nothing_is_a_404()
    {
        var draft = await TestData.ActivityAsync(db.DataSource, Unique("Draft link"), published: false);

        var toDraft = await _portal.Browser().GetAsync($"/volunteer/activity/{draft}");
        var toNothing = await _portal.Browser().GetAsync($"/volunteer/activity/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, toDraft.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, toNothing.StatusCode);
    }
}
