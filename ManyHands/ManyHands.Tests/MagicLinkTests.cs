using System.Security.Cryptography;
using System.Text;
using Dapper;
using ManyHands.Core;
using ManyHands.Data;

namespace ManyHands.Tests;

/// <summary>
/// A sign-in link is a bearer credential delivered by email, so what matters
/// is that it is single use, short lived, stored only as a hash, and that
/// asking for a new one kills the old.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class MagicLinkTests(DatabaseFixture db)
{
    private AuthTokenRepository Tokens => new(db.DataSource);
    private VolunteerRepository Volunteers => new(db.DataSource);

    private Task<Guid> NewVolunteerAsync() => Volunteers.UpsertByEmailAsync(TestData.NewEmail(), "Test Person");

    [Fact]
    public async Task A_fresh_token_signs_the_volunteer_in()
    {
        var volunteerId = await NewVolunteerAsync();
        var raw = await Tokens.IssueAsync(volunteerId);

        var ok = Assert.IsType<MagicLinkResult.Ok>(await Tokens.RedeemAsync(raw));

        Assert.Equal(volunteerId, ok.VolunteerId);
    }

    [Fact]
    public async Task A_token_works_once()
    {
        var raw = await Tokens.IssueAsync(await NewVolunteerAsync());

        Assert.IsType<MagicLinkResult.Ok>(await Tokens.RedeemAsync(raw));
        Assert.IsType<MagicLinkResult.Invalid>(await Tokens.RedeemAsync(raw));
    }

    [Fact]
    public async Task Asking_for_a_new_link_kills_the_previous_one()
    {
        var volunteerId = await NewVolunteerAsync();
        var first = await Tokens.IssueAsync(volunteerId);
        var second = await Tokens.IssueAsync(volunteerId);

        Assert.IsType<MagicLinkResult.Invalid>(await Tokens.RedeemAsync(first));
        Assert.IsType<MagicLinkResult.Ok>(await Tokens.RedeemAsync(second));
    }

    [Fact]
    public async Task An_expired_token_is_refused()
    {
        var volunteerId = await NewVolunteerAsync();
        var raw = await Tokens.IssueAsync(volunteerId);

        await using (var conn = await db.DataSource.OpenConnectionAsync())
        {
            await conn.ExecuteAsync(
                "UPDATE auth_tokens SET expires_at = now() - interval '1 minute' WHERE volunteer_id = @volunteerId",
                new { volunteerId });
        }

        Assert.IsType<MagicLinkResult.Invalid>(await Tokens.RedeemAsync(raw));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-real-token")]
    public async Task Garbage_is_refused(string raw) =>
        Assert.IsType<MagicLinkResult.Invalid>(await Tokens.RedeemAsync(raw));

    [Fact]
    public async Task Only_the_hash_is_stored()
    {
        var volunteerId = await NewVolunteerAsync();
        var raw = await Tokens.IssueAsync(volunteerId);

        await using var conn = await db.DataSource.OpenConnectionAsync();
        var hashes = (await conn.QueryAsync<byte[]>(
            "SELECT token_hash FROM auth_tokens WHERE volunteer_id = @volunteerId",
            new { volunteerId })).ToList();

        var stored = Assert.Single(hashes);
        Assert.Equal(SHA256.HashData(Encoding.UTF8.GetBytes(raw)), stored);
        Assert.NotEqual(Encoding.UTF8.GetBytes(raw), stored);
    }

    [Fact]
    public async Task Redeeming_marks_the_email_verified()
    {
        var volunteerId = await NewVolunteerAsync();

        await Tokens.RedeemAsync(await Tokens.IssueAsync(volunteerId));

        Assert.NotNull((await Volunteers.GetByIdAsync(volunteerId))?.EmailVerifiedAt);
    }

    [Fact]
    public async Task Only_one_of_several_simultaneous_redemptions_succeeds()
    {
        var raw = await Tokens.IssueAsync(await NewVolunteerAsync());

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Tokens.RedeemAsync(raw)));

        Assert.Single(results.OfType<MagicLinkResult.Ok>());
    }

    [Fact]
    public async Task Unsubscribing_stops_email_but_not_sign_in()
    {
        var volunteerId = await NewVolunteerAsync();
        var volunteer = await Volunteers.GetByIdAsync(volunteerId);
        Assert.NotNull(volunteer);

        Assert.True(await Volunteers.UnsubscribeByCommsKeyAsync(volunteer.CommsKey));

        Assert.False((await Volunteers.GetByIdAsync(volunteerId))!.CanReceiveEmail);
        Assert.IsType<MagicLinkResult.Ok>(await Tokens.RedeemAsync(await Tokens.IssueAsync(volunteerId)));
    }

    // The code in the same email: six digits, bound to the person, once.

    [Fact]
    public async Task A_code_signs_its_volunteer_in_once()
    {
        var volunteerId = await NewVolunteerAsync();
        var code = await Tokens.IssueCodeAsync(volunteerId);

        var ok = Assert.IsType<MagicLinkResult.Ok>(await Tokens.RedeemCodeAsync(volunteerId, code));

        Assert.Equal(volunteerId, ok.VolunteerId);
        Assert.IsType<MagicLinkResult.Invalid>(await Tokens.RedeemCodeAsync(volunteerId, code));
        Assert.NotNull((await Volunteers.GetByIdAsync(volunteerId))?.EmailVerifiedAt);
    }

    [Fact]
    public async Task A_code_works_only_for_the_person_it_was_sent_to()
    {
        var code = await Tokens.IssueCodeAsync(await NewVolunteerAsync());

        Assert.IsType<MagicLinkResult.Invalid>(await Tokens.RedeemCodeAsync(await NewVolunteerAsync(), code));
    }

    [Theory]
    [InlineData("{0} {1}")]
    [InlineData("{0}-{1}")]
    [InlineData(" {0}{1} ")]
    public async Task A_code_is_read_as_the_email_shows_it(string format)
    {
        var volunteerId = await NewVolunteerAsync();
        var code = await Tokens.IssueCodeAsync(volunteerId);

        Assert.IsType<MagicLinkResult.Ok>(
            await Tokens.RedeemCodeAsync(volunteerId, string.Format(format, code[..3], code[3..])));
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12a456")]
    public async Task Anything_but_six_digits_is_refused(string typed) =>
        Assert.IsType<MagicLinkResult.Invalid>(await Tokens.RedeemCodeAsync(await NewVolunteerAsync(), typed));

    [Fact]
    public async Task Asking_again_replaces_the_code()
    {
        var volunteerId = await NewVolunteerAsync();
        var first = await Tokens.IssueCodeAsync(volunteerId);
        var second = await Tokens.IssueCodeAsync(volunteerId);

        // Six digits can repeat; the old one is gone either way, so only the new one counts.
        if (first != second)
            Assert.IsType<MagicLinkResult.Invalid>(await Tokens.RedeemCodeAsync(volunteerId, first));
        Assert.IsType<MagicLinkResult.Ok>(await Tokens.RedeemCodeAsync(volunteerId, second));
    }

    [Fact]
    public async Task An_expired_code_is_refused()
    {
        var volunteerId = await NewVolunteerAsync();
        var code = await Tokens.IssueCodeAsync(volunteerId);

        await using (var conn = await db.DataSource.OpenConnectionAsync())
        {
            await conn.ExecuteAsync(
                "UPDATE auth_tokens SET expires_at = now() - interval '1 minute' WHERE volunteer_id = @volunteerId",
                new { volunteerId });
        }

        Assert.IsType<MagicLinkResult.Invalid>(await Tokens.RedeemCodeAsync(volunteerId, code));
    }

    [Fact]
    public async Task A_forgotten_code_no_longer_works()
    {
        var volunteerId = await NewVolunteerAsync();
        var code = await Tokens.IssueCodeAsync(volunteerId);

        await Tokens.ForgetCodeAsync(volunteerId);

        Assert.IsType<MagicLinkResult.Invalid>(await Tokens.RedeemCodeAsync(volunteerId, code));
    }

    [Fact]
    public async Task One_email_signs_in_once_by_link_or_by_code()
    {
        var byCode = await NewVolunteerAsync();
        var link = await Tokens.IssueAsync(byCode);
        Assert.IsType<MagicLinkResult.Ok>(await Tokens.RedeemCodeAsync(byCode, await Tokens.IssueCodeAsync(byCode)));
        Assert.IsType<MagicLinkResult.Invalid>(await Tokens.RedeemAsync(link));

        var byLink = await NewVolunteerAsync();
        var code = await Tokens.IssueCodeAsync(byLink);
        Assert.IsType<MagicLinkResult.Ok>(await Tokens.RedeemAsync(await Tokens.IssueAsync(byLink)));
        Assert.IsType<MagicLinkResult.Invalid>(await Tokens.RedeemCodeAsync(byLink, code));
    }

    [Fact]
    public async Task A_code_is_stored_only_as_a_hash()
    {
        var volunteerId = await NewVolunteerAsync();
        var code = await Tokens.IssueCodeAsync(volunteerId);

        await using var conn = await db.DataSource.OpenConnectionAsync();
        var stored = Assert.Single(await conn.QueryAsync<byte[]>(
            "SELECT token_hash FROM auth_tokens WHERE volunteer_id = @volunteerId AND purpose = 'sign-in-code'",
            new { volunteerId }));

        Assert.Equal(SHA256.HashData(Encoding.UTF8.GetBytes($"{volunteerId:N}:{code}")), stored);
    }
}
