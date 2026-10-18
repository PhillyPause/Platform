using System.Text;
using FluentAssertions;
using ManyHands.Data.Lookup;
using ManyHands.Core;

namespace ManyHands.Tests;

/// <summary>
/// The mapping from the public <c>unitedstates/congress-legislators</c> dataset
/// onto <c>federal_officials</c>. It reads a fixture, never the network:
/// a test that downloads Congress is a test that fails on a train.
///
/// The fixture is deliberately fictional. Real members of Congress are public
/// figures, but "no real people in fixtures" is a rule worth
/// keeping bright rather than arguing case by case.
/// </summary>
public sealed class FederalOfficialsSourceTests
{
    private const string HouseMember = "R000111";
    private const string Senator = "S000222";
    private const string NoBioguide = "Wren Ashford";
    private const string NoTerms = "T000444";
    private const string NoOfficialFullName = "A000555";

    /// <summary>
    /// Shaped like the real file, extra fields and all — the ones we ignore are
    /// as much a part of the contract as the ones we read. The House member
    /// carries a prior term so "the current term is the last one" is actually
    /// exercised rather than assumed.
    /// </summary>
    private const string Fixture =
        """
        [
          {
            "id": { "bioguide": "R000111", "govtrack": 400001, "fec": ["H0PA03000"] },
            "name": { "first": "Robin", "last": "Rivera", "official_full": "Robin Rivera" },
            "bio": { "gender": "F", "birthday": "1971-04-02" },
            "terms": [
              {
                "type": "rep", "start": "2019-01-03", "end": "2021-01-03",
                "state": "PA", "district": 5, "party": "Democrat"
              },
              {
                "type": "rep", "start": "2025-01-03", "end": "2027-01-03",
                "state": "PA", "district": 3, "party": "Democrat",
                "url": "https://rivera.house.gov",
                "phone": "202-225-0100",
                "office": "123 Cannon House Office Building",
                "address": "123 Cannon House Office Building Washington DC 20515"
              }
            ]
          },
          {
            "id": { "bioguide": "S000222", "lis": "S400" },
            "name": { "first": "Dana", "last": "Cole", "official_full": "Dana Cole" },
            "terms": [
              {
                "type": "sen", "start": "2023-01-03", "end": "2029-01-03",
                "state": "OR", "class": 2, "state_rank": "junior", "party": "Republican",
                "url": "https://cole.senate.gov",
                "phone": "202-224-0200",
                "office": "456 Hart Senate Office Building"
              }
            ]
          },
          {
            "id": { "govtrack": 400003 },
            "name": { "first": "Wren", "last": "Ashford", "official_full": "Wren Ashford" },
            "terms": [
              {
                "type": "rep", "start": "2025-01-03", "end": "2027-01-03",
                "state": "TX", "district": 7, "party": "Republican"
              }
            ]
          },
          {
            "id": { "bioguide": "T000444" },
            "name": { "first": "Tam", "last": "Okonjo", "official_full": "Tam Okonjo" },
            "terms": []
          },
          {
            "id": { "bioguide": "A000555" },
            "name": { "first": "Alex", "last": "Nakamura" },
            "terms": [
              {
                "type": "rep", "start": "2025-01-03", "end": "2027-01-03",
                "state": "AK", "district": 0, "party": "Independent"
              }
            ]
          }
        ]
        """;

    private static async Task<CongressLegislatorsSource.FetchResult> ParseAsync(string json = Fixture)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return await CongressLegislatorsSource.ParseAsync(stream);
    }

    private static async Task<FederalOfficial> OfficialAsync(string bioguideId)
    {
        var result = await ParseAsync();
        return result.Officials.Single(o => o.BioguideId == bioguideId);
    }

    [Fact]
    public async Task Usable_records_are_mapped_and_the_rest_are_counted_as_skipped()
    {
        var result = await ParseAsync();

        result.Officials.Should().HaveCount(3);
        result.Skipped.Should().Be(2);
    }

    [Fact]
    public async Task A_representative_becomes_a_house_member_with_a_district()
    {
        var official = await OfficialAsync(HouseMember);

        official.Chamber.Should().Be("house");
        official.State.Should().Be("PA");
        official.District.Should().Be("03");
        official.Party.Should().Be("Democrat");
        official.FullName.Should().Be("Robin Rivera");
    }

    [Fact]
    public async Task The_current_term_wins_over_the_ones_before_it()
    {
        // The fixture's earlier term is PA-05; the seat moved to PA-03.
        var official = await OfficialAsync(HouseMember);

        official.District.Should().Be("03");
        official.TermEndsOn.Should().Be(new DateOnly(2027, 1, 3));
    }

    [Fact]
    public async Task Contact_details_come_off_the_current_term()
    {
        var official = await OfficialAsync(HouseMember);

        official.Website.Should().Be("https://rivera.house.gov");
        official.Phone.Should().Be("202-225-0100");
        official.Office.Should().Be("123 Cannon House Office Building");
    }

    [Fact]
    public async Task A_senator_becomes_a_senate_member_with_no_district()
    {
        var official = await OfficialAsync(Senator);

        official.Chamber.Should().Be("senate");
        official.State.Should().Be("OR");
        official.District.Should().BeNull();
        official.Party.Should().Be("Republican");
        official.TermEndsOn.Should().Be(new DateOnly(2029, 1, 3));
    }

    [Fact]
    public async Task An_at_large_district_keeps_its_conventional_zero_code()
    {
        var official = await OfficialAsync(NoOfficialFullName);

        official.District.Should().Be("00");
    }

    [Fact]
    public async Task A_record_with_no_bioguide_id_is_skipped_rather_than_thrown_on()
    {
        // It is the primary key; there is nothing to upsert against without it.
        var result = await ParseAsync();

        result.Officials.Should().NotContain(o => o.FullName == NoBioguide);
    }

    [Fact]
    public async Task A_record_with_no_terms_is_skipped()
    {
        // No term means no chamber and no state, both of which are NOT NULL.
        var result = await ParseAsync();

        result.Officials.Should().NotContain(o => o.BioguideId == NoTerms);
    }

    [Fact]
    public async Task A_missing_official_full_name_falls_back_to_first_and_last()
    {
        var official = await OfficialAsync(NoOfficialFullName);

        official.FullName.Should().Be("Alex Nakamura");
    }

    [Fact]
    public async Task An_unrecognised_chamber_is_skipped_rather_than_guessed_at()
    {
        // The table CHECKs chamber IN ('house', 'senate'); anything else would
        // be rejected by Postgres, so it never leaves the parser.
        var result = await ParseAsync(
            """
            [
              {
                "id": { "bioguide": "X000999" },
                "name": { "official_full": "Sam Delegate" },
                "terms": [ { "type": "gov", "start": "2025-01-03", "end": "2027-01-03", "state": "PA" } ]
              }
            ]
            """);

        result.Officials.Should().BeEmpty();
        result.Skipped.Should().Be(1);
    }

    [Fact]
    public async Task An_empty_dataset_parses_to_nothing_rather_than_failing()
    {
        var result = await ParseAsync("[]");

        result.Officials.Should().BeEmpty();
        result.Skipped.Should().Be(0);
    }
}
