using ManyHands.Data;
using Website.Portal.Volunteer;

namespace ManyHands.Tests;

/// <summary>
/// The members grid shows each interest as two letters. Every type in the
/// database needs its own, or two columns of chips would read the same.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class InterestCodesTests(DatabaseFixture db)
{
    [Fact]
    public async Task Every_interest_has_two_letters_of_its_own()
    {
        var types = await new ActivityTypeRepository(db.DataSource).GetActiveAsync();

        Assert.NotEmpty(types);
        Assert.All(types, t => Assert.True(InterestCodes.BySlug.ContainsKey(t.Slug), $"{t.Slug} has no code in InterestCodes"));
        var codes = types.Select(t => InterestCodes.For(t.Slug, t.Label)).ToList();
        Assert.All(codes, c => Assert.Matches("^[A-Z]{2}$", c));
        Assert.Equal(codes.Count, codes.Distinct().Count());
    }

    [Theory]
    [InlineData("Website / Tech", "WT")]
    [InlineData("Letter & email writing", "LE")]
    [InlineData("Flyering", "FL")]
    public void A_type_without_a_code_falls_back_to_its_initials(string label, string expected) =>
        Assert.Equal(expected, InterestCodes.FromLabel(label));
}
