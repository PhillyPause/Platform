using FluentAssertions;
using ManyHands.Data;
using Website.Portal.Volunteer;

namespace ManyHands.Tests;

/// <summary>
/// An organizer types Philadelphia's clock; the database stores an instant.
/// The conversions, and the spreadsheet cells the participation file writes.
/// </summary>
public sealed class EditorRulesTests
{
    [Fact]
    public void A_summer_evening_is_four_hours_behind_utc()
    {
        var instant = LocalTime.FromLocal(new DateTime(2026, 7, 4, 19, 0, 0));

        instant.Should().Be(new DateTimeOffset(2026, 7, 4, 23, 0, 0, TimeSpan.Zero));
        instant.Offset.Should().Be(TimeSpan.Zero, "Npgsql writes timestamptz from UTC only");
    }

    [Fact]
    public void A_winter_evening_is_five_hours_behind_utc() =>
        LocalTime.FromLocal(new DateTime(2026, 1, 15, 19, 0, 0))
            .Should().Be(new DateTimeOffset(2026, 1, 16, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public void A_time_the_clocks_skip_moves_forward_an_hour()
    {
        // 2:30 AM on 8 March 2026 never happens in Philadelphia.
        var instant = LocalTime.FromLocal(new DateTime(2026, 3, 8, 2, 30, 0));

        LocalTime.ToLocal(instant).Should().Be(new DateTime(2026, 3, 8, 3, 30, 0));
    }

    [Fact]
    public void What_goes_into_the_editor_comes_back_out_unchanged()
    {
        var typed = new DateTime(2026, 11, 3, 18, 30, 0);

        LocalTime.ToLocal(LocalTime.FromLocal(typed)).Should().Be(typed);
    }

    [Theory]
    [InlineData("=SUM(A1)", "'=SUM(A1)")]
    [InlineData("+1 555 0100", "'+1 555 0100")]
    [InlineData("-x", "'-x")]
    [InlineData("@cmd", "'@cmd")]
    [InlineData("Maya Okafor", "Maya Okafor")]
    [InlineData("Okafor, Maya", "\"Okafor, Maya\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("", "")]
    public void A_spreadsheet_cell_is_quoted_and_never_a_formula(string value, string cell) =>
        Csv.Cell(value).Should().Be(cell);
}
